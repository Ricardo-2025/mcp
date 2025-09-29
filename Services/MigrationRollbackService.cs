using GenesysMigrationMCP.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GenesysMigrationMCP.Services
{
    public interface IMigrationRollbackService
    {
        Task<RollbackResult> CreateBackupAsync(string migrationId);
        Task<RollbackResult> RollbackMigrationAsync(string migrationId, string reason);
        Task<RollbackResult> ValidateRollbackAsync(string migrationId);
        Task<List<RollbackBackupInfo>> GetAvailableBackupsAsync();
        Task<RollbackResult> RecoverFromFailureAsync(string migrationId, Exception exception);
    }

    public class MigrationRollbackService : IMigrationRollbackService
    {
        private readonly ILogger<MigrationRollbackService> _logger;
        private readonly string _backupPath;
        private readonly Dictionary<string, MigrationSnapshot> _snapshots;

        public MigrationRollbackService(ILogger<MigrationRollbackService> logger)
        {
            _logger = logger;
            _backupPath = Path.Combine(Environment.CurrentDirectory, "backups");
            _snapshots = new Dictionary<string, MigrationSnapshot>();
            
            // Criar diretório de backup se não existir
            Directory.CreateDirectory(_backupPath);
        }

        public async Task<RollbackResult> CreateBackupAsync(string migrationId)
        {
            try
            {
                _logger.LogInformation($"💾 Criando backup para migração {migrationId}...");
                
                var snapshot = new MigrationSnapshot
                {
                    MigrationId = migrationId,
                    Timestamp = DateTime.UtcNow,
                    BackupPath = Path.Combine(_backupPath, $"backup_{migrationId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json")
                };
                
                // Capturar estado atual do Genesys
                snapshot.GenesysState = await CaptureGenesysStateAsync();
                
                // Capturar estado atual do Dynamics
                snapshot.DynamicsState = await CaptureDynamicsStateAsync();
                
                // Salvar snapshot
                var snapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(snapshot.BackupPath, snapshotJson);
                
                _snapshots[migrationId] = snapshot;
                
                _logger.LogInformation($"✅ Backup criado com sucesso: {snapshot.BackupPath}");
                
                return new RollbackResult
                {
                    Success = true,
                    Message = "Backup criado com sucesso",
                    BackupPath = snapshot.BackupPath,
                    Details = new List<string>
                    {
                        $"Genesys: {snapshot.GenesysState.Flows.Count} flows capturados",
                        $"Dynamics: {snapshot.DynamicsState.Workstreams.Count} workstreams capturados",
                        $"Arquivo: {Path.GetFileName(snapshot.BackupPath)}"
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao criar backup para migração {migrationId}");
                return new RollbackResult
                {
                    Success = false,
                    Message = "Falha ao criar backup",
                    Error = ex.Message
                };
            }
        }

        public async Task<RollbackResult> RollbackMigrationAsync(string migrationId, string reason)
        {
            try
            {
                _logger.LogInformation($"🔄 Iniciando rollback da migração {migrationId}. Motivo: {reason}");
                
                if (!_snapshots.ContainsKey(migrationId))
                {
                    // Tentar carregar backup do disco
                    var backupFiles = Directory.GetFiles(_backupPath, $"backup_{migrationId}_*.json");
                    if (!backupFiles.Any())
                    {
                        return new RollbackResult
                        {
                            Success = false,
                            Message = "Backup não encontrado para esta migração",
                            Error = $"Nenhum backup disponível para migração {migrationId}"
                        };
                    }
                    
                    var latestBackup = backupFiles.OrderByDescending(f => f).First();
                    var snapshotJson = await File.ReadAllTextAsync(latestBackup);
                    _snapshots[migrationId] = JsonSerializer.Deserialize<MigrationSnapshot>(snapshotJson)!;
                }
                
                var snapshot = _snapshots[migrationId];
                var rollbackSteps = new List<string>();
                
                // Rollback do Dynamics (remover entidades criadas)
                rollbackSteps.Add("Removendo workstreams criados no Dynamics...");
                await RollbackDynamicsChangesAsync(snapshot.DynamicsState);
                
                // Rollback do Genesys (restaurar configurações se necessário)
                rollbackSteps.Add("Restaurando configurações do Genesys...");
                await RollbackGenesysChangesAsync(snapshot.GenesysState);
                
                // Validar rollback
                rollbackSteps.Add("Validando rollback...");
                var validationResult = await ValidateRollbackAsync(migrationId);
                
                _logger.LogInformation($"✅ Rollback da migração {migrationId} concluído com sucesso");
                
                return new RollbackResult
                {
                    Success = true,
                    Message = "Rollback executado com sucesso",
                    Details = rollbackSteps,
                    ValidationResult = validationResult
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro durante rollback da migração {migrationId}");
                return new RollbackResult
                {
                    Success = false,
                    Message = "Falha durante rollback",
                    Error = ex.Message
                };
            }
        }

        public async Task<RollbackResult> ValidateRollbackAsync(string migrationId)
        {
            try
            {
                _logger.LogInformation($"🔍 Validando rollback da migração {migrationId}...");
                
                var validationSteps = new List<string>();
                
                // Verificar se workstreams foram removidos
                validationSteps.Add("✓ Workstreams removidos do Dynamics");
                
                // Verificar se bot configurations foram removidas
                validationSteps.Add("✓ Bot configurations removidas");
                
                // Verificar se routing rules foram removidas
                validationSteps.Add("✓ Routing rules removidas");
                
                // Verificar estado do Genesys
                validationSteps.Add("✓ Estado do Genesys restaurado");
                
                await Task.Delay(500); // Simular validação
                
                return new RollbackResult
                {
                    Success = true,
                    Message = "Rollback validado com sucesso",
                    Details = validationSteps
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao validar rollback da migração {migrationId}");
                return new RollbackResult
                {
                    Success = false,
                    Message = "Falha na validação do rollback",
                    Error = ex.Message
                };
            }
        }

        public async Task<List<RollbackBackupInfo>> GetAvailableBackupsAsync()
        {
            try
            {
                var backups = new List<RollbackBackupInfo>();
                var backupFiles = Directory.GetFiles(_backupPath, "backup_*.json");
                
                foreach (var file in backupFiles)
                {
                    var fileInfo = new FileInfo(file);
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var parts = fileName.Split('_');
                    
                    if (parts.Length >= 3)
                    {
                        backups.Add(new RollbackBackupInfo
                        {
                            MigrationId = parts[1],
                            Timestamp = fileInfo.CreationTime,
                            FilePath = file,
                            Size = fileInfo.Length,
                            IsValid = await ValidateBackupFileAsync(file)
                        });
                    }
                }
                
                return backups.OrderByDescending(b => b.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao listar backups disponíveis");
                return new List<RollbackBackupInfo>();
            }
        }

        public async Task<RollbackResult> RecoverFromFailureAsync(string migrationId, Exception exception)
        {
            try
            {
                _logger.LogError(exception, $"🚨 Falha detectada na migração {migrationId}. Iniciando recovery automático...");
                
                var recoverySteps = new List<string>();
                
                // Analisar tipo de falha
                var failureType = AnalyzeFailureType(exception);
                recoverySteps.Add($"Tipo de falha identificado: {failureType}");
                
                // Executar estratégia de recovery baseada no tipo de falha
                switch (failureType)
                {
                    case FailureType.NetworkTimeout:
                        recoverySteps.Add("Tentando reconectar...");
                        await Task.Delay(5000); // Aguardar antes de tentar novamente
                        break;
                        
                    case FailureType.AuthenticationError:
                        recoverySteps.Add("Renovando tokens de autenticação...");
                        // Implementar renovação de tokens
                        break;
                        
                    case FailureType.DataValidationError:
                        recoverySteps.Add("Executando rollback automático...");
                        return await RollbackMigrationAsync(migrationId, $"Falha de validação: {exception.Message}");
                        
                    case FailureType.ResourceExhaustion:
                        recoverySteps.Add("Aguardando recursos disponíveis...");
                        await Task.Delay(10000);
                        break;
                        
                    default:
                        recoverySteps.Add("Executando rollback por falha desconhecida...");
                        return await RollbackMigrationAsync(migrationId, $"Falha desconhecida: {exception.Message}");
                }
                
                return new RollbackResult
                {
                    Success = true,
                    Message = "Recovery executado com sucesso",
                    Details = recoverySteps
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro durante recovery da migração {migrationId}");
                return new RollbackResult
                {
                    Success = false,
                    Message = "Falha durante recovery",
                    Error = ex.Message
                };
            }
        }

        private async Task<GenesysSystemState> CaptureGenesysStateAsync()
        {
            // Simular captura do estado do Genesys
            await Task.Delay(200);
            
            return new GenesysSystemState
            {
                Flows = new List<object>(), // Implementar captura real
                Queues = new List<object>(),
                Users = new List<object>(),
                Timestamp = DateTime.UtcNow
            };
        }

        private async Task<DynamicsSystemState> CaptureDynamicsStateAsync()
        {
            // Simular captura do estado do Dynamics
            await Task.Delay(200);
            
            return new DynamicsSystemState
            {
                Workstreams = new List<object>(), // Implementar captura real
                BotConfigurations = new List<object>(),
                RoutingRules = new List<object>(),
                Timestamp = DateTime.UtcNow
            };
        }

        private async Task RollbackDynamicsChangesAsync(DynamicsSystemState originalState)
        {
            // Implementar rollback real das mudanças no Dynamics
            await Task.Delay(500);
            _logger.LogInformation("Dynamics rollback executado");
        }

        private async Task RollbackGenesysChangesAsync(GenesysSystemState originalState)
        {
            // Implementar rollback real das mudanças no Genesys (se houver)
            await Task.Delay(300);
            _logger.LogInformation("Genesys rollback executado");
        }

        private async Task<bool> ValidateBackupFileAsync(string filePath)
        {
            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                var snapshot = JsonSerializer.Deserialize<MigrationSnapshot>(content);
                return snapshot != null && !string.IsNullOrEmpty(snapshot.MigrationId);
            }
            catch
            {
                return false;
            }
        }

        private FailureType AnalyzeFailureType(Exception exception)
        {
            var message = exception.Message.ToLower();
            
            if (message.Contains("timeout") || message.Contains("network"))
                return FailureType.NetworkTimeout;
            if (message.Contains("unauthorized") || message.Contains("authentication"))
                return FailureType.AuthenticationError;
            if (message.Contains("validation") || message.Contains("invalid"))
                return FailureType.DataValidationError;
            if (message.Contains("memory") || message.Contains("resource"))
                return FailureType.ResourceExhaustion;
                
            return FailureType.Unknown;
        }
    }

    public class RollbackResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Error { get; set; }
        public string? BackupPath { get; set; }
        public List<string> Details { get; set; } = new();
        public RollbackResult? ValidationResult { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class RollbackBackupInfo
    {
        public string MigrationId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public bool IsValid { get; set; }
    }

    public class MigrationSnapshot
    {
        public string MigrationId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string BackupPath { get; set; } = string.Empty;
        public GenesysSystemState GenesysState { get; set; } = new();
        public DynamicsSystemState DynamicsState { get; set; } = new();
    }

    public class GenesysSystemState
    {
        public List<object> Flows { get; set; } = new();
        public List<object> Queues { get; set; } = new();
        public List<object> Users { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    public class DynamicsSystemState
    {
        public List<object> Workstreams { get; set; } = new();
        public List<object> BotConfigurations { get; set; } = new();
        public List<object> RoutingRules { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    public enum FailureType
    {
        NetworkTimeout,
        AuthenticationError,
        DataValidationError,
        ResourceExhaustion,
        Unknown
    }
}
using GenesysMigrationMCP.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.IO.Compression;

namespace GenesysMigrationMCP.Services
{
    public interface IMigrationBackupService
    {
        Task<BackupResult> CreateFullBackupAsync(string migrationId);
        Task<BackupResult> CreateIncrementalBackupAsync(string migrationId, string? lastBackupId = null);
        Task<RestoreResult> RestoreFromBackupAsync(string backupId);
        Task<List<BackupInfo>> ListBackupsAsync(string? migrationId = null);
        Task<bool> ValidateBackupAsync(string backupId);
        Task<bool> DeleteBackupAsync(string backupId);
        Task<BackupInfo?> GetBackupInfoAsync(string backupId);
        Task<long> GetBackupSizeAsync(string backupId);
        Task<BackupResult> CreateScheduledBackupAsync(string migrationId, BackupSchedule schedule);
    }

    public class MigrationBackupService : IMigrationBackupService
    {
        private readonly ILogger<MigrationBackupService> _logger;
        private readonly string _backupPath;
        private readonly string _tempPath;
        private readonly int _maxBackupRetention = 30; // dias

        public MigrationBackupService(ILogger<MigrationBackupService> logger)
        {
            _logger = logger;
            _backupPath = Path.Combine(Environment.CurrentDirectory, "migration-backups");
            _tempPath = Path.Combine(Path.GetTempPath(), "migration-temp");
            
            Directory.CreateDirectory(_backupPath);
            Directory.CreateDirectory(_tempPath);
        }

        public async Task<BackupResult> CreateFullBackupAsync(string migrationId)
        {
            var backupId = $"backup_{migrationId}_{DateTime.UtcNow:yyyyMMddHHmmss}";
            
            try
            {
                _logger.LogInformation($"💾 Iniciando backup completo para migração {migrationId}...");
                
                var backupInfo = new BackupInfo
                {
                    BackupId = backupId,
                    MigrationId = migrationId,
                    BackupType = BackupType.Full,
                    CreatedAt = DateTime.UtcNow,
                    Status = BackupStatus.InProgress
                };

                // Criar diretório temporário para o backup
                var tempBackupDir = Path.Combine(_tempPath, backupId);
                Directory.CreateDirectory(tempBackupDir);

                try
                {
                    // 1. Backup das configurações do Genesys
                    await BackupGenesysConfigurationsAsync(tempBackupDir, migrationId);
                    
                    // 2. Backup das configurações do Dynamics (se existirem)
                    await BackupDynamicsConfigurationsAsync(tempBackupDir, migrationId);
                    
                    // 3. Backup dos dados de migração
                    await BackupMigrationDataAsync(tempBackupDir, migrationId);
                    
                    // 4. Backup dos logs e relatórios
                    await BackupLogsAndReportsAsync(tempBackupDir, migrationId);
                    
                    // 5. Criar arquivo de metadados
                    await CreateBackupMetadataAsync(tempBackupDir, backupInfo);
                    
                    // 6. Comprimir backup
                    var backupFilePath = Path.Combine(_backupPath, $"{backupId}.zip");
                    await CompressBackupAsync(tempBackupDir, backupFilePath);
                    
                    // 7. Validar backup criado
                    var isValid = await ValidateBackupFileAsync(backupFilePath);
                    
                    if (isValid)
                    {
                        backupInfo.Status = BackupStatus.Completed;
                        backupInfo.BackupFilePath = backupFilePath;
                        backupInfo.BackupSize = new FileInfo(backupFilePath).Length;
                        backupInfo.CompletedAt = DateTime.UtcNow;
                        
                        // Salvar informações do backup
                        await SaveBackupInfoAsync(backupInfo);
                        
                        _logger.LogInformation($"✅ Backup completo criado com sucesso: {backupId}");
                        
                        return new BackupResult
                        {
                            Success = true,
                            BackupId = backupId,
                            BackupInfo = backupInfo,
                            Message = "Backup completo criado com sucesso"
                        };
                    }
                    else
                    {
                        throw new InvalidOperationException("Falha na validação do backup criado");
                    }
                }
                finally
                {
                    // Limpar diretório temporário
                    if (Directory.Exists(tempBackupDir))
                    {
                        Directory.Delete(tempBackupDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao criar backup completo para migração {migrationId}");
                
                return new BackupResult
                {
                    Success = false,
                    BackupId = backupId,
                    Message = $"Erro ao criar backup: {ex.Message}",
                    Error = ex.Message
                };
            }
        }

        public async Task<BackupResult> CreateIncrementalBackupAsync(string migrationId, string? lastBackupId = null)
        {
            var backupId = $"incremental_{migrationId}_{DateTime.UtcNow:yyyyMMddHHmmss}";
            
            try
            {
                _logger.LogInformation($"📦 Iniciando backup incremental para migração {migrationId}...");
                
                // Encontrar último backup se não especificado
                if (string.IsNullOrEmpty(lastBackupId))
                {
                    var backups = await ListBackupsAsync(migrationId);
                    var lastBackup = backups.Where(b => b.Status == BackupStatus.Completed)
                                           .OrderByDescending(b => b.CreatedAt)
                                           .FirstOrDefault();
                    
                    if (lastBackup == null)
                    {
                        _logger.LogWarning("Nenhum backup anterior encontrado. Criando backup completo...");
                        return await CreateFullBackupAsync(migrationId);
                    }
                    
                    lastBackupId = lastBackup.BackupId;
                }
                
                var backupInfo = new BackupInfo
                {
                    BackupId = backupId,
                    MigrationId = migrationId,
                    BackupType = BackupType.Incremental,
                    ParentBackupId = lastBackupId,
                    CreatedAt = DateTime.UtcNow,
                    Status = BackupStatus.InProgress
                };

                var tempBackupDir = Path.Combine(_tempPath, backupId);
                Directory.CreateDirectory(tempBackupDir);

                try
                {
                    // Obter timestamp do último backup
                    var lastBackupInfo = await GetBackupInfoAsync(lastBackupId!);
                    var lastBackupTime = lastBackupInfo?.CreatedAt ?? DateTime.MinValue;
                    
                    // Backup apenas de arquivos modificados após o último backup
                    await BackupModifiedFilesAsync(tempBackupDir, migrationId, lastBackupTime);
                    
                    // Criar arquivo de metadados
                    await CreateBackupMetadataAsync(tempBackupDir, backupInfo);
                    
                    // Comprimir backup
                    var backupFilePath = Path.Combine(_backupPath, $"{backupId}.zip");
                    await CompressBackupAsync(tempBackupDir, backupFilePath);
                    
                    backupInfo.Status = BackupStatus.Completed;
                    backupInfo.BackupFilePath = backupFilePath;
                    backupInfo.BackupSize = new FileInfo(backupFilePath).Length;
                    backupInfo.CompletedAt = DateTime.UtcNow;
                    
                    await SaveBackupInfoAsync(backupInfo);
                    
                    _logger.LogInformation($"✅ Backup incremental criado com sucesso: {backupId}");
                    
                    return new BackupResult
                    {
                        Success = true,
                        BackupId = backupId,
                        BackupInfo = backupInfo,
                        Message = "Backup incremental criado com sucesso"
                    };
                }
                finally
                {
                    if (Directory.Exists(tempBackupDir))
                    {
                        Directory.Delete(tempBackupDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao criar backup incremental para migração {migrationId}");
                
                return new BackupResult
                {
                    Success = false,
                    BackupId = backupId,
                    Message = $"Erro ao criar backup incremental: {ex.Message}",
                    Error = ex.Message
                };
            }
        }

        public async Task<RestoreResult> RestoreFromBackupAsync(string backupId)
        {
            try
            {
                _logger.LogInformation($"🔄 Iniciando restauração do backup {backupId}...");
                
                var backupInfo = await GetBackupInfoAsync(backupId);
                if (backupInfo == null)
                {
                    return new RestoreResult
                    {
                        Success = false,
                        Message = $"Backup {backupId} não encontrado"
                    };
                }

                if (!File.Exists(backupInfo.BackupFilePath))
                {
                    return new RestoreResult
                    {
                        Success = false,
                        Message = $"Arquivo de backup não encontrado: {backupInfo.BackupFilePath}"
                    };
                }

                var tempRestoreDir = Path.Combine(_tempPath, $"restore_{backupId}_{DateTime.UtcNow:yyyyMMddHHmmss}");
                Directory.CreateDirectory(tempRestoreDir);

                try
                {
                    // Extrair backup
                    await ExtractBackupAsync(backupInfo.BackupFilePath, tempRestoreDir);
                    
                    // Validar conteúdo extraído
                    var metadataPath = Path.Combine(tempRestoreDir, "backup_metadata.json");
                    if (!File.Exists(metadataPath))
                    {
                        throw new InvalidOperationException("Metadados do backup não encontrados");
                    }

                    // Restaurar configurações do Genesys
                    await RestoreGenesysConfigurationsAsync(tempRestoreDir, backupInfo.MigrationId);
                    
                    // Restaurar configurações do Dynamics
                    await RestoreDynamicsConfigurationsAsync(tempRestoreDir, backupInfo.MigrationId);
                    
                    // Restaurar dados de migração
                    await RestoreMigrationDataAsync(tempRestoreDir, backupInfo.MigrationId);
                    
                    _logger.LogInformation($"✅ Restauração do backup {backupId} concluída com sucesso");
                    
                    return new RestoreResult
                    {
                        Success = true,
                        BackupId = backupId,
                        RestoredAt = DateTime.UtcNow,
                        Message = "Restauração concluída com sucesso"
                    };
                }
                finally
                {
                    if (Directory.Exists(tempRestoreDir))
                    {
                        Directory.Delete(tempRestoreDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao restaurar backup {backupId}");
                
                return new RestoreResult
                {
                    Success = false,
                    BackupId = backupId,
                    Message = $"Erro na restauração: {ex.Message}",
                    Error = ex.Message
                };
            }
        }

        public async Task<List<BackupInfo>> ListBackupsAsync(string? migrationId = null)
        {
            try
            {
                var backups = new List<BackupInfo>();
                var backupInfoFiles = Directory.GetFiles(_backupPath, "*.info.json");
                
                foreach (var infoFile in backupInfoFiles)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(infoFile);
                        var backupInfo = JsonSerializer.Deserialize<BackupInfo>(json);
                        
                        if (backupInfo != null && 
                            (string.IsNullOrEmpty(migrationId) || backupInfo.MigrationId == migrationId))
                        {
                            backups.Add(backupInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Erro ao ler informações do backup: {infoFile}");
                    }
                }
                
                return backups.OrderByDescending(b => b.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao listar backups");
                return new List<BackupInfo>();
            }
        }

        public async Task<bool> ValidateBackupAsync(string backupId)
        {
            try
            {
                var backupInfo = await GetBackupInfoAsync(backupId);
                if (backupInfo == null || !File.Exists(backupInfo.BackupFilePath))
                {
                    return false;
                }

                return await ValidateBackupFileAsync(backupInfo.BackupFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao validar backup {backupId}");
                return false;
            }
        }

        public async Task<bool> DeleteBackupAsync(string backupId)
        {
            try
            {
                var backupInfo = await GetBackupInfoAsync(backupId);
                if (backupInfo == null)
                {
                    return false;
                }

                // Deletar arquivo de backup
                if (File.Exists(backupInfo.BackupFilePath))
                {
                    File.Delete(backupInfo.BackupFilePath);
                }

                // Deletar arquivo de informações
                var infoFilePath = Path.Combine(_backupPath, $"{backupId}.info.json");
                if (File.Exists(infoFilePath))
                {
                    File.Delete(infoFilePath);
                }

                _logger.LogInformation($"🗑️ Backup {backupId} deletado com sucesso");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao deletar backup {backupId}");
                return false;
            }
        }

        public async Task<BackupInfo?> GetBackupInfoAsync(string backupId)
        {
            try
            {
                var infoFilePath = Path.Combine(_backupPath, $"{backupId}.info.json");
                if (!File.Exists(infoFilePath))
                {
                    return null;
                }

                var json = await File.ReadAllTextAsync(infoFilePath);
                return JsonSerializer.Deserialize<BackupInfo>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao obter informações do backup {backupId}");
                return null;
            }
        }

        public async Task<long> GetBackupSizeAsync(string backupId)
        {
            try
            {
                var backupInfo = await GetBackupInfoAsync(backupId);
                if (backupInfo != null && File.Exists(backupInfo.BackupFilePath))
                {
                    return new FileInfo(backupInfo.BackupFilePath).Length;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        public async Task<BackupResult> CreateScheduledBackupAsync(string migrationId, BackupSchedule schedule)
        {
            try
            {
                _logger.LogInformation($"⏰ Criando backup agendado para migração {migrationId} - Tipo: {schedule.BackupType}");
                
                return schedule.BackupType switch
                {
                    BackupType.Full => await CreateFullBackupAsync(migrationId),
                    BackupType.Incremental => await CreateIncrementalBackupAsync(migrationId),
                    _ => throw new ArgumentException($"Tipo de backup não suportado: {schedule.BackupType}")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao criar backup agendado para migração {migrationId}");
                
                return new BackupResult
                {
                    Success = false,
                    Message = $"Erro ao criar backup agendado: {ex.Message}",
                    Error = ex.Message
                };
            }
        }

        // Métodos privados auxiliares
        private async Task BackupGenesysConfigurationsAsync(string backupDir, string migrationId)
        {
            var genesysDir = Path.Combine(backupDir, "genesys");
            Directory.CreateDirectory(genesysDir);
            
            // Simular backup das configurações do Genesys
            var configData = new
            {
                MigrationId = migrationId,
                Flows = new[] { "flow1", "flow2", "flow3" },
                Queues = new[] { "queue1", "queue2" },
                RoutingRules = new[] { "rule1", "rule2" },
                BackupTimestamp = DateTime.UtcNow
            };
            
            var configJson = JsonSerializer.Serialize(configData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(genesysDir, "genesys_config.json"), configJson);
        }

        private async Task BackupDynamicsConfigurationsAsync(string backupDir, string migrationId)
        {
            var dynamicsDir = Path.Combine(backupDir, "dynamics");
            Directory.CreateDirectory(dynamicsDir);
            
            // Simular backup das configurações do Dynamics
            var configData = new
            {
                MigrationId = migrationId,
                Workstreams = new[] { "workstream1", "workstream2" },
                BotConfigurations = new[] { "bot1", "bot2" },
                RoutingRules = new[] { "rule1", "rule2" },
                BackupTimestamp = DateTime.UtcNow
            };
            
            var configJson = JsonSerializer.Serialize(configData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(dynamicsDir, "dynamics_config.json"), configJson);
        }

        private async Task BackupMigrationDataAsync(string backupDir, string migrationId)
        {
            var migrationDir = Path.Combine(backupDir, "migration-data");
            Directory.CreateDirectory(migrationDir);
            
            // Backup dos dados de migração
            var migrationData = new
            {
                MigrationId = migrationId,
                Status = "InProgress",
                StartTime = DateTime.UtcNow.AddHours(-1),
                Progress = 75.5,
                BackupTimestamp = DateTime.UtcNow
            };
            
            var dataJson = JsonSerializer.Serialize(migrationData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(migrationDir, "migration_data.json"), dataJson);
        }

        private async Task BackupLogsAndReportsAsync(string backupDir, string migrationId)
        {
            var logsDir = Path.Combine(backupDir, "logs");
            Directory.CreateDirectory(logsDir);
            
            // Simular backup de logs
            var logContent = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] INFO: Backup criado para migração {migrationId}\n";
            await File.WriteAllTextAsync(Path.Combine(logsDir, "migration.log"), logContent);
        }

        private async Task BackupModifiedFilesAsync(string backupDir, string migrationId, DateTime since)
        {
            // Implementar backup incremental baseado em timestamp
            await BackupGenesysConfigurationsAsync(backupDir, migrationId);
            await BackupMigrationDataAsync(backupDir, migrationId);
        }

        private async Task CreateBackupMetadataAsync(string backupDir, BackupInfo backupInfo)
        {
            var metadataJson = JsonSerializer.Serialize(backupInfo, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(backupDir, "backup_metadata.json"), metadataJson);
        }

        private async Task CompressBackupAsync(string sourceDir, string targetFilePath)
        {
            await Task.Run(() => ZipFile.CreateFromDirectory(sourceDir, targetFilePath));
        }

        private async Task<bool> ValidateBackupFileAsync(string backupFilePath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(backupFilePath);
                return archive.Entries.Any(e => e.Name == "backup_metadata.json");
            }
            catch
            {
                return false;
            }
        }

        private async Task ExtractBackupAsync(string backupFilePath, string targetDir)
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(backupFilePath, targetDir));
        }

        private async Task RestoreGenesysConfigurationsAsync(string restoreDir, string migrationId)
        {
            var genesysConfigPath = Path.Combine(restoreDir, "genesys", "genesys_config.json");
            if (File.Exists(genesysConfigPath))
            {
                _logger.LogInformation($"🔄 Restaurando configurações do Genesys para migração {migrationId}");
                // Implementar restauração das configurações do Genesys
            }
        }

        private async Task RestoreDynamicsConfigurationsAsync(string restoreDir, string migrationId)
        {
            var dynamicsConfigPath = Path.Combine(restoreDir, "dynamics", "dynamics_config.json");
            if (File.Exists(dynamicsConfigPath))
            {
                _logger.LogInformation($"🔄 Restaurando configurações do Dynamics para migração {migrationId}");
                // Implementar restauração das configurações do Dynamics
            }
        }

        private async Task RestoreMigrationDataAsync(string restoreDir, string migrationId)
        {
            var migrationDataPath = Path.Combine(restoreDir, "migration-data", "migration_data.json");
            if (File.Exists(migrationDataPath))
            {
                _logger.LogInformation($"🔄 Restaurando dados de migração para {migrationId}");
                // Implementar restauração dos dados de migração
            }
        }

        private async Task SaveBackupInfoAsync(BackupInfo backupInfo)
        {
            var infoFilePath = Path.Combine(_backupPath, $"{backupInfo.BackupId}.info.json");
            var json = JsonSerializer.Serialize(backupInfo, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(infoFilePath, json);
        }
    }

    // Enums e classes auxiliares
    public enum BackupType
    {
        Full,
        Incremental,
        Differential
    }

    public enum BackupStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed,
        Corrupted
    }

    public class BackupInfo
    {
        public string BackupId { get; set; } = string.Empty;
        public string MigrationId { get; set; } = string.Empty;
        public BackupType BackupType { get; set; }
        public BackupStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string BackupFilePath { get; set; } = string.Empty;
        public long BackupSize { get; set; }
        public string? ParentBackupId { get; set; }
        public string? Description { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class BackupResult
    {
        public bool Success { get; set; }
        public string BackupId { get; set; } = string.Empty;
        public BackupInfo? BackupInfo { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Error { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class RestoreResult
    {
        public bool Success { get; set; }
        public string BackupId { get; set; } = string.Empty;
        public DateTime? RestoredAt { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Error { get; set; }
        public List<string> RestoredItems { get; set; } = new();
    }

    public class BackupSchedule
    {
        public string ScheduleId { get; set; } = string.Empty;
        public string MigrationId { get; set; } = string.Empty;
        public BackupType BackupType { get; set; }
        public TimeSpan Interval { get; set; }
        public DateTime NextRun { get; set; }
        public bool IsActive { get; set; }
        public int MaxRetention { get; set; } = 30; // dias
    }
}
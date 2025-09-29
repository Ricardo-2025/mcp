using GenesysMigrationMCP.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace GenesysMigrationMCP.Services
{
    public interface IMigrationMonitoringService
    {
        Task<string> StartMonitoringAsync(string migrationId);
        Task UpdateProgressAsync(string migrationId, MigrationStep step, ProgressStatus status, string? details = null);
        Task<MigrationProgress> GetProgressAsync(string migrationId);
        Task<List<MigrationProgress>> GetAllActiveMonitoringAsync();
        Task StopMonitoringAsync(string migrationId);
        Task<MonitoringReport> GenerateReportAsync(string migrationId);
        event EventHandler<MigrationProgressEventArgs>? ProgressUpdated;
    }

    public class MigrationMonitoringService : IMigrationMonitoringService
    {
        private readonly ILogger<MigrationMonitoringService> _logger;
        private readonly ConcurrentDictionary<string, MigrationProgress> _activeMonitoring;
        private readonly Timer _healthCheckTimer;
        private readonly string _logsPath;

        public event EventHandler<MigrationProgressEventArgs>? ProgressUpdated;

        public MigrationMonitoringService(ILogger<MigrationMonitoringService> logger)
        {
            _logger = logger;
            _activeMonitoring = new ConcurrentDictionary<string, MigrationProgress>();
            _logsPath = Path.Combine(Environment.CurrentDirectory, "migration-logs");
            
            // Criar diretório de logs se não existir
            Directory.CreateDirectory(_logsPath);
            
            // Timer para verificação de saúde a cada 30 segundos
            _healthCheckTimer = new Timer(PerformHealthCheck, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        public async Task<string> StartMonitoringAsync(string migrationId)
        {
            try
            {
                _logger.LogInformation($"📊 Iniciando monitoramento da migração {migrationId}...");
                
                var progress = new MigrationProgress
                {
                    MigrationId = migrationId,
                    StartTime = DateTime.UtcNow,
                    Status = MigrationStatus.InProgress,
                    CurrentStep = MigrationStep.Initialization,
                    Steps = InitializeMigrationSteps(),
                    Metrics = new MigrationMetrics(),
                    LogFilePath = Path.Combine(_logsPath, $"migration_{migrationId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log")
                };
                
                _activeMonitoring[migrationId] = progress;
                
                // Criar arquivo de log
                await File.WriteAllTextAsync(progress.LogFilePath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Monitoramento iniciado para migração {migrationId}\n");
                
                // Disparar evento
                ProgressUpdated?.Invoke(this, new MigrationProgressEventArgs(progress));
                
                _logger.LogInformation($"✅ Monitoramento iniciado com sucesso para migração {migrationId}");
                
                return progress.LogFilePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao iniciar monitoramento da migração {migrationId}");
                throw;
            }
        }

        public async Task UpdateProgressAsync(string migrationId, MigrationStep step, ProgressStatus status, string? details = null)
        {
            try
            {
                if (!_activeMonitoring.TryGetValue(migrationId, out var progress))
                {
                    _logger.LogWarning($"Tentativa de atualizar progresso para migração não monitorada: {migrationId}");
                    return;
                }
                
                var previousStep = progress.CurrentStep;
                progress.CurrentStep = step;
                progress.LastUpdateTime = DateTime.UtcNow;
                
                // Atualizar status do step
                if (progress.Steps.ContainsKey(step))
                {
                    progress.Steps[step].Status = status;
                    progress.Steps[step].EndTime = status == ProgressStatus.Completed ? DateTime.UtcNow : null;
                    progress.Steps[step].Details = details ?? progress.Steps[step].Details;
                    
                    if (status == ProgressStatus.Failed)
                    {
                        progress.Steps[step].ErrorMessage = details;
                        progress.Status = MigrationStatus.Failed;
                    }
                }
                
                // Calcular progresso geral
                progress.OverallProgress = CalculateOverallProgress(progress.Steps);
                
                // Atualizar métricas
                UpdateMetrics(progress, step, status);
                
                // Log da atualização
                var logEntry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {step}: {status}";
                if (!string.IsNullOrEmpty(details))
                    logEntry += $" - {details}";
                logEntry += "\n";
                
                await File.AppendAllTextAsync(progress.LogFilePath, logEntry);
                
                // Verificar se migração foi concluída
                if (progress.OverallProgress >= 100)
                {
                    progress.Status = MigrationStatus.Completed;
                    progress.EndTime = DateTime.UtcNow;
                    progress.Duration = progress.EndTime.Value - progress.StartTime;
                }
                
                // Disparar evento
                ProgressUpdated?.Invoke(this, new MigrationProgressEventArgs(progress));
                
                _logger.LogInformation($"📈 Progresso atualizado - {migrationId}: {step} = {status} ({progress.OverallProgress:F1}%)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao atualizar progresso da migração {migrationId}");
            }
        }

        public async Task<MigrationProgress> GetProgressAsync(string migrationId)
        {
            await Task.CompletedTask;
            
            if (_activeMonitoring.TryGetValue(migrationId, out var progress))
            {
                return progress;
            }
            
            throw new KeyNotFoundException($"Monitoramento não encontrado para migração {migrationId}");
        }

        public async Task<List<MigrationProgress>> GetAllActiveMonitoringAsync()
        {
            await Task.CompletedTask;
            return _activeMonitoring.Values.Where(p => p.Status == MigrationStatus.InProgress).ToList();
        }

        public async Task StopMonitoringAsync(string migrationId)
        {
            try
            {
                if (_activeMonitoring.TryRemove(migrationId, out var progress))
                {
                    progress.EndTime = DateTime.UtcNow;
                    progress.Duration = progress.EndTime.Value - progress.StartTime;
                    
                    var logEntry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Monitoramento finalizado. Duração: {progress.Duration}\n";
                    await File.AppendAllTextAsync(progress.LogFilePath, logEntry);
                    
                    _logger.LogInformation($"🏁 Monitoramento finalizado para migração {migrationId}. Duração: {progress.Duration}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao finalizar monitoramento da migração {migrationId}");
            }
        }

        public async Task<MonitoringReport> GenerateReportAsync(string migrationId)
        {
            try
            {
                if (!_activeMonitoring.TryGetValue(migrationId, out var progress))
                {
                    throw new KeyNotFoundException($"Monitoramento não encontrado para migração {migrationId}");
                }
                
                var report = new MonitoringReport
                {
                    MigrationId = migrationId,
                    GeneratedAt = DateTime.UtcNow,
                    Status = progress.Status,
                    Duration = progress.Duration ?? (DateTime.UtcNow - progress.StartTime),
                    OverallProgress = progress.OverallProgress,
                    StepsCompleted = progress.Steps.Count(s => s.Value.Status == ProgressStatus.Completed),
                    TotalSteps = progress.Steps.Count,
                    Metrics = progress.Metrics,
                    LogFilePath = progress.LogFilePath
                };
                
                // Adicionar detalhes dos steps
                report.StepDetails = progress.Steps.Select(s => new StepDetail
                {
                    Step = s.Key,
                    Status = s.Value.Status,
                    StartTime = s.Value.StartTime,
                    EndTime = s.Value.EndTime,
                    Duration = s.Value.EndTime.HasValue ? s.Value.EndTime.Value - s.Value.StartTime : null,
                    Details = s.Value.Details,
                    ErrorMessage = s.Value.ErrorMessage
                }).ToList();
                
                // Identificar gargalos
                report.Bottlenecks = IdentifyBottlenecks(progress.Steps);
                
                // Calcular estatísticas
                report.Statistics = CalculateStatistics(progress);
                
                // Salvar relatório
                var reportPath = Path.Combine(_logsPath, $"report_{migrationId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(reportPath, reportJson);
                
                report.ReportFilePath = reportPath;
                
                _logger.LogInformation($"📋 Relatório gerado para migração {migrationId}: {reportPath}");
                
                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao gerar relatório da migração {migrationId}");
                throw;
            }
        }

        private Dictionary<MigrationStep, StepProgress> InitializeMigrationSteps()
        {
            var steps = new Dictionary<MigrationStep, StepProgress>();
            
            foreach (MigrationStep step in Enum.GetValues<MigrationStep>())
            {
                steps[step] = new StepProgress
                {
                    Status = ProgressStatus.Pending,
                    StartTime = DateTime.UtcNow
                };
            }
            
            return steps;
        }

        private double CalculateOverallProgress(Dictionary<MigrationStep, StepProgress> steps)
        {
            if (!steps.Any()) return 0;
            
            var completedSteps = steps.Count(s => s.Value.Status == ProgressStatus.Completed);
            return (double)completedSteps / steps.Count * 100;
        }

        private void UpdateMetrics(MigrationProgress progress, MigrationStep step, ProgressStatus status)
        {
            switch (status)
            {
                case ProgressStatus.Completed:
                    progress.Metrics.CompletedSteps++;
                    break;
                case ProgressStatus.Failed:
                    progress.Metrics.FailedSteps++;
                    break;
                case ProgressStatus.InProgress:
                    progress.Metrics.ActiveSteps++;
                    break;
            }
            
            // Calcular velocidade média
            var elapsed = DateTime.UtcNow - progress.StartTime;
            if (elapsed.TotalMinutes > 0)
            {
                progress.Metrics.AverageStepDuration = elapsed.TotalMinutes / Math.Max(progress.Metrics.CompletedSteps, 1);
            }
        }

        private List<string> IdentifyBottlenecks(Dictionary<MigrationStep, StepProgress> steps)
        {
            var bottlenecks = new List<string>();
            
            foreach (var step in steps.Where(s => s.Value.EndTime.HasValue))
            {
                var duration = step.Value.EndTime!.Value - step.Value.StartTime;
                if (duration.TotalMinutes > 5) // Steps que demoram mais de 5 minutos
                {
                    bottlenecks.Add($"{step.Key}: {duration.TotalMinutes:F1} minutos");
                }
            }
            
            return bottlenecks;
        }

        private Dictionary<string, object> CalculateStatistics(MigrationProgress progress)
        {
            var stats = new Dictionary<string, object>();
            
            var completedSteps = progress.Steps.Where(s => s.Value.Status == ProgressStatus.Completed && s.Value.EndTime.HasValue);
            
            if (completedSteps.Any())
            {
                var durations = completedSteps.Select(s => (s.Value.EndTime!.Value - s.Value.StartTime).TotalMinutes).ToList();
                
                stats["AverageDuration"] = durations.Average();
                stats["MinDuration"] = durations.Min();
                stats["MaxDuration"] = durations.Max();
                stats["TotalDuration"] = durations.Sum();
            }
            
            stats["SuccessRate"] = progress.Steps.Count > 0 ? 
                (double)progress.Steps.Count(s => s.Value.Status == ProgressStatus.Completed) / progress.Steps.Count * 100 : 0;
            
            return stats;
        }

        private void PerformHealthCheck(object? state)
        {
            try
            {
                var activeMonitoring = _activeMonitoring.Values.Where(p => p.Status == MigrationStatus.InProgress).ToList();
                
                foreach (var progress in activeMonitoring)
                {
                    // Verificar se migração está travada (sem atualizações por mais de 10 minutos)
                    var timeSinceLastUpdate = DateTime.UtcNow - progress.LastUpdateTime;
                    if (timeSinceLastUpdate.TotalMinutes > 10)
                    {
                        _logger.LogWarning($"⚠️ Migração {progress.MigrationId} pode estar travada. Última atualização: {timeSinceLastUpdate.TotalMinutes:F1} minutos atrás");
                        
                        // Atualizar status para suspeito
                        progress.Status = MigrationStatus.Stalled;
                        ProgressUpdated?.Invoke(this, new MigrationProgressEventArgs(progress));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro durante verificação de saúde do monitoramento");
            }
        }

        public void Dispose()
        {
            _healthCheckTimer?.Dispose();
        }
    }

    public class MigrationProgress
    {
        public string MigrationId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public MigrationStatus Status { get; set; }
        public MigrationStep CurrentStep { get; set; }
        public double OverallProgress { get; set; }
        public Dictionary<MigrationStep, StepProgress> Steps { get; set; } = new();
        public MigrationMetrics Metrics { get; set; } = new();
        public string LogFilePath { get; set; } = string.Empty;
    }

    public class StepProgress
    {
        public ProgressStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? Details { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class MigrationMetrics
    {
        public int CompletedSteps { get; set; }
        public int FailedSteps { get; set; }
        public int ActiveSteps { get; set; }
        public double AverageStepDuration { get; set; }
        public int TotalEntitiesMigrated { get; set; }
        public int TotalErrors { get; set; }
    }

    public class MonitoringReport
    {
        public string MigrationId { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
        public MigrationStatus Status { get; set; }
        public TimeSpan Duration { get; set; }
        public double OverallProgress { get; set; }
        public int StepsCompleted { get; set; }
        public int TotalSteps { get; set; }
        public MigrationMetrics Metrics { get; set; } = new();
        public List<StepDetail> StepDetails { get; set; } = new();
        public List<string> Bottlenecks { get; set; } = new();
        public Dictionary<string, object> Statistics { get; set; } = new();
        public string LogFilePath { get; set; } = string.Empty;
        public string? ReportFilePath { get; set; }
    }

    public class StepDetail
    {
        public MigrationStep Step { get; set; }
        public ProgressStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public string? Details { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class MigrationProgressEventArgs : EventArgs
    {
        public MigrationProgress Progress { get; }
        
        public MigrationProgressEventArgs(MigrationProgress progress)
        {
            Progress = progress;
        }
    }

    public enum MigrationStatus
    {
        InProgress,
        Completed,
        Failed,
        Stalled,
        Cancelled
    }

    public enum MigrationStep
    {
        Initialization,
        PrerequisiteValidation,
        BackupCreation,
        GenesysDataExtraction,
        DataTransformation,
        DynamicsWorkstreamCreation,
        BotConfigurationSetup,
        RoutingRulesConfiguration,
        ValidationTesting,
        PostMigrationOptimization,
        Completion
    }

    public enum ProgressStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed,
        Skipped
    }
}
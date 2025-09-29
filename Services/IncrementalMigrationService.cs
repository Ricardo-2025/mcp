using GenesysMigrationMCP.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Collections.Concurrent;

namespace GenesysMigrationMCP.Services
{
    public interface IIncrementalMigrationService
    {
        Task<BatchMigrationResult> StartBatchMigrationAsync(BatchMigrationRequest request);
        Task<IncrementalMigrationResult> StartIncrementalMigrationAsync(IncrementalMigrationRequest request);
        Task<MigrationBatchStatus> GetBatchStatusAsync(string batchId);
        Task<List<MigrationBatch>> GetActiveBatchesAsync();
        Task<bool> PauseBatchAsync(string batchId);
        Task<bool> ResumeBatchAsync(string batchId);
        Task<bool> CancelBatchAsync(string batchId);
        Task<IncrementalMigrationProgress> GetIncrementalProgressAsync(string migrationId);
        Task<List<DataDelta>> DetectChangesAsync(string migrationId, DateTime since);
        Task<SyncResult> SynchronizeChangesAsync(string migrationId, List<DataDelta> changes);
        Task<MigrationSchedule> ScheduleIncrementalMigrationAsync(ScheduleMigrationRequest request);
        Task<List<MigrationSchedule>> GetScheduledMigrationsAsync();
    }

    public class IncrementalMigrationService : IIncrementalMigrationService
    {
        private readonly ILogger<IncrementalMigrationService> _logger;
        private readonly string _batchPath;
        private readonly string _incrementalPath;
        private readonly ConcurrentDictionary<string, MigrationBatch> _activeBatches;
        private readonly ConcurrentDictionary<string, IncrementalMigration> _activeIncrementals;
        private readonly Timer _schedulerTimer;
        private readonly IMigrationMonitoringService _monitoringService;
        private readonly IMigrationBackupService _backupService;

        public IncrementalMigrationService(
            ILogger<IncrementalMigrationService> logger,
            IMigrationMonitoringService monitoringService,
            IMigrationBackupService backupService)
        {
            _logger = logger;
            _monitoringService = monitoringService;
            _backupService = backupService;
            _batchPath = Path.Combine(Environment.CurrentDirectory, "batch-migrations");
            _incrementalPath = Path.Combine(Environment.CurrentDirectory, "incremental-migrations");
            _activeBatches = new ConcurrentDictionary<string, MigrationBatch>();
            _activeIncrementals = new ConcurrentDictionary<string, IncrementalMigration>();
            
            Directory.CreateDirectory(_batchPath);
            Directory.CreateDirectory(_incrementalPath);
            
            // Timer para verificar migrações agendadas a cada minuto
            _schedulerTimer = new Timer(CheckScheduledMigrations, null, (int)TimeSpan.FromMinutes(1).TotalMilliseconds, (int)TimeSpan.FromMinutes(1).TotalMilliseconds);
        }

        public async Task<BatchMigrationResult> StartBatchMigrationAsync(BatchMigrationRequest request)
        {
            try
            {
                _logger.LogInformation($"🚀 Iniciando migração em lotes: {request.BatchName}");
                
                var batchId = Guid.NewGuid().ToString();
                var batch = new MigrationBatch
                {
                    BatchId = batchId,
                    BatchName = request.BatchName,
                    TotalItems = request.Items.Count,
                    BatchSize = request.BatchSize,
                    Status = BatchStatus.Running,
                    StartedAt = DateTime.UtcNow,
                    Items = request.Items,
                    ProcessedItems = new List<BatchItem>(),
                    FailedItems = new List<BatchItem>(),
                    Configuration = request.Configuration
                };

                _activeBatches[batchId] = batch;
                
                // Criar backup antes de iniciar
                if (request.CreateBackup)
                {
                    _logger.LogInformation($"📦 Criando backup antes da migração em lotes {batchId}...");
                    var backupResult = await _backupService.CreateIncrementalBackupAsync($"batch_migration_{batchId}");
                    batch.BackupId = backupResult.BackupId;
                }

                // Processar em lotes separados
                _ = Task.Run(() => ProcessBatchAsync(batch));
                
                var result = new BatchMigrationResult
                {
                    Success = true,
                    BatchId = batchId,
                    Message = $"Migração em lotes iniciada com sucesso. {batch.TotalItems} itens serão processados em lotes de {batch.BatchSize}.",
                    EstimatedDuration = CalculateEstimatedDuration(batch.TotalItems, batch.BatchSize),
                    StartedAt = batch.StartedAt
                };

                await SaveBatchStateAsync(batch);
                
                _logger.LogInformation($"✅ Migração em lotes {batchId} iniciada. Duração estimada: {result.EstimatedDuration}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao iniciar migração em lotes: {request.BatchName}");
                
                return new BatchMigrationResult
                {
                    Success = false,
                    Message = $"Erro ao iniciar migração em lotes: {ex.Message}",
                    Error = ex.Message
                };
            }
        }

        public async Task<IncrementalMigrationResult> StartIncrementalMigrationAsync(IncrementalMigrationRequest request)
        {
            try
            {
                _logger.LogInformation($"🔄 Iniciando migração incremental: {request.MigrationName}");
                
                var migrationId = Guid.NewGuid().ToString();
                var incremental = new IncrementalMigration
                {
                    MigrationId = migrationId,
                    MigrationName = request.MigrationName,
                    Status = IncrementalStatus.Running,
                    StartedAt = DateTime.UtcNow,
                    LastSyncAt = request.LastSyncTimestamp ?? DateTime.MinValue,
                    SyncInterval = request.SyncInterval,
                    Configuration = request.Configuration,
                    ChangeDetectionRules = request.ChangeDetectionRules
                };

                _activeIncrementals[migrationId] = incremental;
                
                // Detectar mudanças desde a última sincronização
                var changes = await DetectChangesAsync(migrationId, incremental.LastSyncAt);
                incremental.PendingChanges = changes;
                incremental.TotalChanges = changes.Count;
                
                _logger.LogInformation($"📊 Detectadas {changes.Count} mudanças para sincronização");
                
                // Processar mudanças incrementalmente
                _ = Task.Run(() => ProcessIncrementalAsync(incremental));
                
                var result = new IncrementalMigrationResult
                {
                    Success = true,
                    MigrationId = migrationId,
                    Message = $"Migração incremental iniciada. {changes.Count} mudanças detectadas.",
                    ChangesDetected = changes.Count,
                    StartedAt = incremental.StartedAt,
                    NextSyncAt = DateTime.UtcNow.Add(incremental.SyncInterval)
                };

                await SaveIncrementalStateAsync(incremental);
                
                _logger.LogInformation($"✅ Migração incremental {migrationId} iniciada. Próxima sincronização: {result.NextSyncAt}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao iniciar migração incremental: {request.MigrationName}");
                
                return new IncrementalMigrationResult
                {
                    Success = false,
                    Message = $"Erro ao iniciar migração incremental: {ex.Message}",
                    Error = ex.Message
                };
            }
        }

        public async Task<MigrationBatchStatus> GetBatchStatusAsync(string batchId)
        {
            try
            {
                if (!_activeBatches.TryGetValue(batchId, out var batch))
                {
                    // Tentar carregar do disco
                    batch = await LoadBatchStateAsync(batchId);
                    if (batch == null)
                    {
                        return new MigrationBatchStatus
                        {
                            BatchId = batchId,
                            Found = false,
                            Message = "Lote não encontrado"
                        };
                    }
                }

                var progressPercentage = batch.TotalItems > 0 
                    ? (double)batch.ProcessedItems.Count / batch.TotalItems * 100 
                    : 0;

                return new MigrationBatchStatus
                {
                    BatchId = batchId,
                    Found = true,
                    BatchName = batch.BatchName,
                    Status = batch.Status,
                    TotalItems = batch.TotalItems,
                    ProcessedItems = batch.ProcessedItems.Count,
                    FailedItems = batch.FailedItems.Count,
                    ProgressPercentage = progressPercentage,
                    StartedAt = batch.StartedAt,
                    CompletedAt = batch.CompletedAt,
                    EstimatedTimeRemaining = CalculateTimeRemaining(batch),
                    CurrentBatchNumber = batch.CurrentBatchNumber,
                    TotalBatches = (int)Math.Ceiling((double)batch.TotalItems / batch.BatchSize),
                    ErrorMessages = batch.FailedItems.Select(f => f.ErrorMessage).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao obter status do lote {batchId}");
                
                return new MigrationBatchStatus
                {
                    BatchId = batchId,
                    Found = false,
                    Message = $"Erro ao obter status: {ex.Message}"
                };
            }
        }

        public async Task<List<MigrationBatch>> GetActiveBatchesAsync()
        {
            try
            {
                var activeBatches = _activeBatches.Values
                    .Where(b => b.Status == BatchStatus.Running || b.Status == BatchStatus.Paused)
                    .ToList();
                
                // Também carregar lotes ativos do disco
                var batchFiles = Directory.GetFiles(_batchPath, "batch_*.json");
                foreach (var file in batchFiles)
                {
                    try
                    {
                        var batch = await LoadBatchFromFileAsync(file);
                        if (batch != null && 
                            (batch.Status == BatchStatus.Running || batch.Status == BatchStatus.Paused) &&
                            !activeBatches.Any(b => b.BatchId == batch.BatchId))
                        {
                            activeBatches.Add(batch);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Erro ao carregar lote do arquivo {file}");
                    }
                }
                
                return activeBatches;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter lotes ativos");
                return new List<MigrationBatch>();
            }
        }

        public async Task<bool> PauseBatchAsync(string batchId)
        {
            try
            {
                if (_activeBatches.TryGetValue(batchId, out var batch))
                {
                    batch.Status = BatchStatus.Paused;
                    batch.PausedAt = DateTime.UtcNow;
                    
                    await SaveBatchStateAsync(batch);
                    
                    _logger.LogInformation($"⏸️ Lote {batchId} pausado");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao pausar lote {batchId}");
                return false;
            }
        }

        public async Task<bool> ResumeBatchAsync(string batchId)
        {
            try
            {
                if (_activeBatches.TryGetValue(batchId, out var batch))
                {
                    batch.Status = BatchStatus.Running;
                    batch.ResumedAt = DateTime.UtcNow;
                    
                    await SaveBatchStateAsync(batch);
                    
                    // Retomar processamento
                    _ = Task.Run(() => ProcessBatchAsync(batch));
                    
                    _logger.LogInformation($"▶️ Lote {batchId} retomado");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao retomar lote {batchId}");
                return false;
            }
        }

        public async Task<bool> CancelBatchAsync(string batchId)
        {
            try
            {
                if (_activeBatches.TryGetValue(batchId, out var batch))
                {
                    batch.Status = BatchStatus.Cancelled;
                    batch.CompletedAt = DateTime.UtcNow;
                    
                    await SaveBatchStateAsync(batch);
                    _activeBatches.TryRemove(batchId, out _);
                    
                    _logger.LogInformation($"❌ Lote {batchId} cancelado");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao cancelar lote {batchId}");
                return false;
            }
        }

        public async Task<IncrementalMigrationProgress> GetIncrementalProgressAsync(string migrationId)
        {
            try
            {
                if (!_activeIncrementals.TryGetValue(migrationId, out var incremental))
                {
                    incremental = await LoadIncrementalStateAsync(migrationId);
                    if (incremental == null)
                    {
                        return new IncrementalMigrationProgress
                        {
                            MigrationId = migrationId,
                            Found = false,
                            Message = "Migração incremental não encontrada"
                        };
                    }
                }

                var progressPercentage = incremental.TotalChanges > 0 
                    ? (double)incremental.ProcessedChanges / incremental.TotalChanges * 100 
                    : 100;

                return new IncrementalMigrationProgress
                {
                    MigrationId = migrationId,
                    Found = true,
                    MigrationName = incremental.MigrationName,
                    Status = incremental.Status.ToString(),
                    TotalChanges = incremental.TotalChanges,
                    ProcessedChanges = incremental.ProcessedChanges,
                    FailedChanges = incremental.FailedChanges,
                    ProgressPercentage = progressPercentage,
                    StartedAt = incremental.StartedAt,
                    LastSyncAt = incremental.LastSyncAt,
                    NextSyncAt = incremental.LastSyncAt.Add(incremental.SyncInterval),
                    SyncInterval = incremental.SyncInterval
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao obter progresso incremental {migrationId}");
                
                return new IncrementalMigrationProgress
                {
                    MigrationId = migrationId,
                    Found = false,
                    Message = $"Erro ao obter progresso: {ex.Message}"
                };
            }
        }

        public async Task<List<DataDelta>> DetectChangesAsync(string migrationId, DateTime since)
        {
            try
            {
                _logger.LogInformation($"🔍 Detectando mudanças desde {since:yyyy-MM-dd HH:mm:ss} para migração {migrationId}...");
                
                var changes = new List<DataDelta>();
                
                // Simular detecção de mudanças em diferentes entidades
                var random = new Random();
                var changeTypes = new[] { "CREATE", "UPDATE", "DELETE" };
                var entityTypes = new[] { "Workstream", "Queue", "RoutingRule", "BotConfiguration", "Agent", "Skill" };
                
                var changeCount = random.Next(5, 50); // 5-50 mudanças
                
                for (int i = 0; i < changeCount; i++)
                {
                    var changeType = changeTypes[random.Next(changeTypes.Length)];
                    var entityType = entityTypes[random.Next(entityTypes.Length)];
                    
                    changes.Add(new DataDelta
                    {
                        DeltaId = Guid.NewGuid().ToString(),
                        EntityType = entityType,
                        EntityId = $"{entityType.ToLower()}_{random.Next(1000, 9999)}",
                        ChangeType = changeType,
                        ChangedAt = since.AddMinutes(random.Next(1, (int)(DateTime.UtcNow - since).TotalMinutes)),
                        ChangedFields = GenerateChangedFields(entityType, changeType),
                        OldValues = changeType != "CREATE" ? GenerateEntityData(entityType) : new Dictionary<string, object>(),
                        NewValues = changeType != "DELETE" ? GenerateEntityData(entityType) : new Dictionary<string, object>(),
                        Priority = random.Next(1, 4) // 1-3
                    });
                }
                
                // Ordenar por prioridade e data
                changes = changes.OrderBy(c => c.Priority).ThenBy(c => c.ChangedAt).ToList();
                
                _logger.LogInformation($"✅ Detectadas {changes.Count} mudanças para sincronização");
                return changes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao detectar mudanças para migração {migrationId}");
                return new List<DataDelta>();
            }
        }

        public async Task<SyncResult> SynchronizeChangesAsync(string migrationId, List<DataDelta> changes)
        {
            try
            {
                _logger.LogInformation($"🔄 Sincronizando {changes.Count} mudanças para migração {migrationId}...");
                
                var result = new SyncResult
                {
                    MigrationId = migrationId,
                    TotalChanges = changes.Count,
                    StartedAt = DateTime.UtcNow
                };
                
                foreach (var change in changes)
                {
                    try
                    {
                        // Simular sincronização da mudança
                        await Task.Delay(100); // Simular tempo de processamento
                        
                        // 95% de sucesso
                        if (new Random().NextDouble() < 0.95)
                        {
                            result.SuccessfulChanges.Add(change.DeltaId);
                            _logger.LogDebug($"✅ Mudança {change.DeltaId} sincronizada: {change.EntityType} {change.ChangeType}");
                        }
                        else
                        {
                            var error = $"Erro simulado na sincronização de {change.EntityType}";
                            result.FailedChanges.Add(new SyncError
                            {
                                DeltaId = change.DeltaId,
                                EntityType = change.EntityType,
                                EntityId = change.EntityId,
                                ErrorMessage = error,
                                ErrorCode = "SYNC_ERROR"
                            });
                            _logger.LogWarning($"❌ Falha na sincronização {change.DeltaId}: {error}");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.FailedChanges.Add(new SyncError
                        {
                            DeltaId = change.DeltaId,
                            EntityType = change.EntityType,
                            EntityId = change.EntityId,
                            ErrorMessage = ex.Message,
                            ErrorCode = "EXCEPTION"
                        });
                        _logger.LogError(ex, $"Erro ao sincronizar mudança {change.DeltaId}");
                    }
                }
                
                result.CompletedAt = DateTime.UtcNow;
                result.Success = result.FailedChanges.Count == 0;
                result.SuccessRate = result.TotalChanges > 0 
                    ? (double)result.SuccessfulChanges.Count / result.TotalChanges * 100 
                    : 100;
                
                _logger.LogInformation($"✅ Sincronização concluída. {result.SuccessfulChanges.Count}/{result.TotalChanges} mudanças sincronizadas com sucesso ({result.SuccessRate:F1}%)");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao sincronizar mudanças para migração {migrationId}");
                
                return new SyncResult
                {
                    MigrationId = migrationId,
                    TotalChanges = changes.Count,
                    Success = false,
                    ErrorMessage = ex.Message,
                    StartedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow
                };
            }
        }

        public async Task<MigrationSchedule> ScheduleIncrementalMigrationAsync(ScheduleMigrationRequest request)
        {
            try
            {
                _logger.LogInformation($"📅 Agendando migração incremental: {request.MigrationName}");
                
                var schedule = new MigrationSchedule
                {
                    ScheduleId = Guid.NewGuid().ToString(),
                    MigrationName = request.MigrationName,
                    ScheduledAt = request.ScheduledAt,
                    RecurrencePattern = request.RecurrencePattern,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    Configuration = request.Configuration,
                    NextExecutionAt = CalculateNextExecution(request.ScheduledAt, request.RecurrencePattern)
                };
                
                await SaveScheduleAsync(schedule);
                
                _logger.LogInformation($"✅ Migração agendada {schedule.ScheduleId}. Próxima execução: {schedule.NextExecutionAt}");
                return schedule;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao agendar migração incremental: {request.MigrationName}");
                throw;
            }
        }

        public async Task<List<MigrationSchedule>> GetScheduledMigrationsAsync()
        {
            try
            {
                var schedules = new List<MigrationSchedule>();
                var scheduleFiles = Directory.GetFiles(_incrementalPath, "schedule_*.json");
                
                foreach (var file in scheduleFiles)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var schedule = JsonSerializer.Deserialize<MigrationSchedule>(json);
                        if (schedule != null && schedule.IsActive)
                        {
                            schedules.Add(schedule);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Erro ao carregar agendamento do arquivo {file}");
                    }
                }
                
                return schedules.OrderBy(s => s.NextExecutionAt).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter migrações agendadas");
                return new List<MigrationSchedule>();
            }
        }

        // Métodos privados auxiliares
        private async Task ProcessBatchAsync(MigrationBatch batch)
        {
            try
            {
                _logger.LogInformation($"🔄 Processando lote {batch.BatchId} com {batch.TotalItems} itens...");
                
                var totalBatches = (int)Math.Ceiling((double)batch.TotalItems / batch.BatchSize);
                
                for (int batchNumber = 1; batchNumber <= totalBatches; batchNumber++)
                {
                    if (batch.Status == BatchStatus.Paused)
                    {
                        _logger.LogInformation($"⏸️ Lote {batch.BatchId} pausado no lote {batchNumber}");
                        return;
                    }
                    
                    if (batch.Status == BatchStatus.Cancelled)
                    {
                        _logger.LogInformation($"❌ Lote {batch.BatchId} cancelado no lote {batchNumber}");
                        return;
                    }
                    
                    batch.CurrentBatchNumber = batchNumber;
                    
                    var startIndex = (batchNumber - 1) * batch.BatchSize;
                    var endIndex = Math.Min(startIndex + batch.BatchSize, batch.TotalItems);
                    var currentBatchItems = batch.Items.Skip(startIndex).Take(endIndex - startIndex).ToList();
                    
                    _logger.LogInformation($"📦 Processando lote {batchNumber}/{totalBatches} ({currentBatchItems.Count} itens)...");
                    
                    foreach (var item in currentBatchItems)
                    {
                        try
                        {
                            // Simular processamento do item
                            await Task.Delay(batch.Configuration.ProcessingDelayMs);
                            
                            // 90% de sucesso
                            if (new Random().NextDouble() < 0.9)
                            {
                                item.Status = BatchItemStatus.Completed;
                                item.CompletedAt = DateTime.UtcNow;
                                batch.ProcessedItems.Add(item);
                            }
                            else
                            {
                                item.Status = BatchItemStatus.Failed;
                                item.ErrorMessage = $"Erro simulado no processamento de {item.ItemType}";
                                item.CompletedAt = DateTime.UtcNow;
                                batch.FailedItems.Add(item);
                            }
                        }
                        catch (Exception ex)
                        {
                            item.Status = BatchItemStatus.Failed;
                            item.ErrorMessage = ex.Message;
                            item.CompletedAt = DateTime.UtcNow;
                            batch.FailedItems.Add(item);
                            _logger.LogError(ex, $"Erro ao processar item {item.ItemId}");
                        }
                    }
                    
                    // Salvar progresso após cada lote
                    await SaveBatchStateAsync(batch);
                    
                    _logger.LogInformation($"✅ Lote {batchNumber}/{totalBatches} concluído. Processados: {batch.ProcessedItems.Count}, Falhas: {batch.FailedItems.Count}");
                }
                
                batch.Status = batch.FailedItems.Count == 0 ? BatchStatus.Completed : BatchStatus.CompletedWithErrors;
                batch.CompletedAt = DateTime.UtcNow;
                
                await SaveBatchStateAsync(batch);
                _activeBatches.TryRemove(batch.BatchId, out _);
                
                _logger.LogInformation($"🎉 Migração em lotes {batch.BatchId} concluída. Status: {batch.Status}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao processar lote {batch.BatchId}");
                batch.Status = BatchStatus.Failed;
                batch.CompletedAt = DateTime.UtcNow;
                await SaveBatchStateAsync(batch);
            }
        }

        private async Task ProcessIncrementalAsync(IncrementalMigration incremental)
        {
            try
            {
                _logger.LogInformation($"🔄 Processando migração incremental {incremental.MigrationId}...");
                
                while (incremental.Status == IncrementalStatus.Running)
                {
                    try
                    {
                        // Processar mudanças pendentes
                        if (incremental.PendingChanges.Any())
                        {
                            var syncResult = await SynchronizeChangesAsync(incremental.MigrationId, incremental.PendingChanges);
                            
                            incremental.ProcessedChanges += syncResult.SuccessfulChanges.Count;
                            incremental.FailedChanges += syncResult.FailedChanges.Count;
                            incremental.LastSyncAt = DateTime.UtcNow;
                            
                            // Remover mudanças processadas
                            incremental.PendingChanges.Clear();
                        }
                        
                        // Aguardar próximo ciclo
                        await Task.Delay(incremental.SyncInterval);
                        
                        // Detectar novas mudanças
                        var newChanges = await DetectChangesAsync(incremental.MigrationId, incremental.LastSyncAt);
                        incremental.PendingChanges.AddRange(newChanges);
                        incremental.TotalChanges += newChanges.Count;
                        
                        await SaveIncrementalStateAsync(incremental);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Erro no ciclo de migração incremental {incremental.MigrationId}");
                        await Task.Delay(TimeSpan.FromMinutes(1)); // Aguardar antes de tentar novamente
                    }
                }
                
                _logger.LogInformation($"🏁 Migração incremental {incremental.MigrationId} finalizada. Status: {incremental.Status}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao processar migração incremental {incremental.MigrationId}");
                incremental.Status = IncrementalStatus.Failed;
                await SaveIncrementalStateAsync(incremental);
            }
        }

        private void CheckScheduledMigrations(object? state)
        {
            try
            {
                var schedules = GetScheduledMigrationsAsync().Result;
                var now = DateTime.UtcNow;
                
                foreach (var schedule in schedules.Where(s => s.NextExecutionAt <= now))
                {
                    try
                    {
                        _logger.LogInformation($"⏰ Executando migração agendada: {schedule.MigrationName}");
                        
                        var request = new IncrementalMigrationRequest
                        {
                            MigrationName = schedule.MigrationName,
                            Configuration = schedule.Configuration,
                            SyncInterval = TimeSpan.FromHours(1), // Padrão
                            LastSyncTimestamp = schedule.LastExecutedAt
                        };
                        
                        _ = Task.Run(() => StartIncrementalMigrationAsync(request));
                        
                        schedule.LastExecutedAt = now;
                        schedule.NextExecutionAt = CalculateNextExecution(now, schedule.RecurrencePattern);
                        schedule.ExecutionCount++;
                        
                        _ = Task.Run(() => SaveScheduleAsync(schedule));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Erro ao executar migração agendada {schedule.ScheduleId}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao verificar migrações agendadas");
            }
        }

        private TimeSpan CalculateEstimatedDuration(int totalItems, int batchSize)
        {
            var totalBatches = (int)Math.Ceiling((double)totalItems / batchSize);
            var estimatedMinutesPerBatch = 2; // 2 minutos por lote
            return TimeSpan.FromMinutes(totalBatches * estimatedMinutesPerBatch);
        }

        private TimeSpan? CalculateTimeRemaining(MigrationBatch batch)
        {
            if (batch.Status != BatchStatus.Running || batch.ProcessedItems.Count == 0)
                return null;
                
            var elapsed = DateTime.UtcNow - batch.StartedAt;
            var itemsPerMinute = batch.ProcessedItems.Count / elapsed.TotalMinutes;
            var remainingItems = batch.TotalItems - batch.ProcessedItems.Count;
            
            if (itemsPerMinute > 0)
            {
                return TimeSpan.FromMinutes(remainingItems / itemsPerMinute);
            }
            
            return null;
        }

        private DateTime CalculateNextExecution(DateTime baseTime, string recurrencePattern)
        {
            return recurrencePattern.ToLower() switch
            {
                "hourly" => baseTime.AddHours(1),
                "daily" => baseTime.AddDays(1),
                "weekly" => baseTime.AddDays(7),
                "monthly" => baseTime.AddMonths(1),
                _ => baseTime.AddHours(1) // Padrão: a cada hora
            };
        }

        private List<string> GenerateChangedFields(string entityType, string changeType)
        {
            var fields = entityType switch
            {
                "Workstream" => new[] { "Name", "Description", "WorkDistributionMode", "Capacity" },
                "Queue" => new[] { "Name", "Priority", "OperatingHours", "Skills" },
                "RoutingRule" => new[] { "Name", "Conditions", "Actions", "Priority" },
                "BotConfiguration" => new[] { "Name", "BotId", "Timeout", "FallbackAction" },
                "Agent" => new[] { "Name", "Email", "Skills", "Capacity" },
                "Skill" => new[] { "Name", "Description", "Type" },
                _ => new[] { "Name", "Description" }
            };
            
            var random = new Random();
            var fieldCount = changeType == "CREATE" ? fields.Length : random.Next(1, fields.Length + 1);
            return fields.Take(fieldCount).ToList();
        }

        private Dictionary<string, object> GenerateEntityData(string entityType)
        {
            var random = new Random();
            return entityType switch
            {
                "Workstream" => new Dictionary<string, object>
                {
                    ["Name"] = $"Workstream_{random.Next(1000, 9999)}",
                    ["Description"] = "Auto-generated workstream",
                    ["WorkDistributionMode"] = "Push",
                    ["Capacity"] = random.Next(10, 100)
                },
                "Queue" => new Dictionary<string, object>
                {
                    ["Name"] = $"Queue_{random.Next(1000, 9999)}",
                    ["Priority"] = random.Next(1, 5),
                    ["OperatingHours"] = "24/7",
                    ["Skills"] = new[] { "General", "Technical" }
                },
                _ => new Dictionary<string, object>
                {
                    ["Name"] = $"{entityType}_{random.Next(1000, 9999)}",
                    ["Description"] = $"Auto-generated {entityType.ToLower()}"
                }
            };
        }

        // Métodos de persistência
        private async Task SaveBatchStateAsync(MigrationBatch batch)
        {
            try
            {
                var filePath = Path.Combine(_batchPath, $"batch_{batch.BatchId}.json");
                var json = JsonSerializer.Serialize(batch, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao salvar estado do lote {batch.BatchId}");
            }
        }

        private async Task<MigrationBatch?> LoadBatchStateAsync(string batchId)
        {
            try
            {
                var filePath = Path.Combine(_batchPath, $"batch_{batchId}.json");
                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    return JsonSerializer.Deserialize<MigrationBatch>(json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao carregar estado do lote {batchId}");
            }
            return null;
        }

        private async Task<MigrationBatch?> LoadBatchFromFileAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<MigrationBatch>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao carregar lote do arquivo {filePath}");
                return null;
            }
        }

        private async Task SaveIncrementalStateAsync(IncrementalMigration incremental)
        {
            try
            {
                var filePath = Path.Combine(_incrementalPath, $"incremental_{incremental.MigrationId}.json");
                var json = JsonSerializer.Serialize(incremental, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao salvar estado incremental {incremental.MigrationId}");
            }
        }

        private async Task<IncrementalMigration?> LoadIncrementalStateAsync(string migrationId)
        {
            try
            {
                var filePath = Path.Combine(_incrementalPath, $"incremental_{migrationId}.json");
                if (File.Exists(filePath))
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    return JsonSerializer.Deserialize<IncrementalMigration>(json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao carregar estado incremental {migrationId}");
            }
            return null;
        }

        private async Task SaveScheduleAsync(MigrationSchedule schedule)
        {
            try
            {
                var filePath = Path.Combine(_incrementalPath, $"schedule_{schedule.ScheduleId}.json");
                var json = JsonSerializer.Serialize(schedule, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao salvar agendamento {schedule.ScheduleId}");
            }
        }

        public void Dispose()
        {
            _schedulerTimer?.Dispose();
        }
    }

    // Enums e classes auxiliares
    public enum BatchStatus
    {
        Pending,
        Running,
        Paused,
        Completed,
        CompletedWithErrors,
        Failed,
        Cancelled
    }

    public enum BatchItemStatus
    {
        Pending,
        Processing,
        Completed,
        Failed,
        Skipped
    }

    public enum IncrementalStatus
    {
        Running,
        Paused,
        Stopped,
        Failed,
        Completed
    }

    public class BatchMigrationRequest
    {
        public string BatchName { get; set; } = string.Empty;
        public List<BatchItem> Items { get; set; } = new();
        public int BatchSize { get; set; } = 10;
        public bool CreateBackup { get; set; } = true;
        public BatchConfiguration Configuration { get; set; } = new();
    }

    public class IncrementalMigrationRequest
    {
        public string MigrationName { get; set; } = string.Empty;
        public TimeSpan SyncInterval { get; set; } = TimeSpan.FromHours(1);
        public DateTime? LastSyncTimestamp { get; set; }
        public IncrementalConfiguration Configuration { get; set; } = new();
        public List<ChangeDetectionRule> ChangeDetectionRules { get; set; } = new();
    }

    public class ScheduleMigrationRequest
    {
        public string MigrationName { get; set; } = string.Empty;
        public DateTime ScheduledAt { get; set; }
        public string RecurrencePattern { get; set; } = "daily"; // hourly, daily, weekly, monthly
        public IncrementalConfiguration Configuration { get; set; } = new();
    }

    public class BatchConfiguration
    {
        public int ProcessingDelayMs { get; set; } = 100;
        public int MaxRetries { get; set; } = 3;
        public bool ContinueOnError { get; set; } = true;
        public bool CreateDetailedLog { get; set; } = true;
        public Dictionary<string, object> CustomSettings { get; set; } = new();
    }

    public class IncrementalConfiguration
    {
        public bool EnableRealTimeSync { get; set; } = false;
        public int MaxChangesPerSync { get; set; } = 100;
        public bool CreateBackupBeforeSync { get; set; } = true;
        public List<string> ExcludedEntityTypes { get; set; } = new();
        public Dictionary<string, object> CustomSettings { get; set; } = new();
    }

    public class BatchItem
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public Dictionary<string, object> Data { get; set; } = new();
        public BatchItemStatus Status { get; set; } = BatchItemStatus.Pending;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public int RetryCount { get; set; } = 0;
    }

    public class MigrationBatch
    {
        public string BatchId { get; set; } = string.Empty;
        public string BatchName { get; set; } = string.Empty;
        public BatchStatus Status { get; set; } = BatchStatus.Pending;
        public int TotalItems { get; set; }
        public int BatchSize { get; set; }
        public int CurrentBatchNumber { get; set; } = 0;
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? PausedAt { get; set; }
        public DateTime? ResumedAt { get; set; }
        public List<BatchItem> Items { get; set; } = new();
        public List<BatchItem> ProcessedItems { get; set; } = new();
        public List<BatchItem> FailedItems { get; set; } = new();
        public BatchConfiguration Configuration { get; set; } = new();
        public string? BackupId { get; set; }
    }

    public class IncrementalMigration
    {
        public string MigrationId { get; set; } = string.Empty;
        public string MigrationName { get; set; } = string.Empty;
        public IncrementalStatus Status { get; set; } = IncrementalStatus.Running;
        public DateTime StartedAt { get; set; }
        public DateTime LastSyncAt { get; set; }
        public TimeSpan SyncInterval { get; set; }
        public int TotalChanges { get; set; }
        public int ProcessedChanges { get; set; }
        public int FailedChanges { get; set; }
        public List<DataDelta> PendingChanges { get; set; } = new();
        public IncrementalConfiguration Configuration { get; set; } = new();
        public List<ChangeDetectionRule> ChangeDetectionRules { get; set; } = new();
    }

    public class DataDelta
    {
        public string DeltaId { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string ChangeType { get; set; } = string.Empty; // CREATE, UPDATE, DELETE
        public DateTime ChangedAt { get; set; }
        public List<string> ChangedFields { get; set; } = new();
        public Dictionary<string, object> OldValues { get; set; } = new();
        public Dictionary<string, object> NewValues { get; set; } = new();
        public int Priority { get; set; } = 1;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class ChangeDetectionRule
    {
        public string RuleId { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public List<string> MonitoredFields { get; set; } = new();
        public string Condition { get; set; } = string.Empty;
        public int Priority { get; set; } = 1;
        public bool IsActive { get; set; } = true;
    }

    public class MigrationSchedule
    {
        public string ScheduleId { get; set; } = string.Empty;
        public string MigrationName { get; set; } = string.Empty;
        public DateTime ScheduledAt { get; set; }
        public DateTime NextExecutionAt { get; set; }
        public DateTime? LastExecutedAt { get; set; }
        public string RecurrencePattern { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public int ExecutionCount { get; set; } = 0;
        public IncrementalConfiguration Configuration { get; set; } = new();
    }

    public class BatchMigrationResult
    {
        public bool Success { get; set; }
        public string BatchId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public TimeSpan EstimatedDuration { get; set; }
        public DateTime StartedAt { get; set; }
        public string? Error { get; set; }
    }

    public class IncrementalMigrationResult
    {
        public bool Success { get; set; }
        public string MigrationId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int ChangesDetected { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime NextSyncAt { get; set; }
        public string? Error { get; set; }
    }

    public class MigrationBatchStatus
    {
        public string BatchId { get; set; } = string.Empty;
        public bool Found { get; set; }
        public string BatchName { get; set; } = string.Empty;
        public BatchStatus Status { get; set; }
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public int FailedItems { get; set; }
        public double ProgressPercentage { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan? EstimatedTimeRemaining { get; set; }
        public int CurrentBatchNumber { get; set; }
        public int TotalBatches { get; set; }
        public List<string> ErrorMessages { get; set; } = new();
        public string Message { get; set; } = string.Empty;
    }

    public class IncrementalMigrationProgress
    {
        public string MigrationId { get; set; } = string.Empty;
        public bool Found { get; set; }
        public string MigrationName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int TotalChanges { get; set; }
        public int ProcessedChanges { get; set; }
        public int FailedChanges { get; set; }
        public double ProgressPercentage { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime LastSyncAt { get; set; }
        public DateTime NextSyncAt { get; set; }
        public TimeSpan SyncInterval { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class SyncResult
    {
        public string MigrationId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public int TotalChanges { get; set; }
        public List<string> SuccessfulChanges { get; set; } = new();
        public List<SyncError> FailedChanges { get; set; } = new();
        public double SuccessRate { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class SyncError
    {
        public string DeltaId { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public DateTime ErrorAt { get; set; } = DateTime.UtcNow;
    }
}
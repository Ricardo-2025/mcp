using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace GenesysMigrationMCP.Services
{
    /// <summary>
    /// Implementação do serviço de logging estruturado
    /// </summary>
    public class LoggingService : ILoggingService
    {
        private readonly ILogger<LoggingService> _logger;
        private readonly ConcurrentQueue<LogEntry> _logBuffer;
        private readonly ConcurrentDictionary<string, List<double>> _metrics;
        private readonly Timer _metricsTimer;
        private readonly string _applicationName;
        private readonly string _version;

        public LoggingService(ILogger<LoggingService> logger)
        {
            _logger = logger;
            _logBuffer = new ConcurrentQueue<LogEntry>();
            _metrics = new ConcurrentDictionary<string, List<double>>();
            _applicationName = "GenesysMigrationMCP";
            _version = "1.0.0";
            
            // Timer para flush periódico das métricas
            _metricsTimer = new Timer(FlushMetrics, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public void LogMigrationStarted(string migrationId, string requestedBy, Dictionary<string, object> parameters)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Information,
                Category = "Migration",
                Message = "Migração iniciada",
                MigrationId = migrationId,
                ClientId = requestedBy,
                Properties = new Dictionary<string, object>(parameters)
                {
                    ["EventType"] = "MigrationStarted",
                    ["Application"] = _applicationName,
                    ["Version"] = _version
                }
            };

            LogStructured(logEntry);
            
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["MigrationId"] = migrationId,
                ["RequestedBy"] = requestedBy
            });
            
            _logger.LogInformation("Migração {MigrationId} iniciada por {RequestedBy} com parâmetros: {Parameters}",
                migrationId, requestedBy, JsonSerializer.Serialize(parameters));
        }

        public void LogMigrationCompleted(string migrationId, TimeSpan duration, int itemsProcessed, bool success)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = success ? LogLevel.Information : LogLevel.Warning,
                Category = "Migration",
                Message = success ? "Migração concluída com sucesso" : "Migração concluída com erros",
                MigrationId = migrationId,
                Properties = new Dictionary<string, object>
                {
                    ["EventType"] = "MigrationCompleted",
                    ["Duration"] = duration.TotalSeconds,
                    ["ItemsProcessed"] = itemsProcessed,
                    ["Success"] = success,
                    ["ItemsPerSecond"] = itemsProcessed / Math.Max(duration.TotalSeconds, 1),
                    ["Application"] = _applicationName,
                    ["Version"] = _version
                }
            };

            LogStructured(logEntry);
            
            // Registrar métricas de performance
            LogPerformanceMetric(PerformanceMetrics.MigrationDuration, duration.TotalSeconds, 
                new Dictionary<string, object> { ["MigrationId"] = migrationId });
            LogPerformanceMetric(PerformanceMetrics.ItemsProcessedPerSecond, 
                itemsProcessed / Math.Max(duration.TotalSeconds, 1));
            
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["MigrationId"] = migrationId,
                ["Duration"] = duration.TotalSeconds,
                ["ItemsProcessed"] = itemsProcessed
            });
            
            if (success)
            {
                _logger.LogInformation("Migração {MigrationId} concluída com sucesso em {Duration}s, {ItemsProcessed} itens processados",
                    migrationId, duration.TotalSeconds, itemsProcessed);
            }
            else
            {
                _logger.LogWarning("Migração {MigrationId} concluída com erros em {Duration}s, {ItemsProcessed} itens processados",
                    migrationId, duration.TotalSeconds, itemsProcessed);
            }
        }

        public void LogMigrationError(string migrationId, Exception exception, Dictionary<string, object>? context = null)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Error,
                Category = "Migration",
                Message = "Erro durante migração",
                MigrationId = migrationId,
                Exception = exception,
                Properties = new Dictionary<string, object>
                {
                    ["EventType"] = "MigrationError",
                    ["ErrorType"] = exception.GetType().Name,
                    ["Application"] = _applicationName,
                    ["Version"] = _version
                }
            };

            if (context != null)
            {
                foreach (var kvp in context)
                {
                    logEntry.Properties[kvp.Key] = kvp.Value;
                }
            }

            LogStructured(logEntry);
            
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["MigrationId"] = migrationId,
                ["ErrorType"] = exception.GetType().Name
            });
            
            _logger.LogError(exception, "Erro na migração {MigrationId}: {ErrorMessage}", 
                migrationId, exception.Message);
        }

        public void LogApiRequest(string endpoint, string method, string clientId, TimeSpan responseTime, int statusCode)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = statusCode >= 400 ? LogLevel.Warning : LogLevel.Information,
                Category = "ApiRequest",
                Message = "Requisição API processada",
                ClientId = clientId,
                Properties = new Dictionary<string, object>
                {
                    ["EventType"] = "ApiRequest",
                    ["Endpoint"] = endpoint,
                    ["Method"] = method,
                    ["StatusCode"] = statusCode,
                    ["ResponseTime"] = responseTime.TotalMilliseconds,
                    ["Application"] = _applicationName,
                    ["Version"] = _version
                }
            };

            LogStructured(logEntry);
            
            // Registrar métrica de performance
            LogPerformanceMetric(PerformanceMetrics.RequestDuration, responseTime.TotalMilliseconds,
                new Dictionary<string, object> 
                { 
                    ["Endpoint"] = endpoint,
                    ["Method"] = method,
                    ["StatusCode"] = statusCode
                });
            
            _logger.LogInformation("API {Method} {Endpoint} - Cliente: {ClientId}, Status: {StatusCode}, Tempo: {ResponseTime}ms",
                method, endpoint, clientId, statusCode, responseTime.TotalMilliseconds);
        }

        public void LogSecurityEvent(string eventType, string clientId, string details, bool isSuccessful = true)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = isSuccessful ? LogLevel.Information : LogLevel.Warning,
                Category = "Security",
                Message = $"Evento de segurança: {eventType}",
                ClientId = clientId,
                Properties = new Dictionary<string, object>
                {
                    ["EventType"] = "SecurityEvent",
                    ["SecurityEventType"] = eventType,
                    ["Details"] = details,
                    ["IsSuccessful"] = isSuccessful,
                    ["Application"] = _applicationName,
                    ["Version"] = _version
                }
            };

            LogStructured(logEntry);
            
            var logLevel = isSuccessful ? LogLevel.Information : LogLevel.Warning;
            _logger.Log(logLevel, "Evento de segurança {EventType} para cliente {ClientId}: {Details}",
                eventType, clientId, details);
        }

        public void LogPerformanceMetric(string metricName, double value, Dictionary<string, object>? tags = null)
        {
            // Adicionar à coleção de métricas
            _metrics.AddOrUpdate(metricName, 
                new List<double> { value },
                (key, existing) => 
                {
                    existing.Add(value);
                    // Manter apenas os últimos 1000 valores
                    if (existing.Count > 1000)
                    {
                        existing.RemoveRange(0, existing.Count - 1000);
                    }
                    return existing;
                });

            var logEntry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Debug,
                Category = "Performance",
                Message = $"Métrica: {metricName}",
                Properties = new Dictionary<string, object>
                {
                    ["EventType"] = "PerformanceMetric",
                    ["MetricName"] = metricName,
                    ["Value"] = value,
                    ["Application"] = _applicationName,
                    ["Version"] = _version
                }
            };

            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    logEntry.Properties[$"Tag_{tag.Key}"] = tag.Value;
                }
            }

            LogStructured(logEntry);
        }

        public void LogBusinessEvent(string eventName, Dictionary<string, object> properties)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Information,
                Category = "Business",
                Message = $"Evento de negócio: {eventName}",
                Properties = new Dictionary<string, object>(properties)
                {
                    ["EventType"] = "BusinessEvent",
                    ["BusinessEventName"] = eventName,
                    ["Application"] = _applicationName,
                    ["Version"] = _version
                }
            };

            LogStructured(logEntry);
            
            _logger.LogInformation("Evento de negócio {EventName}: {Properties}",
                eventName, JsonSerializer.Serialize(properties));
        }

        public async Task<Dictionary<string, object>> GetMetricsAsync(DateTime from, DateTime to)
        {
            await Task.CompletedTask; // Placeholder para implementação assíncrona futura
            
            var result = new Dictionary<string, object>();
            
            foreach (var metric in _metrics)
            {
                var values = metric.Value.ToList();
                if (values.Any())
                {
                    result[metric.Key] = new
                    {
                        Count = values.Count,
                        Average = values.Average(),
                        Min = values.Min(),
                        Max = values.Max(),
                        Sum = values.Sum()
                    };
                }
            }
            
            return result;
        }

        public async Task<List<LogEntry>> GetLogsAsync(string? migrationId = null, LogLevel? level = null, int maxResults = 100)
        {
            await Task.CompletedTask; // Placeholder para implementação assíncrona futura
            
            var logs = _logBuffer.ToList()
                .Where(log => migrationId == null || log.MigrationId == migrationId)
                .Where(log => level == null || log.Level == level)
                .OrderByDescending(log => log.Timestamp)
                .Take(maxResults)
                .ToList();
            
            return logs;
        }

        private void LogStructured(LogEntry logEntry)
        {
            // Adicionar ao buffer para consultas futuras
            _logBuffer.Enqueue(logEntry);
            
            // Manter apenas os últimos 10000 logs em memória
            while (_logBuffer.Count > 10000)
            {
                _logBuffer.TryDequeue(out _);
            }
        }

        private void FlushMetrics(object? state)
        {
            try
            {
                var metricsSnapshot = new Dictionary<string, object>();
                
                foreach (var metric in _metrics)
                {
                    var values = metric.Value.ToList();
                    if (values.Any())
                    {
                        metricsSnapshot[metric.Key] = new
                        {
                            Count = values.Count,
                            Average = values.Average(),
                            Timestamp = DateTime.UtcNow
                        };
                    }
                }
                
                if (metricsSnapshot.Any())
                {
                    _logger.LogInformation("Métricas agregadas: {Metrics}", 
                        JsonSerializer.Serialize(metricsSnapshot));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao fazer flush das métricas");
            }
        }

        public void Dispose()
        {
            _metricsTimer?.Dispose();
        }
    }
}
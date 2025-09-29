using Microsoft.Extensions.Logging;

namespace GenesysMigrationMCP.Services
{
    /// <summary>
    /// Interface para serviço de logging estruturado
    /// </summary>
    public interface ILoggingService
    {
        void LogMigrationStarted(string migrationId, string requestedBy, Dictionary<string, object> parameters);
        void LogMigrationCompleted(string migrationId, TimeSpan duration, int itemsProcessed, bool success);
        void LogMigrationError(string migrationId, Exception exception, Dictionary<string, object>? context = null);
        void LogApiRequest(string endpoint, string method, string clientId, TimeSpan responseTime, int statusCode);
        void LogSecurityEvent(string eventType, string clientId, string details, bool isSuccessful = true);
        void LogPerformanceMetric(string metricName, double value, Dictionary<string, object>? tags = null);
        void LogBusinessEvent(string eventName, Dictionary<string, object> properties);
        Task<Dictionary<string, object>> GetMetricsAsync(DateTime from, DateTime to);
        Task<List<LogEntry>> GetLogsAsync(string? migrationId = null, LogLevel? level = null, int maxResults = 100);
    }

    /// <summary>
    /// Entrada de log estruturada
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? MigrationId { get; set; }
        public string? ClientId { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();
        public Exception? Exception { get; set; }
    }

    /// <summary>
    /// Tipos de eventos de segurança
    /// </summary>
    public static class SecurityEventTypes
    {
        public const string AuthenticationSuccess = "AuthenticationSuccess";
        public const string AuthenticationFailure = "AuthenticationFailure";
        public const string RateLimitExceeded = "RateLimitExceeded";
        public const string SuspiciousActivity = "SuspiciousActivity";
        public const string ApiKeyUsage = "ApiKeyUsage";
        public const string UnauthorizedAccess = "UnauthorizedAccess";
    }

    /// <summary>
    /// Métricas de performance
    /// </summary>
    public static class PerformanceMetrics
    {
        public const string RequestDuration = "RequestDuration";
        public const string MigrationDuration = "MigrationDuration";
        public const string ItemsProcessedPerSecond = "ItemsProcessedPerSecond";
        public const string ErrorRate = "ErrorRate";
        public const string MemoryUsage = "MemoryUsage";
        public const string CpuUsage = "CpuUsage";
    }

    /// <summary>
    /// Eventos de negócio
    /// </summary>
    public static class BusinessEvents
    {
        public const string FlowExtracted = "FlowExtracted";
        public const string WorkstreamCreated = "WorkstreamCreated";
        public const string BotConfigured = "BotConfigured";
        public const string MigrationRequested = "MigrationRequested";
        public const string ValidationFailed = "ValidationFailed";
    }
}
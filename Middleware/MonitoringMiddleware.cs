using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using GenesysMigrationMCP.Services;
using System.Diagnostics;
using System.Net;

namespace GenesysMigrationMCP.Middleware
{
    /// <summary>
    /// Middleware para monitoramento automático de performance e métricas
    /// </summary>
    public class MonitoringMiddleware : IFunctionsWorkerMiddleware
    {
        private readonly ILogger<MonitoringMiddleware> _logger;

        public MonitoringMiddleware(ILogger<MonitoringMiddleware> logger)
        {
            _logger = logger;
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = Guid.NewGuid().ToString("N")[..8];
            var functionName = context.FunctionDefinition.Name;
            var loggingService = context.InstanceServices.GetService(typeof(ILoggingService)) as ILoggingService;
            
            try
            {
                var request = await context.GetHttpRequestDataAsync();
                if (request != null)
                {
                    var clientId = GetClientIdentifier(request);
                    var endpoint = request.Url.AbsolutePath;
                    var method = request.Method;
                    
                    // Log início da requisição
                    using var scope = _logger.BeginScope(new Dictionary<string, object>
                    {
                        ["RequestId"] = requestId,
                        ["FunctionName"] = functionName,
                        ["ClientId"] = clientId,
                        ["Endpoint"] = endpoint,
                        ["Method"] = method
                    });
                    
                    _logger.LogInformation("Iniciando processamento da requisição {RequestId} para {Method} {Endpoint}",
                        requestId, method, endpoint);
                    
                    // Capturar métricas de sistema antes da execução
                    var memoryBefore = GC.GetTotalMemory(false);
                    var processBefore = Process.GetCurrentProcess();
                    var cpuBefore = processBefore.TotalProcessorTime;
                    
                    await next(context);
                    
                    stopwatch.Stop();
                    
                    // Capturar métricas de sistema após a execução
                    var memoryAfter = GC.GetTotalMemory(false);
                    var processAfter = Process.GetCurrentProcess();
                    var cpuAfter = processAfter.TotalProcessorTime;
                    
                    var memoryUsed = memoryAfter - memoryBefore;
                    var cpuUsed = (cpuAfter - cpuBefore).TotalMilliseconds;
                    
                    // Obter status code da resposta
                    var statusCode = GetResponseStatusCode(context);
                    
                    // Log métricas de performance
                    loggingService?.LogPerformanceMetric(PerformanceMetrics.RequestDuration, 
                        stopwatch.Elapsed.TotalMilliseconds,
                        new Dictionary<string, object>
                        {
                            ["FunctionName"] = functionName,
                            ["Endpoint"] = endpoint,
                            ["Method"] = method,
                            ["StatusCode"] = statusCode
                        });
                    
                    loggingService?.LogPerformanceMetric(PerformanceMetrics.MemoryUsage, memoryUsed);
                    loggingService?.LogPerformanceMetric(PerformanceMetrics.CpuUsage, cpuUsed);
                    
                    // Log da requisição API
                    loggingService?.LogApiRequest(endpoint, method, clientId, stopwatch.Elapsed, statusCode);
                    
                    _logger.LogInformation(
                        "Requisição {RequestId} concluída - Status: {StatusCode}, Duração: {Duration}ms, Memória: {Memory}KB, CPU: {Cpu}ms",
                        requestId, statusCode, stopwatch.Elapsed.TotalMilliseconds, memoryUsed / 1024, cpuUsed);
                }
                else
                {
                    // Função não HTTP
                    _logger.LogInformation("Executando função {FunctionName} (não HTTP)", functionName);
                    await next(context);
                    stopwatch.Stop();
                    
                    loggingService?.LogPerformanceMetric(PerformanceMetrics.RequestDuration, 
                        stopwatch.Elapsed.TotalMilliseconds,
                        new Dictionary<string, object> { ["FunctionName"] = functionName });
                    
                    _logger.LogInformation("Função {FunctionName} concluída em {Duration}ms",
                        functionName, stopwatch.Elapsed.TotalMilliseconds);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                _logger.LogError(ex, "Erro durante execução da função {FunctionName} (RequestId: {RequestId}) após {Duration}ms",
                    functionName, requestId, stopwatch.Elapsed.TotalMilliseconds);
                
                // Log métrica de erro
                loggingService?.LogPerformanceMetric(PerformanceMetrics.ErrorRate, 1,
                    new Dictionary<string, object>
                    {
                        ["FunctionName"] = functionName,
                        ["ErrorType"] = ex.GetType().Name,
                        ["RequestId"] = requestId
                    });
                
                throw;
            }
        }

        private string GetClientIdentifier(Microsoft.Azure.Functions.Worker.Http.HttpRequestData request)
        {
            // Tentar obter identificador do cliente
            var clientId = "unknown";
            
            // Verificar header de API key
            if (request.Headers.TryGetValues("X-API-Key", out var apiKeyHeaders))
            {
                var apiKey = apiKeyHeaders.FirstOrDefault();
                if (!string.IsNullOrEmpty(apiKey))
                {
                    // Usar hash da API key como identificador (mais seguro)
                    clientId = $"key_{apiKey.GetHashCode():X8}";
                }
            }
            else if (request.Headers.TryGetValues("Authorization", out var authHeaders))
            {
                var authHeader = authHeaders.FirstOrDefault();
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                {
                    var token = authHeader.Substring(7);
                    clientId = $"bearer_{token.GetHashCode():X8}";
                }
            }
            
            // Fallback para IP do cliente
            if (clientId == "unknown")
            {
                if (request.Headers.TryGetValues("X-Forwarded-For", out var forwardedFor))
                {
                    clientId = forwardedFor.FirstOrDefault()?.Split(',')[0].Trim() ?? "unknown";
                }
                else if (request.Headers.TryGetValues("X-Real-IP", out var realIp))
                {
                    clientId = realIp.FirstOrDefault() ?? "unknown";
                }
            }
            
            return clientId;
        }

        private int GetResponseStatusCode(FunctionContext context)
        {
            try
            {
                var response = context.GetHttpResponseData();
                if (response != null)
                {
                    return (int)response.StatusCode;
                }
            }
            catch
            {
                // Ignorar erros ao obter status code
            }
            
            return 200; // Default para sucesso se não conseguir obter
        }
    }

    /// <summary>
    /// Middleware para health check automático
    /// </summary>
    public class HealthCheckMiddleware : IFunctionsWorkerMiddleware
    {
        private readonly ILogger<HealthCheckMiddleware> _logger;
        private readonly ILoggingService _loggingService;
        private static readonly Dictionary<string, DateTime> _lastHealthCheck = new();
        private static readonly object _lockObject = new();

        public HealthCheckMiddleware(ILogger<HealthCheckMiddleware> logger, ILoggingService loggingService)
        {
            _logger = logger;
            _loggingService = loggingService;
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            try
            {
                // Executar health check periódico (a cada 5 minutos)
                var now = DateTime.UtcNow;
                var functionName = context.FunctionDefinition.Name;
                
                lock (_lockObject)
                {
                    if (!_lastHealthCheck.ContainsKey(functionName) || 
                        now - _lastHealthCheck[functionName] > TimeSpan.FromMinutes(5))
                    {
                        _lastHealthCheck[functionName] = now;
                        
                        // Executar health check em background
                        _ = Task.Run(() => PerformHealthCheck(functionName));
                    }
                }
                
                await next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no middleware de health check");
                await next(context);
            }
        }

        private async Task PerformHealthCheck(string functionName)
        {
            try
            {
                var healthData = new Dictionary<string, object>
                {
                    ["Timestamp"] = DateTime.UtcNow,
                    ["FunctionName"] = functionName,
                    ["Status"] = "Healthy"
                };
                
                // Verificar memória
                var memoryUsage = GC.GetTotalMemory(false);
                healthData["MemoryUsageBytes"] = memoryUsage;
                healthData["MemoryUsageMB"] = memoryUsage / (1024 * 1024);
                
                // Verificar processo
                var process = Process.GetCurrentProcess();
                healthData["WorkingSetMB"] = process.WorkingSet64 / (1024 * 1024);
                healthData["ThreadCount"] = process.Threads.Count;
                
                // Verificar uptime
                healthData["UptimeMinutes"] = (DateTime.UtcNow - process.StartTime).TotalMinutes;
                
                _loggingService.LogBusinessEvent("HealthCheck", healthData);
                
                _logger.LogDebug("Health check executado para {FunctionName}: Memória={MemoryMB}MB, Threads={ThreadCount}",
                    functionName, memoryUsage / (1024 * 1024), process.Threads.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro durante health check para {FunctionName}", functionName);
                
                _loggingService.LogBusinessEvent("HealthCheckError", new Dictionary<string, object>
                {
                    ["FunctionName"] = functionName,
                    ["Error"] = ex.Message,
                    ["Timestamp"] = DateTime.UtcNow
                });
            }
        }
    }
}
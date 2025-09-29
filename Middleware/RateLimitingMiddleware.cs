using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace GenesysMigrationMCP.Middleware
{
    /// <summary>
    /// Middleware para rate limiting e proteção contra ataques
    /// </summary>
    public class RateLimitingMiddleware : IFunctionsWorkerMiddleware
    {
        private readonly ILogger<RateLimitingMiddleware> _logger;
        private readonly ConcurrentDictionary<string, ClientRequestInfo> _clientRequests;
        private readonly Timer _cleanupTimer;
        
        // Configurações de rate limiting
        private readonly int _maxRequestsPerMinute;
        private readonly int _maxRequestsPerHour;
        private readonly TimeSpan _windowSize = TimeSpan.FromMinutes(1);
        private readonly TimeSpan _hourlyWindowSize = TimeSpan.FromHours(1);

        public RateLimitingMiddleware(ILogger<RateLimitingMiddleware> logger)
        {
            _logger = logger;
            _clientRequests = new ConcurrentDictionary<string, ClientRequestInfo>();
            
            // Configurar limites (em produção, usar configuração)
            _maxRequestsPerMinute = int.Parse(Environment.GetEnvironmentVariable("MCP_RATE_LIMIT_PER_MINUTE") ?? "60");
            _maxRequestsPerHour = int.Parse(Environment.GetEnvironmentVariable("MCP_RATE_LIMIT_PER_HOUR") ?? "1000");
            
            // Timer para limpeza periódica dos dados antigos
            _cleanupTimer = new Timer(CleanupOldEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            try
            {
                var request = await context.GetHttpRequestDataAsync();
                if (request == null)
                {
                    await next(context);
                    return;
                }

                var clientId = GetClientIdentifier(request);
                var now = DateTime.UtcNow;
                
                // Verificar rate limiting
                if (!IsRequestAllowed(clientId, now))
                {
                    _logger.LogWarning($"Rate limit excedido para cliente: {clientId}");
                    await SetRateLimitResponse(context, clientId);
                    return;
                }

                // Registrar a requisição
                RecordRequest(clientId, now);
                
                await next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no middleware de rate limiting");
                await next(context);
            }
        }

        private string GetClientIdentifier(Microsoft.Azure.Functions.Worker.Http.HttpRequestData request)
        {
            // Tentar obter IP do cliente
            var clientIp = "unknown";
            
            // Verificar headers de proxy/load balancer
            if (request.Headers.TryGetValues("X-Forwarded-For", out var forwardedFor))
            {
                clientIp = forwardedFor.FirstOrDefault()?.Split(',')[0].Trim() ?? clientIp;
            }
            else if (request.Headers.TryGetValues("X-Real-IP", out var realIp))
            {
                clientIp = realIp.FirstOrDefault() ?? clientIp;
            }
            
            // Adicionar User-Agent para melhor identificação
            var userAgent = "";
            if (request.Headers.TryGetValues("User-Agent", out var userAgentHeaders))
            {
                userAgent = userAgentHeaders.FirstOrDefault() ?? "";
            }
            
            // Criar identificador único baseado em IP e User-Agent
            return $"{clientIp}_{userAgent.GetHashCode()}".Replace(" ", "");
        }

        private bool IsRequestAllowed(string clientId, DateTime now)
        {
            var clientInfo = _clientRequests.GetOrAdd(clientId, _ => new ClientRequestInfo());
            
            lock (clientInfo)
            {
                // Limpar requisições antigas
                clientInfo.RequestTimes.RemoveAll(time => now - time > _hourlyWindowSize);
                
                // Contar requisições na última hora
                var hourlyCount = clientInfo.RequestTimes.Count;
                if (hourlyCount >= _maxRequestsPerHour)
                {
                    return false;
                }
                
                // Contar requisições no último minuto
                var minuteCount = clientInfo.RequestTimes.Count(time => now - time <= _windowSize);
                if (minuteCount >= _maxRequestsPerMinute)
                {
                    return false;
                }
                
                return true;
            }
        }

        private void RecordRequest(string clientId, DateTime now)
        {
            var clientInfo = _clientRequests.GetOrAdd(clientId, _ => new ClientRequestInfo());
            
            lock (clientInfo)
            {
                clientInfo.RequestTimes.Add(now);
                clientInfo.LastRequestTime = now;
            }
        }

        private async Task SetRateLimitResponse(FunctionContext context, string clientId)
        {
            var response = context.GetHttpResponseData();
            if (response != null)
            {
                response.StatusCode = HttpStatusCode.TooManyRequests;
                response.Headers.Add("Content-Type", "application/json");
                response.Headers.Add("Retry-After", "60"); // Tentar novamente em 60 segundos
                
                var errorResponse = new
                {
                    success = false,
                    error = "Muitas requisições. Limite excedido.",
                    details = new
                    {
                        maxRequestsPerMinute = _maxRequestsPerMinute,
                        maxRequestsPerHour = _maxRequestsPerHour,
                        retryAfterSeconds = 60
                    },
                    timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse));
            }
        }

        private void CleanupOldEntries(object? state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var keysToRemove = new List<string>();
                
                foreach (var kvp in _clientRequests)
                {
                    var clientInfo = kvp.Value;
                    lock (clientInfo)
                    {
                        // Remover clientes inativos por mais de 2 horas
                        if (now - clientInfo.LastRequestTime > TimeSpan.FromHours(2))
                        {
                            keysToRemove.Add(kvp.Key);
                        }
                        else
                        {
                            // Limpar requisições antigas
                            clientInfo.RequestTimes.RemoveAll(time => now - time > _hourlyWindowSize);
                        }
                    }
                }
                
                // Remover clientes inativos
                foreach (var key in keysToRemove)
                {
                    _clientRequests.TryRemove(key, out _);
                }
                
                _logger.LogDebug($"Limpeza de rate limiting: {keysToRemove.Count} clientes removidos");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro durante limpeza do rate limiting");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }

    /// <summary>
    /// Informações de requisições por cliente
    /// </summary>
    public class ClientRequestInfo
    {
        public List<DateTime> RequestTimes { get; } = new List<DateTime>();
        public DateTime LastRequestTime { get; set; } = DateTime.UtcNow;
    }
}
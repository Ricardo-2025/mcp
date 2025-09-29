using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace GenesysMigrationMCP.Middleware
{
    /// <summary>
    /// Middleware para autenticação e autorização das Azure Functions
    /// </summary>
    public class AuthenticationMiddleware : IFunctionsWorkerMiddleware
    {
        private readonly ILogger<AuthenticationMiddleware> _logger;
        private readonly HashSet<string> _validApiKeys;
        private readonly HashSet<string> _publicEndpoints;

        public AuthenticationMiddleware(ILogger<AuthenticationMiddleware> logger)
        {
            _logger = logger;
            
            // Configurar chaves de API válidas (em produção, usar Azure Key Vault)
            _validApiKeys = new HashSet<string>
            {
                Environment.GetEnvironmentVariable("MCP_API_KEY") ?? "default-dev-key-123",
                Environment.GetEnvironmentVariable("MCP_ADMIN_KEY") ?? "admin-dev-key-456"
            };

            // Endpoints públicos que não requerem autenticação
            _publicEndpoints = new HashSet<string>
            {
                "/mcp/info",
                "/mcp/capabilities",
                "/mcp/endpoints"
            };
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

                var path = request.Url.AbsolutePath;
                
                // Verificar se é um endpoint público
                if (IsPublicEndpoint(path))
                {
                    _logger.LogInformation($"Acesso público permitido para: {path}");
                    await next(context);
                    return;
                }

                // Verificar autenticação para endpoints protegidos
                if (!await IsAuthenticatedAsync(request))
                {
                    _logger.LogWarning($"Acesso não autorizado tentado para: {path}");
                    await SetUnauthorizedResponse(context);
                    return;
                }

                _logger.LogInformation($"Acesso autorizado para: {path}");
                await next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no middleware de autenticação");
                await SetInternalErrorResponse(context);
            }
        }

        private bool IsPublicEndpoint(string path)
        {
            return _publicEndpoints.Any(endpoint => path.StartsWith(endpoint, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<bool> IsAuthenticatedAsync(Microsoft.Azure.Functions.Worker.Http.HttpRequestData request)
        {
            // Verificar header Authorization
            if (request.Headers.TryGetValues("Authorization", out var authHeaders))
            {
                var authHeader = authHeaders.FirstOrDefault();
                if (!string.IsNullOrEmpty(authHeader))
                {
                    // Suporte para Bearer token
                    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = authHeader.Substring(7);
                        return _validApiKeys.Contains(token);
                    }
                }
            }

            // Verificar header X-API-Key
            if (request.Headers.TryGetValues("X-API-Key", out var apiKeyHeaders))
            {
                var apiKey = apiKeyHeaders.FirstOrDefault();
                if (!string.IsNullOrEmpty(apiKey))
                {
                    return _validApiKeys.Contains(apiKey);
                }
            }

            // Verificar query parameter (menos seguro, apenas para desenvolvimento)
            var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
            var queryApiKey = query["api_key"];
            if (!string.IsNullOrEmpty(queryApiKey))
            {
                _logger.LogWarning("API Key fornecida via query parameter - não recomendado para produção");
                return _validApiKeys.Contains(queryApiKey);
            }

            return false;
        }

        private async Task SetUnauthorizedResponse(FunctionContext context)
        {
            var response = context.GetHttpResponseData();
            if (response != null)
            {
                response.StatusCode = HttpStatusCode.Unauthorized;
                response.Headers.Add("Content-Type", "application/json");
                
                var errorResponse = new
                {
                    success = false,
                    error = "Não autorizado. Forneça uma chave de API válida via header 'Authorization: Bearer <key>' ou 'X-API-Key: <key>'",
                    timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse));
            }
        }

        private async Task SetInternalErrorResponse(FunctionContext context)
        {
            var response = context.GetHttpResponseData();
            if (response != null)
            {
                response.StatusCode = HttpStatusCode.InternalServerError;
                response.Headers.Add("Content-Type", "application/json");
                
                var errorResponse = new
                {
                    success = false,
                    error = "Erro interno do servidor",
                    timestamp = DateTime.UtcNow
                };

                await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse));
            }
        }
    }
}
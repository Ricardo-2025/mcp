using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GenesysMigrationMCP.Services;
using GenesysMigrationMCP.Models;
using Newtonsoft.Json;
using System.Text;
using System.Text.Json;
using System.Net;

namespace GenesysMigrationMCP.Functions
{
    public class MCPServerFunction
    {
        private readonly IMcpService _mcpService;
        private readonly ILogger<MCPServerFunction> _logger;

        // Armazena sessões ativas e último "ping" (initialized/requests)
        private static readonly Dictionary<string, DateTime> _sessions = new();

        public MCPServerFunction(IMcpService mcpService, ILogger<MCPServerFunction> logger)
        {
            _mcpService = mcpService;
            _logger = logger;
        }

        [Function("MCPEndpoint")]
        public async Task<HttpResponseData> HandleMCPRequest(
            [HttpTrigger(AuthorizationLevel.Function, "post", "options", Route = "mcp")] HttpRequestData req)
        {
            try
            {
                // Preflight CORS
                if (req.Method == "OPTIONS")
                {
                    var corsResponse = req.CreateResponse(HttpStatusCode.OK);
                    corsResponse.Headers.Add("Access-Control-Allow-Origin", "*");
                    corsResponse.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
                    corsResponse.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Mcp-Session-Id, Authorization");
                    corsResponse.Headers.Add("Access-Control-Max-Age", "86400");
                    return corsResponse;
                }

                _logger.LogInformation("MCP request received");

                // Extrai/gera Session-Id
                string sessionId = GetSessionId(req) ?? Guid.NewGuid().ToString();
                _logger.LogInformation("Processing request for session: {SessionId}", sessionId);

                // Corpo da requisição
                string requestBody = await req.ReadAsStringAsync();
                if (string.IsNullOrEmpty(requestBody))
                {
                    return await CreateErrorResponse(req, null, -32600, "Invalid Request", "Request body is empty");
                }

                // Desserializa JSON-RPC
                MCPRequest? mcpRequest;
                try
                {
                    mcpRequest = JsonConvert.DeserializeObject<MCPRequest>(requestBody);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize MCP request");
                    return await CreateErrorResponse(req, null, -32700, "Parse error", "Invalid JSON");
                }

                if (mcpRequest == null || string.IsNullOrWhiteSpace(mcpRequest.Method))
                {
                    return await CreateErrorResponse(req, null, -32600, "Invalid Request", "Request is null or method missing");
                }

                _logger.LogInformation("MCP {Method} (id: {Id})", mcpRequest.Method, mcpRequest.Id ?? "<notification>");

                // Processa o pedido (pode retornar null para notificações)
                var response = await ProcessMCPRequest(mcpRequest, req, sessionId);

                // 🔹 Notificação: não retorna envelope JSON-RPC
                if (response is null)
                {
                    var noContent = req.CreateResponse(HttpStatusCode.NoContent);
                    noContent.Headers.Add("Access-Control-Allow-Origin", "*");
                    noContent.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
                    noContent.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Mcp-Session-Id, Authorization");
                    noContent.Headers.Add("Access-Control-Expose-Headers", "Mcp-Session-Id");
                    noContent.Headers.Add("Mcp-Session-Id", sessionId);
                    _sessions[sessionId] = DateTime.UtcNow;
                    return noContent;
                }

                // 🔹 Chamada RPC normal: retorna envelope JSON-RPC
                var jsonResponse = JsonConvert.SerializeObject(response, Formatting.None);
                _logger.LogInformation("Serialized response: {Json}", jsonResponse);

                var result = req.CreateResponse(HttpStatusCode.OK);
                result.Headers.Add("Content-Type", "application/json");
                result.Headers.Add("Access-Control-Allow-Origin", "*");
                result.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
                result.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Mcp-Session-Id, Authorization");
                result.Headers.Add("Access-Control-Expose-Headers", "Mcp-Session-Id");
                result.Headers.Add("Mcp-Session-Id", sessionId);

                await result.WriteStringAsync(jsonResponse);
                _sessions[sessionId] = DateTime.UtcNow;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing MCP request");
                return await CreateErrorResponse(req, null, -32603, "Internal error", ex.Message);
            }
        }

        // Agora pode retornar null para notificações (sem resposta)
        private async Task<MCPResponse?> ProcessMCPRequest(MCPRequest request, HttpRequestData httpRequest, string sessionId)
        {
            try
            {
                // 🔹 Notificação JSON-RPC: id ausente → não responder
                var isNotification = request.Id == null || (request.Id is string s && string.IsNullOrWhiteSpace(s));
                if (isNotification)
                {
                    switch (request.Method)
                    {
                        case "notifications/initialized":
                            _logger.LogInformation("MCP: notifications/initialized recebido (sessão {SessionId})", sessionId);
                            _sessions[sessionId] = DateTime.UtcNow; // marca como ativa
                            return null;

                        case "notifications/exit":
                            _logger.LogInformation("MCP: notifications/exit recebido (sessão {SessionId})", sessionId);
                            // opcional: limpar recursos da sessão
                            return null;

                        default:
                            _logger.LogInformation("MCP: notificação não mapeada: {Method}", request.Method);
                            return null;
                    }
                }

                // 🔹 Para chamadas RPC (com id), valide sessão (exceto initialize)
                if (!string.Equals(request.Method, "initialize", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(sessionId) || !_sessions.ContainsKey(sessionId))
                    {
                        return new MCPResponse
                        {
                            Id = request.Id,
                            Error = new MCPError
                            {
                                Code = -32002,
                                Message = "Invalid session",
                                Data = "Session not found or expired"
                            }
                        };
                    }
                }

                // Atualiza timestamp da sessão
                _sessions[sessionId] = DateTime.UtcNow;

                // Dispatcher de métodos
                object? result = request.Method switch
                {
                    "initialize" => await HandleInitialize(request, sessionId, httpRequest),
                    "tools/list" => await _mcpService.ListTools(),
                    "tools/call" => await HandleToolCall(request, sessionId),
                    "resources/list" => await _mcpService.ListResources(),
                    "resources/read" => await HandleResourceRead(request, sessionId),
                    "migrate_skills" => await HandleMigrateSkills(request, sessionId),
                    "session/terminate" => HandleSessionTermination(sessionId, httpRequest),
                    _ => throw new InvalidOperationException($"Unknown method: {request.Method}")
                };

                return new MCPResponse { Id = request.Id, Result = result };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MCP method: {Method} for session {SessionId}", request.Method, sessionId);
                return new MCPResponse
                {
                    Id = request.Id,
                    Error = new MCPError
                    {
                        Code = -32603,
                        Message = "Internal error",
                        Data = ex.Message
                    }
                };
            }
        }

        private async Task<InitializeResult> HandleInitialize(MCPRequest request, string sessionId, HttpRequestData httpRequest)
        {
            _sessions[sessionId] = DateTime.UtcNow;

            var result = await _mcpService.Initialize();
            result.SessionId = sessionId; // devolve o sessionId ao cliente

            _logger.LogInformation("Session initialized with ID: {SessionId}", sessionId);
            return result;
        }

        private async Task<CallToolResult> HandleToolCall(MCPRequest request, string sessionId)
        {
            var toolParams = JsonConvert.DeserializeObject<CallToolParams>(request.Params?.ToString() ?? "{}");
            if (toolParams == null)
                throw new ArgumentException("Invalid tool parameters");

            _logger.LogInformation("Calling tool: {Tool} with arguments: {Args}",
                toolParams.Name, JsonConvert.SerializeObject(toolParams.Arguments));

            var result = await _mcpService.CallTool(toolParams.Name, toolParams.Arguments ?? new Dictionary<string, object>());

            _logger.LogInformation("Tool call result: {Result}", JsonConvert.SerializeObject(result));
            return result;
        }

        private async Task<object> HandleResourceRead(MCPRequest request, string sessionId)
        {
            return await _mcpService.ReadResource(request.Params?.ToString() ?? "");
        }

        private object HandleSessionTermination(string sessionId, HttpRequestData httpRequest)
        {
            if (!string.IsNullOrEmpty(sessionId) && _sessions.ContainsKey(sessionId))
            {
                _sessions.Remove(sessionId);
            }
            return new { terminated = true };
        }

        private string? GetSessionId(HttpRequestData request)
        {
            if (request.Headers.Contains("Mcp-Session-Id"))
            {
                return request.Headers.GetValues("Mcp-Session-Id").FirstOrDefault();
            }
            return null;
        }

        private async Task<object> HandleMigrateSkills(MCPRequest request, string sessionId)
        {
            try
            {
                var parameters = new Dictionary<string, object>();
                if (request.Params is JsonElement json && json.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in json.EnumerateObject())
                    {
                        parameters[p.Name] = p.Value.ValueKind switch
                        {
                            JsonValueKind.String => p.Value.GetString()!,
                            JsonValueKind.Number => p.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => p.Value.ToString()
                        };
                    }
                }

                return await _mcpService.MigrateSkills(parameters);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling migrate_skills request for session {SessionId}", sessionId);
                throw;
            }
        }

        private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string? sessionId, int code, string message, object? data = null)
        {
            var errorResponse = new MCPResponse
            {
                Id = null,
                Error = new MCPError { Code = code, Message = message, Data = data }
            };

            var jsonResponse = JsonConvert.SerializeObject(errorResponse, Formatting.None);
            var result = req.CreateResponse(HttpStatusCode.OK);
            result.Headers.Add("Content-Type", "application/json");
            result.Headers.Add("Access-Control-Allow-Origin", "*");
            result.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            result.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Mcp-Session-Id, Authorization");
            result.Headers.Add("Access-Control-Expose-Headers", "Mcp-Session-Id");

            if (!string.IsNullOrEmpty(sessionId))
                result.Headers.Add("Mcp-Session-Id", sessionId);

            await result.WriteStringAsync(jsonResponse);
            return result;
        }
    }
}

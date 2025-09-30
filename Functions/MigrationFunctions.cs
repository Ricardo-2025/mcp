using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GenesysMigrationMCP.Services;
using GenesysMigrationMCP.Models;
using Newtonsoft.Json;
using System.Text.Json;
using System.Net;
using System.Linq; // para Select/Count

namespace GenesysMigrationMCP.Functions
{
    public class MCPServerFunction
    {
        private readonly IMcpService _mcpService;
        private readonly ISessionService _sessionService;
        private readonly ILogger<MCPServerFunction> _logger;

        public MCPServerFunction(IMcpService mcpService, ISessionService sessionService, ILogger<MCPServerFunction> logger)
        {
            _mcpService = mcpService;
            _sessionService = sessionService;
            _logger = logger;
        }

        [Function("MCPEndpoint")]
        public async Task<HttpResponseData> HandleMCPRequest(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "mcp")] HttpRequestData req)
        {
            try
            {
                // Preflight CORS
                if (req.Method == "OPTIONS")
                {
                    var corsResponse = req.CreateResponse(HttpStatusCode.OK);
                    AddCors(corsResponse);
                    return corsResponse;
                }

                _logger.LogInformation("MCP request received");

                // Extrai/gera Session-Id (normalizado)
                string sessionId = GetSessionId(req) ?? await _sessionService.CreateSessionAsync();
                _logger.LogInformation("Processing request for session: {SessionId}", sessionId);

                // Corpo da requisição
                string requestBody = await req.ReadAsStringAsync();
                if (string.IsNullOrEmpty(requestBody))
                {
                    return await CreateErrorResponse(req, sessionId, -32600, "Invalid Request", "Request body is empty");
                }

                // Desserializa JSON-RPC (Newtonsoft)
                MCPRequest? mcpRequest;
                try
                {
                    mcpRequest = JsonConvert.DeserializeObject<MCPRequest>(requestBody);
                }
                catch (Newtonsoft.Json.JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize MCP request");
                    return await CreateErrorResponse(req, sessionId, -32700, "Parse error", "Invalid JSON");
                }

                if (mcpRequest == null || string.IsNullOrWhiteSpace(mcpRequest.Method))
                {
                    return await CreateErrorResponse(req, sessionId, -32600, "Invalid Request", "Request is null or method missing");
                }

                _logger.LogInformation("MCP {Method} (id: {Id}) for session {SessionId}", mcpRequest.Method, mcpRequest.Id ?? "<notification>", sessionId);

                // Processa o pedido (pode retornar null para notificações)
                var response = await ProcessMCPRequest(mcpRequest, req, sessionId);

                // 🔹 Notificação: não retorna envelope JSON-RPC → devolve 200 {}
                if (response is null)
                {
                    var ok = req.CreateResponse(HttpStatusCode.OK);
                    AddCors(ok);
                    AddSessionHeaders(ok, sessionId); // envia apenas o header canônico
                    ok.Headers.Add("Content-Type", "application/json");
                    await ok.WriteStringAsync("{}");
                    await _sessionService.UpdateSessionActivityAsync(sessionId);
                    return ok;
                }

                // 🔹 Chamada RPC normal
                var jsonResponse = JsonConvert.SerializeObject(response, Formatting.None);
                _logger.LogInformation("Serialized response: {Json}", jsonResponse);

                var result = req.CreateResponse(HttpStatusCode.OK);
                AddCors(result);
                AddSessionHeaders(result, sessionId); // envia apenas o header canônico
                result.Headers.Add("Content-Type", "application/json");
                await result.WriteStringAsync(jsonResponse);
                await _sessionService.UpdateSessionActivityAsync(sessionId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing MCP request");
                var sessionId = GetSessionId(req);
                return await CreateErrorResponse(req, sessionId, -32603, "Internal error", ex.Message);
            }
        }

        // Dispatcher
        private async Task<MCPResponse?> ProcessMCPRequest(MCPRequest request, HttpRequestData httpRequest, string sessionId)
        {
            try
            {
                // 🔹 Notificação JSON-RPC: id ausente
                var isNotification = request.Id == null || (request.Id is string s && string.IsNullOrWhiteSpace(s));
                if (isNotification)
                {
                    switch (request.Method)
                    {
                        case "notifications/initialized":
                            _logger.LogInformation("MCP: notifications/initialized recebido (sessão {SessionId})", sessionId);
                            await _sessionService.UpdateSessionActivityAsync(sessionId);
                            return null;

                        case "notifications/exit":
                            _logger.LogInformation("MCP: notifications/exit recebido (sessão {SessionId})", sessionId);
                            await _sessionService.RemoveSessionAsync(sessionId);
                            return null;

                        default:
                            _logger.LogInformation("MCP: notificação não mapeada: {Method}", request.Method);
                            return null;
                    }
                }

                // 🔹 Para chamadas RPC (com id), valide sessão (exceto initialize)
                if (!string.Equals(request.Method, "initialize", StringComparison.OrdinalIgnoreCase))
                {
                    var valid = await _sessionService.ValidateSessionAsync(sessionId);
                    if (!valid)
                    {
                        // Tolerância: auto-cria sessão para métodos "leitura" (evita invalid session em clients que chamam fora de ordem)
                        if (request.Method == "tools/list" || request.Method == "resources/list")
                        {
                            _logger.LogWarning("Sessão inválida para {Method}. Criando nova sessão e seguindo.", request.Method);
                            sessionId = await _sessionService.CreateSessionAsync();
                        }
                        else
                        {
                            _logger.LogWarning("Invalid or expired session: {SessionId} for method: {Method}", sessionId, request.Method);
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
                }

                await _sessionService.UpdateSessionActivityAsync(sessionId);

                object? result = request.Method switch
                {
                    "initialize" => await HandleInitialize(request, sessionId, httpRequest),
                    "tools/list" => await HandleToolsList(request),        // ⬅️ paginado
                    "tools/call" => await HandleToolCall(request, sessionId),
                    "resources/list" => await _mcpService.ListResources(),
                    "resources/read" => await HandleResourceRead(request, sessionId),
                    "migrate_skills" => await HandleMigrateSkills(request, sessionId),
                    "session/terminate" => await HandleSessionTermination(sessionId, httpRequest),
                    "session/info" => await HandleSessionInfo(sessionId),
                    "session/list" => await HandleListSessions(),
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

        // ---------- Handlers ----------

        private async Task<InitializeResult> HandleInitialize(MCPRequest request, string sessionId, HttpRequestData httpRequest)
        {
            if (!await _sessionService.ValidateSessionAsync(sessionId))
            {
                sessionId = await _sessionService.CreateSessionAsync();
            }
            await _sessionService.UpdateSessionActivityAsync(sessionId);

            var result = await _mcpService.Initialize();
            result.SessionId = sessionId;
            _logger.LogInformation("Session initialized with ID: {SessionId}", sessionId);
            return result;
        }

        // tools/list paginado: limit <= 200, cursor = base64(offset)
        private async Task<object> HandleToolsList(MCPRequest request)
        {
            int limit = 90;   // default
            int offset = 0;
            bool all = false; // ⬅️ novo

            try
            {
                if (request.Params != null)
                {
                    var raw = request.Params is string s
                        ? Newtonsoft.Json.Linq.JToken.Parse(s)
                        : Newtonsoft.Json.Linq.JToken.FromObject(request.Params);

                    if (raw["limit"] != null)
                        limit = Math.Clamp((int)raw["limit"], 1, 300); // cap maior mas seguro

                    if (raw["cursor"] != null)
                    {
                        var c = (string)raw["cursor"];
                        if (!string.IsNullOrWhiteSpace(c))
                        {
                            var bytes = System.Convert.FromBase64String(c);
                            if (int.TryParse(System.Text.Encoding.UTF8.GetString(bytes), out var o))
                                offset = Math.Max(0, o);
                        }
                    }

                    if (raw["all"] != null) // ⬅️ novo
                        all = (bool)raw["all"];
                }
            }
            catch
            {
                // usa defaults se params inválidos
            }

            // ⬅️ Modo "all": junta páginas até o cap
            if (all)
            {
                const int hardCap = 300; // evite respostas gigantes
                var acc = new List<ToolListItem>();
                int total = 0;
                int cursor = offset;
                int pageLimit = Math.Min(limit, hardCap);

                while (acc.Count < hardCap)
                {
                    var page = await _mcpService.ListToolsPaged(cursor, pageLimit);
                    if (page.Items.Count == 0)
                    {
                        total = page.Total;
                        break;
                    }

                    acc.AddRange(page.Items);
                    total = page.Total;
                    cursor += page.Items.Count;

                    if (cursor >= total) break;           // fim
                    if (acc.Count >= hardCap) break;      // cap
                }

                _logger.LogInformation("tools/list(all) -> total={Total} returned={Returned} offset={Offset}",
                    total, acc.Count, offset);

                return new
                {
                    tools = acc,
                    paging = new
                    {
                        total,
                        limit = acc.Count,
                        cursor = cursor < total
                            ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(cursor.ToString()))
                            : null
                    }
                };
            }

            // ⬅️ Modo paginado normal
            var singlePage = await _mcpService.ListToolsPaged(offset, limit);

            string? nextCursor = null;
            var nextOffset = offset + singlePage.Items.Count;
            if (singlePage.Total > nextOffset)
                nextCursor = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(nextOffset.ToString()));

            _logger.LogInformation("tools/list -> total={Total} pageCount={Count} limit={Limit} offset={Offset}",
                singlePage.Total, singlePage.Items.Count, limit, offset);

            return new
            {
                tools = singlePage.Items,
                paging = new { total = singlePage.Total, limit, cursor = nextCursor }
            };
        }

        private async Task<CallToolResult> HandleToolCall(MCPRequest request, string sessionId)
        {
            var toolParams = JsonConvert.DeserializeObject<CallToolParams>(request.Params?.ToString() ?? "{}");
            if (toolParams == null)
                throw new ArgumentException("Invalid tool parameters");

            _logger.LogInformation("Calling tool: {Tool} with arguments: {Args} for session: {SessionId}",
                toolParams.Name, JsonConvert.SerializeObject(toolParams.Arguments), sessionId);

            var result = await _mcpService.CallTool(toolParams.Name, toolParams.Arguments ?? new Dictionary<string, object>());
            _logger.LogInformation("Tool call result: {Result}", JsonConvert.SerializeObject(result));
            return result;
        }

        private async Task<object> HandleResourceRead(MCPRequest request, string sessionId)
        {
            return await _mcpService.ReadResource(request.Params?.ToString() ?? "");
        }

        private async Task<object> HandleSessionTermination(string sessionId, HttpRequestData httpRequest)
        {
            await _sessionService.RemoveSessionAsync(sessionId);
            _logger.LogInformation("Session terminated: {SessionId}", sessionId);
            return new { terminated = true, sessionId = sessionId };
        }

        private async Task<object> HandleSessionInfo(string sessionId)
        {
            var sessionInfo = await _sessionService.GetSessionInfoAsync(sessionId);
            if (sessionInfo == null)
            {
                return new { error = "Session not found", sessionId = sessionId };
            }

            return new
            {
                sessionId = sessionInfo.SessionId,
                createdAt = sessionInfo.CreatedAt,
                lastActivity = sessionInfo.LastActivity,
                isActive = sessionInfo.IsActive,
                durationMinutes = (DateTime.UtcNow - sessionInfo.CreatedAt).TotalMinutes
            };
        }

        private async Task<object> HandleListSessions()
        {
            var sessions = await _sessionService.GetActiveSessionsAsync();
            return new
            {
                activeSessions = sessions.Count(),
                sessions = sessions.Select(s => new
                {
                    sessionId = s.SessionId,
                    createdAt = s.CreatedAt,
                    lastActivity = s.LastActivity,
                    durationMinutes = (DateTime.UtcNow - s.CreatedAt).TotalMinutes
                })
            };
        }

        // ---------- Util ----------

        private static string? NormalizeSessionId(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Se vier "id1,id2,..." (alguns clientes concatenam valores), pega o primeiro
            var first = raw.Split(',')[0].Trim();

            // Opcional: validar como GUID
            if (Guid.TryParse(first, out _)) return first;

            // Se não for GUID, devolve o token limpo mesmo assim
            return first;
        }

        private string? GetSessionId(HttpRequestData request)
        {
            // tenta variantes do header
            string[] names = { "Mcp-Session-Id", "mcp-session-id", "X-MCP-Session-Id", "x-mcp-session-id" };
            foreach (var n in names)
            {
                if (request.Headers.Contains(n))
                {
                    var val = request.Headers.GetValues(n).FirstOrDefault();
                    var norm = NormalizeSessionId(val);
                    if (!string.IsNullOrEmpty(norm)) return norm;
                }
            }

            // fallback: query string
            var uri = request.Url;
            if (!string.IsNullOrEmpty(uri.Query))
            {
                var qs = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var norm = NormalizeSessionId(qs.Get("mcpSessionId") ?? qs.Get("sessionId"));
                if (!string.IsNullOrEmpty(norm)) return norm;
            }
            return null;
        }

        private async Task<object> HandleMigrateSkills(MCPRequest request, string sessionId)
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

        private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string? sessionId, int code, string message, object? data = null)
        {
            var errorResponse = new MCPResponse
            {
                Id = null,
                Error = new MCPError { Code = code, Message = message, Data = data }
            };

            var jsonResponse = JsonConvert.SerializeObject(errorResponse, Formatting.None);
            var result = req.CreateResponse(HttpStatusCode.OK);
            AddCors(result);
            if (!string.IsNullOrEmpty(sessionId)) AddSessionHeaders(result, sessionId);
            result.Headers.Add("Content-Type", "application/json");
            await result.WriteStringAsync(jsonResponse);
            return result;
        }

        // helpers
        private static void AddCors(HttpResponseData resp)
        {
            resp.Headers.Add("Access-Control-Allow-Origin", "*");
            resp.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            resp.Headers.Add("Access-Control-Allow-Headers",
                "Content-Type, Authorization, x-functions-key, Mcp-Session-Id, mcp-session-id, X-MCP-Session-Id");
            resp.Headers.Add("Access-Control-Expose-Headers",
                "Mcp-Session-Id, mcp-session-id, X-MCP-Session-Id");
        }

        private static void AddSessionHeaders(HttpResponseData resp, string sessionId)
        {
            // ✅ Envie apenas o header canônico para evitar concatenção "id,id" no client
            resp.Headers.Add("Mcp-Session-Id", sessionId);
        }
    }
}

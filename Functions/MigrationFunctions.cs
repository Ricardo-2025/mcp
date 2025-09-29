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
        private static readonly Dictionary<string, DateTime> _sessions = new();

        public MCPServerFunction(IMcpService mcpService, ILogger<MCPServerFunction> logger)
        {
            _mcpService = mcpService;
            _logger = logger;
        }

        [Function("MCPEndpoint")]
        public async Task<HttpResponseData> HandleMCPRequest(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "mcp")] HttpRequestData req)
        {
            try
            {
                // Handle CORS preflight requests
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

                // Extract session ID from headers or generate new one
                string sessionId = GetSessionId(req) ?? Guid.NewGuid().ToString();
                
                _logger.LogInformation($"Processing request for session: {sessionId}");

                // Read request body
                string requestBody = await req.ReadAsStringAsync();
                
                if (string.IsNullOrEmpty(requestBody))
                {
                    return await CreateErrorResponse(req, null, -32600, "Invalid Request", "Request body is empty");
                }

                // Deserialize MCP request
                MCPRequest? mcpRequest;
                try
                {
                    mcpRequest = JsonConvert.DeserializeObject<MCPRequest>(requestBody);
                }
                catch (Newtonsoft.Json.JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize MCP request");
                    return await CreateErrorResponse(req, null, -32700, "Parse error", "Invalid JSON");
                }

                if (mcpRequest == null)
                {
                    return await CreateErrorResponse(req, null, -32600, "Invalid Request", "Request is null");
                }

                // Process the MCP request
                var response = await ProcessMCPRequest(mcpRequest, req, sessionId);

                // Create HTTP response
                var jsonResponse = JsonConvert.SerializeObject(response, Formatting.None);
                _logger.LogInformation($"Serialized response: {jsonResponse}");
                
                var result = req.CreateResponse(HttpStatusCode.OK);
                result.Headers.Add("Content-Type", "application/json");
                
                if (string.IsNullOrEmpty(jsonResponse) || jsonResponse == "null")
                {
                    _logger.LogWarning("Response is null or empty, creating default response");
                    jsonResponse = JsonConvert.SerializeObject(new { error = "Empty response" });
                }
                
                await result.WriteStringAsync(jsonResponse);
                _logger.LogInformation($"Response written with length: {jsonResponse.Length}");

                // Add CORS and session headers to response
                result.Headers.Add("Access-Control-Allow-Origin", "*");
                result.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
                result.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Mcp-Session-Id");
                result.Headers.Add("Access-Control-Expose-Headers", "Mcp-Session-Id");

                // Add session ID to response headers
                result.Headers.Add("Mcp-Session-Id", sessionId);
                _sessions[sessionId] = DateTime.UtcNow;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing MCP request");
                return await CreateErrorResponse(req, null, -32603, "Internal error", ex.Message);
            }
        }

        private async Task<MCPResponse> ProcessMCPRequest(MCPRequest request, HttpRequestData httpRequest, string sessionId)
        {
            try
            {
                // Validate session for non-initialize methods
                if (request.Method != "initialize")
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

                // Update session timestamp
                _sessions[sessionId] = DateTime.UtcNow;

                // Process method
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

                return new MCPResponse
                {
                    Id = request.Id,
                    Result = result
                };
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
            
            // Adicionar sessionId à resposta
            result.SessionId = sessionId;
            _logger.LogInformation($"Session initialized with ID: {sessionId}");
            
            return result;
        }

        private async Task<CallToolResult> HandleToolCall(MCPRequest request, string sessionId)
        {
            var toolParams = JsonConvert.DeserializeObject<CallToolParams>(request.Params?.ToString() ?? "{}");
            if (toolParams == null)
            {
                throw new ArgumentException("Invalid tool parameters");
            }
            
            _logger.LogInformation($"Calling tool: {toolParams.Name} with arguments: {JsonConvert.SerializeObject(toolParams.Arguments)}");
            
            var result = await _mcpService.CallTool(toolParams.Name, toolParams.Arguments ?? new Dictionary<string, object>());
            
            _logger.LogInformation($"Tool call result: {JsonConvert.SerializeObject(result)}");
            
            return result;
        }

        private async Task<object> HandleResourceRead(MCPRequest request, string sessionId)
        {
            // Implementation for resource reading
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

        private string GenerateSessionId()
        {
            return Guid.NewGuid().ToString();
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
                // Extract parameters from request
                var parameters = new Dictionary<string, object>();
                
                if (request.Params != null)
                {
                    if (request.Params is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in jsonElement.EnumerateObject())
                        {
                            parameters[property.Name] = property.Value.ValueKind switch
                            {
                                JsonValueKind.String => property.Value.GetString(),
                                JsonValueKind.Number => property.Value.GetDouble(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => property.Value.ToString()
                            };
                        }
                    }
                }
                
                // Call the MCP service to migrate skills
                var result = await _mcpService.MigrateSkills(parameters);
                return result;
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
                Error = new MCPError
                {
                    Code = code,
                    Message = message,
                    Data = data
                }
            };

            var jsonResponse = JsonConvert.SerializeObject(errorResponse, Formatting.None);
            var result = req.CreateResponse(HttpStatusCode.OK);
            result.Headers.Add("Content-Type", "application/json");
            await result.WriteStringAsync(jsonResponse);
            
            // Add session ID to response headers if provided
            if (!string.IsNullOrEmpty(sessionId))
            {
                result.Headers.Add("Mcp-Session-Id", sessionId);
            }

            return result;
        }
    }
}
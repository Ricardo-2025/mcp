using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace GenesysMigrationMCP.Models
{
    // Modelo base para requisições MCP
    public class MCPRequest
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("method")]
        [Required]
        public string Method { get; set; } = string.Empty;

        [JsonProperty("params")]
        public object? Params { get; set; }
    }

    // Modelo base para respostas MCP
    public class MCPResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("result")]
        public object? Result { get; set; }

        [JsonProperty("error")]
        public MCPError? Error { get; set; }
    }

    // Modelo para erros MCP
    public class MCPError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        [JsonProperty("data")]
        public object? Data { get; set; }
    }

    // Modelo para inicialização do servidor
    public class InitializeParams
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; } = "2024-11-05";

        [JsonProperty("capabilities")]
        public ClientCapabilities Capabilities { get; set; } = new();

        [JsonProperty("clientInfo")]
        public ClientInfo ClientInfo { get; set; } = new();
    }

    public class ClientCapabilities
    {
        [JsonProperty("roots")]
        public RootsCapability? Roots { get; set; }

        [JsonProperty("sampling")]
        public object? Sampling { get; set; }
    }

    public class RootsCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    public class ClientInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;
    }

    // Modelo para resposta de inicialização
    public class InitializeResult
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; } = "2024-11-05";

        [JsonProperty("capabilities")]
        public ServerCapabilities Capabilities { get; set; } = new();

        [JsonProperty("serverInfo")]
        public ServerInfo ServerInfo { get; set; } = new();

        [JsonProperty("sessionId")]
        public string? SessionId { get; set; }
    }

    public class ServerCapabilities
    {
        [JsonProperty("logging")]
        public object? Logging { get; set; }

        [JsonProperty("prompts")]
        public PromptsCapability? Prompts { get; set; }

        [JsonProperty("resources")]
        public ResourcesCapability? Resources { get; set; }

        [JsonProperty("tools")]
        public ToolsCapability? Tools { get; set; }
    }

    public class PromptsCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    public class ResourcesCapability
    {
        [JsonProperty("subscribe")]
        public bool Subscribe { get; set; }

        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    public class ToolsCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    public class ServerInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "GenesysMigrationMCP";

        [JsonProperty("version")]
        public string Version { get; set; } = "1.0.0";
    }

    // Modelos para Tools
    public class Tool
    {
        [JsonProperty("name")]
        [Required]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("inputSchema")]
        public ToolInputSchema InputSchema { get; set; } = new();
    }

    public class ToolInputSchema
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "object";

        [JsonProperty("properties")]
        public Dictionary<string, object>? Properties { get; set; }

        [JsonProperty("required")]
        public string[]? Required { get; set; }
    }

    public class ListToolsResult
    {
        [JsonProperty("tools")]
        public Tool[] Tools { get; set; } = Array.Empty<Tool>();
    }

    public class CallToolParams
    {
        [JsonProperty("name")]
        [Required]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("arguments")]
        public Dictionary<string, object>? Arguments { get; set; }
    }

    public class CallToolResult
    {
        [JsonProperty("content")]
        public ToolContent[] Content { get; set; } = Array.Empty<ToolContent>();

        [JsonProperty("isError")]
        public bool IsError { get; set; }
    }

    public class ToolContent
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "text";

        [JsonProperty("text")]
        public string? Text { get; set; }
    }

    // Modelos para migração (compatibilidade com código existente)
    public class MigrationOptions
    {
        public Dictionary<string, object> Settings { get; set; } = new();
        public bool ValidateBeforeMigration { get; set; } = true;
        public bool CreateBackup { get; set; } = true;
    }

    public class MigrationStatus
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Progress { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
    }

    public class MigrationStatistics
    {
        public int TotalMigrations { get; set; }
        public int SuccessfulMigrations { get; set; }
        public int FailedMigrations { get; set; }
        public TimeSpan AverageExecutionTime { get; set; }
        public DateTime LastMigration { get; set; }
        
        // Propriedades adicionais para compatibilidade
        public int TotalFlows { get; set; }
        public int MigratedFlows { get; set; }
        public int FailedFlows { get; set; }
        public int TotalWorkstreams { get; set; }
        public int TotalBotConfigurations { get; set; }
        public TimeSpan ExecutionTime { get; set; }
    }

    // Configuração MCP
    public class McpConfiguration
    {
        public string ServerName { get; set; } = "GenesysMigrationMCP";
        public string Version { get; set; } = "1.0.0";
        public string ProtocolVersion { get; set; } = "2024-11-05";
        public bool EnableLogging { get; set; } = true;
        public bool EnableCors { get; set; } = true;
    }
}
using GenesysMigrationMCP.Models;

namespace GenesysMigrationMCP.Services
{
    public interface IMcpService
    {
        /// <summary>
        /// Inicializa o servidor MCP
        /// </summary>
        Task<InitializeResult> Initialize();

        /// <summary>
        /// Lista todas as tools disponíveis
        /// </summary>
        Task<ListToolsResult> ListTools();

        /// <summary>
        /// Executa uma tool específica
        /// </summary>
        Task<CallToolResult> CallTool(string name, Dictionary<string, object> arguments);

        /// <summary>
        /// Lista recursos disponíveis
        /// </summary>
        Task<object> ListResources();

        /// <summary>
        /// Lê um recurso específico
        /// </summary>
        Task<object> ReadResource(string uri);

        /// <summary>
        /// Migra skills do Genesys para o Dynamics
        /// </summary>
        Task<object> MigrateSkills(Dictionary<string, object> arguments);
    }
}
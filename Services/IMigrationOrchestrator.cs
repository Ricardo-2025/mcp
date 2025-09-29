using GenesysMigrationMCP.Models;

namespace GenesysMigrationMCP.Services
{
    /// <summary>
    /// Interface para orquestração de migrações
    /// </summary>
    public interface IMigrationOrchestrator
    {
        /// <summary>
        /// Extrai flows do Genesys Cloud
        /// </summary>
        Task<object> ExtractGenesysFlowsAsync(string migrationId, Dictionary<string, object> parameters);

        /// <summary>
        /// Migra dados para o Dynamics
        /// </summary>
        Task<object> MigrateToDynamicsAsync(string migrationId, Dictionary<string, object> parameters);

        /// <summary>
        /// Executa migração completa (extração + migração)
        /// </summary>
        Task<object> ExecuteCompleteMigrationAsync(string migrationId, MigrationOptions? options);

        /// <summary>
        /// Valida conexões com Genesys e Dynamics
        /// </summary>
        Task<object> ValidateConnectionsAsync(string migrationId);

        /// <summary>
        /// Obtém estatísticas de migração
        /// </summary>
        Task<MigrationStatistics> GetStatisticsAsync();

        /// <summary>
        /// Atualiza o progresso de uma migração
        /// </summary>
        Task UpdateMigrationProgressAsync(string migrationId, int progress, string message);
    }
}
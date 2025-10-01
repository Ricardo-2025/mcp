using GenesysMigrationMCP.Models;

namespace GenesysMigrationMCP.Services
{
    /// <summary>
    /// Interface para serviço especializado em análise de mapeamento entre Genesys e Dynamics
    /// </summary>
    public interface IMappingAnalysisService
    {
        /// <summary>
        /// Gera relatório completo de análise de mapeamento
        /// </summary>
        /// <param name="includeDetailedAnalysis">Se deve incluir análise detalhada de cada entidade</param>
        /// <param name="entityTypes">Tipos de entidade específicos para analisar (null = todos)</param>
        /// <returns>Relatório completo de mapeamento</returns>
        Task<MappingAnalysisReport> GenerateCompleteMappingReportAsync(
            bool includeDetailedAnalysis = true, 
            List<string>? entityTypes = null);

        /// <summary>
        /// Analisa mapeamento para um tipo específico de entidade
        /// </summary>
        /// <param name="entityType">Tipo de entidade (Users, Queues, Flows, etc.)</param>
        /// <returns>Mapeamento detalhado do tipo de entidade</returns>
        Task<EntityTypeMapping> AnalyzeEntityTypeMappingAsync(string entityType);

        /// <summary>
        /// Compara entidades específicas entre Genesys e Dynamics
        /// </summary>
        /// <param name="genesysEntityId">ID da entidade no Genesys</param>
        /// <param name="dynamicsEntityId">ID da entidade no Dynamics (opcional)</param>
        /// <param name="entityType">Tipo da entidade</param>
        /// <returns>Comparação detalhada das entidades</returns>
        Task<EntityComparison> CompareEntitiesAsync(
            string genesysEntityId, 
            string? dynamicsEntityId, 
            string entityType);

        /// <summary>
        /// Avalia riscos da migração baseado no mapeamento atual
        /// </summary>
        /// <param name="entityTypes">Tipos de entidade para avaliar (null = todos)</param>
        /// <returns>Avaliação completa de riscos</returns>
        Task<RiskAssessment> AssessMigrationRisksAsync(List<string>? entityTypes = null);

        /// <summary>
        /// Analisa qualidade dos dados no Genesys
        /// </summary>
        /// <param name="entityTypes">Tipos de entidade para analisar (null = todos)</param>
        /// <returns>Análise de qualidade dos dados</returns>
        Task<DataQualityAnalysis> AnalyzeDataQualityAsync(List<string>? entityTypes = null);

        /// <summary>
        /// Gera plano de migração baseado na análise de mapeamento
        /// </summary>
        /// <param name="migrationStrategy">Estratégia de migração (Phased, BigBang, Parallel)</param>
        /// <param name="priorityEntityTypes">Tipos de entidade prioritários</param>
        /// <returns>Plano detalhado de migração</returns>
        Task<MigrationPlan> GenerateMigrationPlanAsync(
            string migrationStrategy = "Phased", 
            List<string>? priorityEntityTypes = null);

        /// <summary>
        /// Obtém recomendações específicas para migração
        /// </summary>
        /// <param name="category">Categoria das recomendações (Strategy, Technical, Process, Risk)</param>
        /// <param name="entityType">Tipo de entidade específico (opcional)</param>
        /// <returns>Lista de recomendações</returns>
        Task<List<MigrationRecommendation>> GetMigrationRecommendationsAsync(
            string? category = null, 
            string? entityType = null);

        /// <summary>
        /// Valida se uma entidade do Genesys pode ser migrada para o Dynamics
        /// </summary>
        /// <param name="genesysEntityId">ID da entidade no Genesys</param>
        /// <param name="entityType">Tipo da entidade</param>
        /// <returns>Resultado da validação com detalhes</returns>
        Task<MigrationValidationResult> ValidateEntityMigrationAsync(
            string genesysEntityId, 
            string entityType);

        /// <summary>
        /// Obtém estatísticas resumidas do mapeamento
        /// </summary>
        /// <returns>Resumo estatístico do mapeamento</returns>
        Task<MappingSummary> GetMappingSummaryAsync();

        /// <summary>
        /// Identifica dependências entre entidades para ordem de migração
        /// </summary>
        /// <param name="entityType">Tipo de entidade para analisar dependências</param>
        /// <returns>Mapa de dependências</returns>
        Task<Dictionary<string, List<string>>> AnalyzeEntityDependenciesAsync(string entityType);

        /// <summary>
        /// Sugere mapeamento automático baseado em similaridade
        /// </summary>
        /// <param name="genesysEntityId">ID da entidade no Genesys</param>
        /// <param name="entityType">Tipo da entidade</param>
        /// <returns>Sugestões de mapeamento ordenadas por confiança</returns>
        Task<List<EntityMapping>> SuggestEntityMappingAsync(
            string genesysEntityId, 
            string entityType);

        /// <summary>
        /// Exporta relatório de mapeamento em diferentes formatos
        /// </summary>
        /// <param name="report">Relatório para exportar</param>
        /// <param name="format">Formato de exportação (JSON, Excel, PDF)</param>
        /// <returns>Dados do arquivo exportado</returns>
        Task<byte[]> ExportMappingReportAsync(
            MappingAnalysisReport report, 
            string format = "JSON");

        /// <summary>
        /// Atualiza cache de dados do Genesys e Dynamics
        /// </summary>
        /// <param name="forceRefresh">Força atualização mesmo se cache ainda válido</param>
        /// <returns>Status da atualização</returns>
        Task<bool> RefreshDataCacheAsync(bool forceRefresh = false);
    }

    /// <summary>
    /// Resultado da validação de migração de entidade
    /// </summary>
    public class MigrationValidationResult
    {
        [Newtonsoft.Json.JsonProperty("isValid")]
        public bool IsValid { get; set; }

        [Newtonsoft.Json.JsonProperty("validationErrors")]
        public List<string> ValidationErrors { get; set; } = new();

        [Newtonsoft.Json.JsonProperty("validationWarnings")]
        public List<string> ValidationWarnings { get; set; } = new();

        [Newtonsoft.Json.JsonProperty("requiredActions")]
        public List<string> RequiredActions { get; set; } = new();

        [Newtonsoft.Json.JsonProperty("estimatedEffort")]
        public string EstimatedEffort { get; set; } = "Medium";

        [Newtonsoft.Json.JsonProperty("migrationComplexity")]
        public string MigrationComplexity { get; set; } = "Medium";

        [Newtonsoft.Json.JsonProperty("recommendedApproach")]
        public string? RecommendedApproach { get; set; }
    }
}
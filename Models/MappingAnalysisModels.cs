using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace GenesysMigrationMCP.Models
{
    // ===== MODELOS PARA ANÁLISE DE MAPEAMENTO =====
    
    /// <summary>
    /// Relatório completo de mapeamento entre Genesys e Dynamics
    /// </summary>
    public class MappingAnalysisReport
    {
        [JsonProperty("reportId")]
        public string ReportId { get; set; } = Guid.NewGuid().ToString();
        
        [JsonProperty("generatedDate")]
        public DateTime GeneratedDate { get; set; } = DateTime.UtcNow;
        
        [JsonProperty("genesysEnvironment")]
        public string GenesysEnvironment { get; set; } = string.Empty;
        
        [JsonProperty("dynamicsEnvironment")]
        public string DynamicsEnvironment { get; set; } = string.Empty;
        
        [JsonProperty("summary")]
        public MappingSummary Summary { get; set; } = new();
        
        [JsonProperty("entityMappings")]
        public List<EntityTypeMapping> EntityMappings { get; set; } = new();
        
        [JsonProperty("migrationRecommendations")]
        public List<MigrationRecommendation> MigrationRecommendations { get; set; } = new();
        
        [JsonProperty("riskAssessment")]
        public RiskAssessment RiskAssessment { get; set; } = new();
        
        [JsonProperty("dataQualityAnalysis")]
        public DataQualityAnalysis DataQualityAnalysis { get; set; } = new();
        
        [JsonProperty("migrationPlan")]
        public MigrationPlan MigrationPlan { get; set; } = new();
    }
    
    /// <summary>
    /// Resumo executivo do mapeamento
    /// </summary>
    public class MappingSummary
    {
        [JsonProperty("totalGenesysEntities")]
        public int TotalGenesysEntities { get; set; }
        
        [JsonProperty("totalDynamicsEntities")]
        public int TotalDynamicsEntities { get; set; }
        
        [JsonProperty("mappableEntities")]
        public int MappableEntities { get; set; }
        
        [JsonProperty("unmappableEntities")]
        public int UnmappableEntities { get; set; }
        
        [JsonProperty("partialMappingEntities")]
        public int PartialMappingEntities { get; set; }
        
        [JsonProperty("migrationComplexity")]
        public string MigrationComplexity { get; set; } = "Medium"; // Low, Medium, High, Critical
        
        [JsonProperty("estimatedMigrationTime")]
        public TimeSpan EstimatedMigrationTime { get; set; }
        
        [JsonProperty("confidenceScore")]
        public decimal ConfidenceScore { get; set; } // 0-100
        
        [JsonProperty("entityTypeCounts")]
        public Dictionary<string, EntityTypeCount> EntityTypeCounts { get; set; } = new();
    }
    
    /// <summary>
    /// Contagem por tipo de entidade
    /// </summary>
    public class EntityTypeCount
    {
        [JsonProperty("genesysCount")]
        public int GenesysCount { get; set; }
        
        [JsonProperty("dynamicsCount")]
        public int DynamicsCount { get; set; }
        
        [JsonProperty("mappedCount")]
        public int MappedCount { get; set; }
        
        [JsonProperty("unmappedCount")]
        public int UnmappedCount { get; set; }
    }
    
    /// <summary>
    /// Mapeamento detalhado por tipo de entidade
    /// </summary>
    public class EntityTypeMapping
    {
        [JsonProperty("entityType")]
        public string EntityType { get; set; } = string.Empty; // Users, Queues, Flows, Bots, Skills, etc.
        
        [JsonProperty("genesysEntityType")]
        public string GenesysEntityType { get; set; } = string.Empty;
        
        [JsonProperty("dynamicsEntityType")]
        public string DynamicsEntityType { get; set; } = string.Empty;
        
        [JsonProperty("mappingStatus")]
        public string MappingStatus { get; set; } = string.Empty; // Direct, Partial, Complex, Impossible
        
        [JsonProperty("entities")]
        public List<EntityMapping> Entities { get; set; } = new();
        
        [JsonProperty("fieldMappings")]
        public List<FieldMapping> FieldMappings { get; set; } = new();
        
        [JsonProperty("migrationNotes")]
        public string? MigrationNotes { get; set; }
        
        [JsonProperty("complexityScore")]
        public int ComplexityScore { get; set; } // 1-10
        
        [JsonProperty("migrationPriority")]
        public string MigrationPriority { get; set; } = "Medium"; // Low, Medium, High, Critical
    }
    
    /// <summary>
    /// Mapeamento individual de entidade
    /// </summary>
    public class EntityMapping
    {
        [JsonProperty("genesysId")]
        public string GenesysId { get; set; } = string.Empty;
        
        [JsonProperty("genesysName")]
        public string GenesysName { get; set; } = string.Empty;
        
        [JsonProperty("dynamicsId")]
        public string? DynamicsId { get; set; }
        
        [JsonProperty("dynamicsName")]
        public string? DynamicsName { get; set; }
        
        [JsonProperty("mappingType")]
        public string MappingType { get; set; } = string.Empty; // OneToOne, OneToMany, ManyToOne, Custom
        
        [JsonProperty("mappingConfidence")]
        public decimal MappingConfidence { get; set; } // 0-100
        
        [JsonProperty("genesysData")]
        public object? GenesysData { get; set; }
        
        [JsonProperty("suggestedDynamicsData")]
        public object? SuggestedDynamicsData { get; set; }
        
        [JsonProperty("migrationActions")]
        public List<MigrationAction> MigrationActions { get; set; } = new();
        
        [JsonProperty("dependencies")]
        public List<string> Dependencies { get; set; } = new();
        
        [JsonProperty("issues")]
        public List<MappingIssue> Issues { get; set; } = new();
        
        [JsonProperty("lastAnalyzed")]
        public DateTime LastAnalyzed { get; set; } = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Mapeamento de campos entre entidades
    /// </summary>
    public class FieldMapping
    {
        [JsonProperty("genesysField")]
        public string GenesysField { get; set; } = string.Empty;
        
        [JsonProperty("dynamicsField")]
        public string? DynamicsField { get; set; }
        
        [JsonProperty("mappingType")]
        public string MappingType { get; set; } = string.Empty; // Direct, Transform, Calculated, Manual
        
        [JsonProperty("dataType")]
        public string DataType { get; set; } = string.Empty;
        
        [JsonProperty("isRequired")]
        public bool IsRequired { get; set; }
        
        [JsonProperty("transformationRule")]
        public string? TransformationRule { get; set; }
        
        [JsonProperty("defaultValue")]
        public object? DefaultValue { get; set; }
        
        [JsonProperty("validationRules")]
        public List<string> ValidationRules { get; set; } = new();
        
        [JsonProperty("notes")]
        public string? Notes { get; set; }
    }
    
    /// <summary>
    /// Ação de migração específica
    /// </summary>
    public class MigrationAction
    {
        [JsonProperty("actionType")]
        public string ActionType { get; set; } = string.Empty; // Create, Update, Transform, Validate, Custom
        
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonProperty("priority")]
        public int Priority { get; set; } = 1;
        
        [JsonProperty("estimatedDuration")]
        public TimeSpan EstimatedDuration { get; set; }
        
        [JsonProperty("prerequisites")]
        public List<string> Prerequisites { get; set; } = new();
        
        [JsonProperty("automatable")]
        public bool Automatable { get; set; } = true;
        
        [JsonProperty("riskLevel")]
        public string RiskLevel { get; set; } = "Low"; // Low, Medium, High, Critical
    }
    
    /// <summary>
    /// Problema identificado no mapeamento
    /// </summary>
    public class MappingIssue
    {
        [JsonProperty("issueType")]
        public string IssueType { get; set; } = string.Empty; // DataType, Missing, Conflict, Validation
        
        [JsonProperty("severity")]
        public string Severity { get; set; } = "Medium"; // Low, Medium, High, Critical
        
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonProperty("impact")]
        public string Impact { get; set; } = string.Empty;
        
        [JsonProperty("suggestedResolution")]
        public string? SuggestedResolution { get; set; }
        
        [JsonProperty("affectedFields")]
        public List<string> AffectedFields { get; set; } = new();
        
        [JsonProperty("workaround")]
        public string? Workaround { get; set; }
    }
    
    /// <summary>
    /// Recomendação de migração
    /// </summary>
    public class MigrationRecommendation
    {
        [JsonProperty("category")]
        public string Category { get; set; } = string.Empty; // Strategy, Technical, Process, Risk
        
        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;
        
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonProperty("priority")]
        public string Priority { get; set; } = "Medium"; // Low, Medium, High, Critical
        
        [JsonProperty("impact")]
        public string Impact { get; set; } = string.Empty;
        
        [JsonProperty("effort")]
        public string Effort { get; set; } = string.Empty; // Low, Medium, High
        
        [JsonProperty("timeline")]
        public string Timeline { get; set; } = string.Empty;
        
        [JsonProperty("dependencies")]
        public List<string> Dependencies { get; set; } = new();
        
        [JsonProperty("alternatives")]
        public List<string> Alternatives { get; set; } = new();
    }
    
    /// <summary>
    /// Avaliação de riscos da migração
    /// </summary>
    public class RiskAssessment
    {
        [JsonProperty("overallRiskLevel")]
        public string OverallRiskLevel { get; set; } = "Medium"; // Low, Medium, High, Critical
        
        [JsonProperty("riskFactors")]
        public List<RiskFactor> RiskFactors { get; set; } = new();
        
        [JsonProperty("mitigationStrategies")]
        public List<MitigationStrategy> MitigationStrategies { get; set; } = new();
        
        [JsonProperty("contingencyPlans")]
        public List<ContingencyPlan> ContingencyPlans { get; set; } = new();
        
        [JsonProperty("riskScore")]
        public decimal RiskScore { get; set; } // 0-100
    }
    
    /// <summary>
    /// Fator de risco identificado
    /// </summary>
    public class RiskFactor
    {
        [JsonProperty("category")]
        public string Category { get; set; } = string.Empty; // Technical, Data, Process, Business
        
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonProperty("probability")]
        public string Probability { get; set; } = "Medium"; // Low, Medium, High
        
        [JsonProperty("impact")]
        public string Impact { get; set; } = "Medium"; // Low, Medium, High
        
        [JsonProperty("riskScore")]
        public decimal RiskScore { get; set; } // 0-100
        
        [JsonProperty("affectedEntities")]
        public List<string> AffectedEntities { get; set; } = new();
    }
    
    /// <summary>
    /// Estratégia de mitigação de risco
    /// </summary>
    public class MitigationStrategy
    {
        [JsonProperty("riskCategory")]
        public string RiskCategory { get; set; } = string.Empty;
        
        [JsonProperty("strategy")]
        public string Strategy { get; set; } = string.Empty;
        
        [JsonProperty("implementation")]
        public string Implementation { get; set; } = string.Empty;
        
        [JsonProperty("effectiveness")]
        public string Effectiveness { get; set; } = "Medium"; // Low, Medium, High
        
        [JsonProperty("cost")]
        public string Cost { get; set; } = "Medium"; // Low, Medium, High
        
        [JsonProperty("timeline")]
        public string Timeline { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Plano de contingência
    /// </summary>
    public class ContingencyPlan
    {
        [JsonProperty("scenario")]
        public string Scenario { get; set; } = string.Empty;
        
        [JsonProperty("trigger")]
        public string Trigger { get; set; } = string.Empty;
        
        [JsonProperty("actions")]
        public List<string> Actions { get; set; } = new();
        
        [JsonProperty("rollbackPlan")]
        public string? RollbackPlan { get; set; }
        
        [JsonProperty("communicationPlan")]
        public string? CommunicationPlan { get; set; }
    }
    
    /// <summary>
    /// Análise de qualidade dos dados
    /// </summary>
    public class DataQualityAnalysis
    {
        [JsonProperty("overallQualityScore")]
        public decimal OverallQualityScore { get; set; } // 0-100
        
        [JsonProperty("completenessScore")]
        public decimal CompletenessScore { get; set; } // 0-100
        
        [JsonProperty("consistencyScore")]
        public decimal ConsistencyScore { get; set; } // 0-100
        
        [JsonProperty("accuracyScore")]
        public decimal AccuracyScore { get; set; } // 0-100
        
        [JsonProperty("dataIssues")]
        public List<DataQualityIssue> DataIssues { get; set; } = new();
        
        [JsonProperty("cleanupRecommendations")]
        public List<string> CleanupRecommendations { get; set; } = new();
        
        [JsonProperty("dataVolume")]
        public DataVolumeAnalysis DataVolume { get; set; } = new();
    }
    
    /// <summary>
    /// Problema de qualidade de dados
    /// </summary>
    public class DataQualityIssue
    {
        [JsonProperty("entityType")]
        public string EntityType { get; set; } = string.Empty;
        
        [JsonProperty("field")]
        public string Field { get; set; } = string.Empty;
        
        [JsonProperty("issueType")]
        public string IssueType { get; set; } = string.Empty; // Missing, Invalid, Duplicate, Inconsistent
        
        [JsonProperty("affectedRecords")]
        public int AffectedRecords { get; set; }
        
        [JsonProperty("percentage")]
        public decimal Percentage { get; set; }
        
        [JsonProperty("examples")]
        public List<string> Examples { get; set; } = new();
        
        [JsonProperty("suggestedFix")]
        public string? SuggestedFix { get; set; }
    }
    
    /// <summary>
    /// Análise de volume de dados
    /// </summary>
    public class DataVolumeAnalysis
    {
        [JsonProperty("totalRecords")]
        public long TotalRecords { get; set; }
        
        [JsonProperty("recordsByType")]
        public Dictionary<string, long> RecordsByType { get; set; } = new();
        
        [JsonProperty("estimatedMigrationTime")]
        public TimeSpan EstimatedMigrationTime { get; set; }
        
        [JsonProperty("recommendedBatchSize")]
        public int RecommendedBatchSize { get; set; }
        
        [JsonProperty("storageRequirements")]
        public string StorageRequirements { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Plano de migração detalhado
    /// </summary>
    public class MigrationPlan
    {
        [JsonProperty("phases")]
        public List<MigrationPhase> Phases { get; set; } = new();
        
        [JsonProperty("totalEstimatedDuration")]
        public TimeSpan TotalEstimatedDuration { get; set; }
        
        [JsonProperty("resourceRequirements")]
        public ResourceRequirements ResourceRequirements { get; set; } = new();
        
        [JsonProperty("milestones")]
        public List<Milestone> Milestones { get; set; } = new();
        
        [JsonProperty("rollbackStrategy")]
        public string RollbackStrategy { get; set; } = string.Empty;
        
        [JsonProperty("testingStrategy")]
        public string TestingStrategy { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Fase da migração
    /// </summary>
    public class MigrationPhase
    {
        [JsonProperty("phaseNumber")]
        public int PhaseNumber { get; set; }
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonProperty("entityTypes")]
        public List<string> EntityTypes { get; set; } = new();
        
        [JsonProperty("estimatedDuration")]
        public TimeSpan EstimatedDuration { get; set; }
        
        [JsonProperty("prerequisites")]
        public List<string> Prerequisites { get; set; } = new();
        
        [JsonProperty("deliverables")]
        public List<string> Deliverables { get; set; } = new();
        
        [JsonProperty("riskLevel")]
        public string RiskLevel { get; set; } = "Medium";
    }
    
    /// <summary>
    /// Requisitos de recursos
    /// </summary>
    public class ResourceRequirements
    {
        [JsonProperty("technicalTeam")]
        public int TechnicalTeam { get; set; }
        
        [JsonProperty("businessAnalysts")]
        public int BusinessAnalysts { get; set; }
        
        [JsonProperty("projectManagers")]
        public int ProjectManagers { get; set; }
        
        [JsonProperty("testingTeam")]
        public int TestingTeam { get; set; }
        
        [JsonProperty("infrastructureRequirements")]
        public List<string> InfrastructureRequirements { get; set; } = new();
        
        [JsonProperty("toolsRequired")]
        public List<string> ToolsRequired { get; set; } = new();
    }
    
    /// <summary>
    /// Marco do projeto
    /// </summary>
    public class Milestone
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonProperty("targetDate")]
        public DateTime? TargetDate { get; set; }
        
        [JsonProperty("dependencies")]
        public List<string> Dependencies { get; set; } = new();
        
        [JsonProperty("successCriteria")]
        public List<string> SuccessCriteria { get; set; } = new();
    }
}
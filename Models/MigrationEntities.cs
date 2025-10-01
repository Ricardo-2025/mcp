using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace GenesysMigrationMCP.Models
{
    // ===== MODELOS GENESYS =====
    
    public class GenesysUser
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("email")]
        public string Email { get; set; } = string.Empty;
        
        [JsonProperty("username")]
        public string Username { get; set; } = string.Empty;
        
        [JsonProperty("department")]
        public string? Department { get; set; }
        
        [JsonProperty("title")]
        public string? Title { get; set; }
        
        [JsonProperty("state")]
        public string State { get; set; } = "active";
        
        [JsonProperty("roles")]
        public List<GenesysRole> Roles { get; set; } = new();
        
        [JsonProperty("skills")]
        public List<GenesysSkill> Skills { get; set; } = new();
        
        [JsonProperty("queues")]
        public List<string> QueueIds { get; set; } = new();
        
        [JsonProperty("dateCreated")]
        public DateTime DateCreated { get; set; }
        
        [JsonProperty("dateModified")]
        public DateTime? DateModified { get; set; }
        
        [JsonProperty("isSimulated")]
        public bool IsSimulated { get; set; } = false;
        
        [JsonProperty("dataSource")]
        public string? DataSource { get; set; }
        
        [JsonProperty("reason")]
        public string? Reason { get; set; }
        
        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }
    }
    
    public class GenesysRole
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("description")]
        public string? Description { get; set; }
    }
    
    public class GenesysSkill
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("proficiency")]
        public decimal Proficiency { get; set; }
        
        [JsonProperty("type")]
        public string Type { get; set; } = "ACD";
        
        [JsonProperty("isSimulated")]
        public bool IsSimulated { get; set; } = false;
        
        [JsonProperty("dataSource")]
        public string? DataSource { get; set; }
        
        [JsonProperty("reason")]
        public string? Reason { get; set; }
        
        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }
    }
    
    public class GenesysQueue
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("description")]
        public string? Description { get; set; }
        
        [JsonProperty("mediaSettings")]
        public Dictionary<string, object> MediaSettings { get; set; } = new();
        
        [JsonProperty("routingRules")]
        public List<GenesysRoutingRule> RoutingRules { get; set; } = new();
        
        [JsonProperty("memberCount")]
        public int MemberCount { get; set; }
        
        [JsonProperty("state")]
        public string State { get; set; } = "active";
        
        [JsonProperty("dateCreated")]
        public DateTime DateCreated { get; set; }
        
        [JsonProperty("isSimulated")]
        public bool IsSimulated { get; set; } = false;
        
        [JsonProperty("dataSource")]
        public string? DataSource { get; set; }
        
        [JsonProperty("reason")]
        public string? Reason { get; set; }
        
        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }
    }
    
    public class GenesysRoutingRule
    {
        [JsonProperty("operator")]
        public string Operator { get; set; } = string.Empty;
        
        [JsonProperty("threshold")]
        public int Threshold { get; set; }
        
        [JsonProperty("waitSeconds")]
        public int WaitSeconds { get; set; }
    }
    
    public class GenesysFlow
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("description")]
        public string? Description { get; set; }
        
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty; // inbound, outbound, bot, etc.
        
        [JsonProperty("version")]
        public int Version { get; set; }
        
        [JsonProperty("state")]
        public string State { get; set; } = "active";
        
        [JsonProperty("published")]
        public bool Published { get; set; }
        
        [JsonProperty("definition")]
        public object? Definition { get; set; }
        
        [JsonProperty("supportedLanguages")]
        public List<string> SupportedLanguages { get; set; } = new();
        
        [JsonProperty("dateCreated")]
        public DateTime DateCreated { get; set; }
        
        [JsonProperty("isSimulated")]
        public bool IsSimulated { get; set; } = false;
        
        [JsonProperty("dataSource")]
        public string? DataSource { get; set; }
        
        [JsonProperty("reason")]
        public string? Reason { get; set; }
        
        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }
        
        [JsonProperty("dateModified")]
        public DateTime? DateModified { get; set; }
        
        [JsonProperty("createdBy")]
        public string? CreatedBy { get; set; }
    }
    
    public class GenesysBotConfiguration
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("description")]
        public string? Description { get; set; }
        
        [JsonProperty("botType")]
        public string BotType { get; set; } = string.Empty;
        
        [JsonProperty("intents")]
        public List<GenesysIntent> Intents { get; set; } = new();
        
        [JsonProperty("entities")]
        public List<GenesysEntity> Entities { get; set; } = new();
        
        [JsonProperty("languages")]
        public List<string> Languages { get; set; } = new();
        
        [JsonProperty("dateCreated")]
        public DateTime DateCreated { get; set; }
        
        [JsonProperty("isSimulated")]
        public bool IsSimulated { get; set; } = false;
        
        [JsonProperty("dataSource")]
        public string? DataSource { get; set; }
        
        [JsonProperty("reason")]
        public string? Reason { get; set; }
        
        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }
    }
    
    public class GenesysIntent
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("utterances")]
        public List<string> Utterances { get; set; } = new();
    }
    
    public class GenesysEntity
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;
        
        [JsonProperty("values")]
        public List<string> Values { get; set; } = new();
    }
    
    // ===== MODELOS DYNAMICS CONTACT CENTER =====
    
    public class DynamicsAgent
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("email")]
        public string Email { get; set; } = string.Empty;
        
        [JsonProperty("username")]
        public string Username { get; set; } = string.Empty;
        
        [JsonProperty("capacity")]
        public DynamicsCapacity Capacity { get; set; } = new();
        
        [JsonProperty("skills")]
        public List<DynamicsSkill> Skills { get; set; } = new();
        
        [JsonProperty("workstreams")]
        public List<string> WorkstreamIds { get; set; } = new();
        
        [JsonProperty("presence")]
        public string Presence { get; set; } = "Available";
        
        [JsonProperty("status")]
        public string Status { get; set; } = "Active";
        
        [JsonProperty("dateCreated")]
        public DateTime DateCreated { get; set; }
        
        [JsonProperty("lastModified")]
        public DateTime? LastModified { get; set; }
        
        [JsonProperty("genesysUserId")]
        public string? GenesysUserId { get; set; } // Para rastreamento de migração
        
        [JsonProperty("department")]
        public string? Department { get; set; }
        
        [JsonProperty("dateModified")]
        public DateTime? DateModified { get; set; }
        
        [JsonProperty("genesysSourceId")]
        public string? GenesysSourceId { get; set; }
        
        [JsonProperty("migrationDate")]
        public DateTime? MigrationDate { get; set; }
        
        [JsonProperty("isSimulated")]
        public bool IsSimulated { get; set; } = false;
        
        [JsonProperty("dataSource")]
        public string? DataSource { get; set; }
        
        [JsonProperty("reason")]
        public string? Reason { get; set; }
        
        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }
    }
    
    public class DynamicsCapacity
    {
        [JsonProperty("voice")]
        public int Voice { get; set; } = 1;
        
        [JsonProperty("chat")]
        public int Chat { get; set; } = 3;
        
        [JsonProperty("email")]
        public int Email { get; set; } = 5;
        
        [JsonProperty("sms")]
        public int Sms { get; set; } = 2;
    }
    
    public class DynamicsSkill
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("type")]
        public string Type { get; set; } = "Exact";
        
        [JsonProperty("proficiencyModel")]
        public string ProficiencyModel { get; set; } = "Number";
        
        [JsonProperty("proficiencyValue")]
        public decimal ProficiencyValue { get; set; }
        
        [JsonProperty("isSimulated")]
        public bool IsSimulated { get; set; } = false;
        
        [JsonProperty("dataSource")]
        public string? DataSource { get; set; }
        
        [JsonProperty("reason")]
        public string? Reason { get; set; }
        
        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }
    }
    
    public class DynamicsCharacteristic
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("description")]
        public string? Description { get; set; }
        
        [JsonProperty("characteristictype")]
        public int CharacteristicType { get; set; } = 1; // 1 = Skill, 2 = Certification
        
        [JsonProperty("genesysSkillId")]
        public string? GenesysSkillId { get; set; } // Para rastreamento de migração
        
        [JsonProperty("isSimulated")]
        public bool IsSimulated { get; set; } = false;
        
        [JsonProperty("dataSource")]
        public string? DataSource { get; set; }
        
        [JsonProperty("reason")]
        public string? Reason { get; set; }
        
        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }
    }
    
    public class DynamicsWorkstream
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("description")]
        public string? Description { get; set; }
        
        [JsonProperty("channelType")]
        public string ChannelType { get; set; } = string.Empty; // voice, chat, email, sms
        
        [JsonProperty("routingRules")]
        public List<DynamicsRoutingRule> RoutingRules { get; set; } = new();
        
        [JsonProperty("capacity")]
        public DynamicsWorkstreamCapacity Capacity { get; set; } = new();
        
        [JsonProperty("operatingHours")]
        public DynamicsOperatingHours? OperatingHours { get; set; }
        
        [JsonProperty("status")]
        public string Status { get; set; } = "Active";
        
        [JsonProperty("dateCreated")]
        public DateTime DateCreated { get; set; }
        
        [JsonProperty("isSimulated")]
        public bool IsSimulated { get; set; } = false;
        
        [JsonProperty("dataSource")]
        public string? DataSource { get; set; }
        
        [JsonProperty("reason")]
        public string? Reason { get; set; }
        
        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }
        
        [JsonProperty("genesysQueueId")]
        public string? GenesysQueueId { get; set; } // Para rastreamento de migração
        
        [JsonProperty("agentCount")]
        public int AgentCount { get; set; }
        
        [JsonProperty("routingMethod")]
        public string RoutingMethod { get; set; } = string.Empty;
        
        [JsonProperty("maxConcurrentSessions")]
        public int MaxConcurrentSessions { get; set; }
    }
    
    public class DynamicsRoutingRule
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("condition")]
        public string Condition { get; set; } = string.Empty;
        
        [JsonProperty("action")]
        public string Action { get; set; } = string.Empty;
        
        [JsonProperty("priority")]
        public int Priority { get; set; }
    }
    
    public class DynamicsWorkstreamCapacity
    {
        [JsonProperty("maxConcurrentSessions")]
        public int MaxConcurrentSessions { get; set; } = 1;
        
        [JsonProperty("assignWorkItemAfterDecline")]
        public bool AssignWorkItemAfterDecline { get; set; } = true;
        
        [JsonProperty("autoAcceptEnabled")]
        public bool AutoAcceptEnabled { get; set; } = false;
    }
    
    public class DynamicsOperatingHours
    {
        [JsonProperty("timeZone")]
        public string TimeZone { get; set; } = "UTC";
        
        [JsonProperty("schedule")]
        public Dictionary<string, DynamicsScheduleDay> Schedule { get; set; } = new();
    }
    
    public class DynamicsScheduleDay
    {
        [JsonProperty("isEnabled")]
        public bool IsEnabled { get; set; }
        
        [JsonProperty("startTime")]
        public string StartTime { get; set; } = "09:00";
        
        [JsonProperty("endTime")]
        public string EndTime { get; set; } = "17:00";
    }
    
    public class DynamicsChannel
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty; // voice, chat, email, sms
        
        [JsonProperty("configuration")]
        public Dictionary<string, object> Configuration { get; set; } = new();
        
        [JsonProperty("workstreamId")]
        public string WorkstreamId { get; set; } = string.Empty;
        
        [JsonProperty("status")]
        public string Status { get; set; } = "Active";
        
        [JsonProperty("dateCreated")]
        public DateTime DateCreated { get; set; }
    }
    
    public class DynamicsBotConfiguration
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonProperty("description")]
        public string? Description { get; set; }
        
        [JsonProperty("botFrameworkId")]
        public string BotFrameworkId { get; set; } = string.Empty;
        
        [JsonProperty("language")]
        public string Language { get; set; } = "en-US";
        
        [JsonProperty("workstreamId")]
        public string WorkstreamId { get; set; } = string.Empty;
        
        [JsonProperty("escalationRules")]
        public List<DynamicsEscalationRule> EscalationRules { get; set; } = new();
        
        [JsonProperty("status")]
        public string Status { get; set; } = "Active";
        
        [JsonProperty("dateCreated")]
        public DateTime DateCreated { get; set; }
        
        [JsonProperty("isSimulated")]
        public bool IsSimulated { get; set; } = false;
        
        [JsonProperty("dataSource")]
        public string? DataSource { get; set; }
        
        [JsonProperty("reason")]
        public string? Reason { get; set; }
        
        [JsonProperty("errorMessage")]
        public string? ErrorMessage { get; set; }
        
        [JsonProperty("genesysBotId")]
        public string? GenesysBotId { get; set; } // Para rastreamento de migração
        
        [JsonProperty("botType")]
        public string BotType { get; set; } = string.Empty;
        
        [JsonProperty("languages")]
        public List<string> Languages { get; set; } = new();
        
        [JsonProperty("topicCount")]
        public int TopicCount { get; set; }
    }
    
    public class DynamicsEscalationRule
    {
        [JsonProperty("trigger")]
        public string Trigger { get; set; } = string.Empty;
        
        [JsonProperty("action")]
        public string Action { get; set; } = string.Empty;
        
        [JsonProperty("targetWorkstream")]
        public string? TargetWorkstream { get; set; }
    }
    
    // ===== MODELOS DE COMPARAÇÃO E MIGRAÇÃO =====
    
    public class MigrationMapping
    {
        [JsonProperty("genesysId")]
        public string GenesysId { get; set; } = string.Empty;
        
        [JsonProperty("dynamicsId")]
        public string DynamicsId { get; set; } = string.Empty;
        
        [JsonProperty("entityType")]
        public string EntityType { get; set; } = string.Empty; // User, Queue, Flow, Bot
        
        [JsonProperty("migrationStatus")]
        public string MigrationStatus { get; set; } = "Pending"; // Pending, InProgress, Completed, Failed
        
        [JsonProperty("migrationDate")]
        public DateTime? MigrationDate { get; set; }
        
        [JsonProperty("notes")]
        public string? Notes { get; set; }
    }
    
    public class EntityComparison
    {
        [JsonProperty("entityType")]
        public string EntityType { get; set; } = string.Empty;
        
        [JsonProperty("genesysEntity")]
        public object? GenesysEntity { get; set; }
        
        [JsonProperty("dynamicsEntity")]
        public object? DynamicsEntity { get; set; }
        
        [JsonProperty("differences")]
        public List<PropertyDifference> Differences { get; set; } = new();
        
        [JsonProperty("migrationRecommendation")]
        public string? MigrationRecommendation { get; set; }
    }
    
    public class PropertyDifference
    {
        [JsonProperty("property")]
        public string Property { get; set; } = string.Empty;
        
        [JsonProperty("genesysValue")]
        public object? GenesysValue { get; set; }
        
        [JsonProperty("dynamicsValue")]
        public object? DynamicsValue { get; set; }
        
        [JsonProperty("severity")]
        public string Severity { get; set; } = "Info"; // Info, Warning, Critical
    }
}
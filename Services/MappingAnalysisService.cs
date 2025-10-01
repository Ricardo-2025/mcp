using GenesysMigrationMCP.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Text;
using System.Text.Json;

namespace GenesysMigrationMCP.Services
{
    /// <summary>
    /// Serviço especializado em análise de mapeamento entre Genesys e Dynamics
    /// </summary>
    public class MappingAnalysisService : IMappingAnalysisService
    {
        private readonly GenesysCloudClient _genesysClient;
        private readonly DynamicsClient _dynamicsClient;
        private readonly ILogger<MappingAnalysisService> _logger;
        private readonly IConfiguration _configuration;
        
        // Cache para otimizar performance
        private readonly Dictionary<string, object> _dataCache = new();
        private DateTime _lastCacheUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheValidityPeriod = TimeSpan.FromHours(1);

        public MappingAnalysisService(
            GenesysCloudClient genesysClient,
            DynamicsClient dynamicsClient,
            ILogger<MappingAnalysisService> logger,
            IConfiguration configuration)
        {
            _genesysClient = genesysClient;
            _dynamicsClient = dynamicsClient;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<MappingAnalysisReport> GenerateCompleteMappingReportAsync(
            bool includeDetailedAnalysis = true, 
            List<string>? entityTypes = null)
        {
            _logger.LogInformation("Iniciando geração de relatório completo de mapeamento");

            try
            {
                var report = new MappingAnalysisReport
                {
                    ReportId = Guid.NewGuid().ToString(),
                    GeneratedDate = DateTime.UtcNow,
                    GenesysEnvironment = _configuration["GenesysCloud:ApiUrl"] ?? "Unknown",
                    DynamicsEnvironment = _configuration["Dynamics:ApiUrl"] ?? "Unknown"
                };

                // Definir tipos de entidade para análise
                var typesToAnalyze = entityTypes ?? new List<string> 
                { 
                    "Users", "Queues", "Flows", "Bots", "Skills", "Roles", "Workstreams", "Channels" 
                };

                // Gerar resumo
                report.Summary = await GetMappingSummaryAsync();

                // Analisar cada tipo de entidade
                foreach (var entityType in typesToAnalyze)
                {
                    try
                    {
                        var entityMapping = await AnalyzeEntityTypeMappingAsync(entityType);
                        report.EntityMappings.Add(entityMapping);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Erro ao analisar mapeamento para tipo {entityType}");
                    }
                }

                // Gerar análises complementares
                report.RiskAssessment = await AssessMigrationRisksAsync(typesToAnalyze);
                report.DataQualityAnalysis = await AnalyzeDataQualityAsync(typesToAnalyze);
                report.MigrationRecommendations = await GetMigrationRecommendationsAsync();
                report.MigrationPlan = await GenerateMigrationPlanAsync("Phased", typesToAnalyze);

                _logger.LogInformation($"Relatório de mapeamento gerado com sucesso. ID: {report.ReportId}");
                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao gerar relatório completo de mapeamento");
                throw;
            }
        }

        public async Task<EntityTypeMapping> AnalyzeEntityTypeMappingAsync(string entityType)
        {
            _logger.LogInformation($"Analisando mapeamento para tipo de entidade: {entityType}");

            var mapping = new EntityTypeMapping
            {
                EntityType = entityType,
                GenesysEntityType = GetGenesysEntityType(entityType),
                DynamicsEntityType = GetDynamicsEntityType(entityType)
            };

            try
            {
                switch (entityType.ToLower())
                {
                    case "all":
                        // Para "all", retorna um resumo geral de todos os tipos
                        await AnalyzeAllEntityTypesAsync(mapping);
                        break;
                    case "users":
                        await AnalyzeUserMappingAsync(mapping);
                        break;
                    case "queues":
                        await AnalyzeQueueMappingAsync(mapping);
                        break;
                    case "flows":
                        await AnalyzeFlowMappingAsync(mapping);
                        break;
                    case "bots":
                        await AnalyzeBotMappingAsync(mapping);
                        break;
                    case "skills":
                        await AnalyzeSkillMappingAsync(mapping);
                        break;
                    case "roles":
                        await AnalyzeRoleMappingAsync(mapping);
                        break;
                    case "workstreams":
                        await AnalyzeWorkstreamMappingAsync(mapping);
                        break;
                    case "channels":
                        await AnalyzeChannelMappingAsync(mapping);
                        break;
                    default:
                        _logger.LogWarning($"Tipo de entidade não suportado: {entityType}");
                        mapping.MappingStatus = "Unsupported";
                        break;
                }

                // Calcular complexidade e prioridade
                mapping.ComplexityScore = CalculateComplexityScore(mapping);
                mapping.MigrationPriority = DetermineMigrationPriority(mapping);

                return mapping;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao analisar mapeamento para {entityType}");
                mapping.MappingStatus = "Error";
                mapping.MigrationNotes = $"Erro na análise: {ex.Message}";
                return mapping;
            }
        }

        private async Task AnalyzeUserMappingAsync(EntityTypeMapping mapping)
        {
            // Obter usuários do Genesys
            var genesysUsers = await GetGenesysUsersAsync();
            var dynamicsAgents = await GetDynamicsAgentsAsync();

            mapping.FieldMappings = new List<FieldMapping>
            {
                new() { GenesysField = "id", DynamicsField = "msdyn_systemuserid", MappingType = "Direct", DataType = "string", IsRequired = true },
                new() { GenesysField = "name", DynamicsField = "fullname", MappingType = "Direct", DataType = "string", IsRequired = true },
                new() { GenesysField = "email", DynamicsField = "internalemailaddress", MappingType = "Direct", DataType = "string", IsRequired = true },
                new() { GenesysField = "username", DynamicsField = "domainname", MappingType = "Transform", DataType = "string", IsRequired = true, TransformationRule = "Remove domain suffix" },
                new() { GenesysField = "department", DynamicsField = "msdyn_departmentname", MappingType = "Direct", DataType = "string", IsRequired = false },
                new() { GenesysField = "title", DynamicsField = "title", MappingType = "Direct", DataType = "string", IsRequired = false },
                new() { GenesysField = "state", DynamicsField = "isdisabled", MappingType = "Transform", DataType = "boolean", IsRequired = true, TransformationRule = "active -> false, inactive -> true" },
                new() { GenesysField = "roles", DynamicsField = "systemuserroles_association", MappingType = "Complex", DataType = "array", IsRequired = false, Notes = "Requires role mapping analysis" },
                new() { GenesysField = "skills", DynamicsField = "msdyn_agentskills", MappingType = "Complex", DataType = "array", IsRequired = false, Notes = "Requires skill mapping and proficiency conversion" },
                new() { GenesysField = "queues", DynamicsField = "msdyn_agentworkstreams", MappingType = "Complex", DataType = "array", IsRequired = false, Notes = "Queues map to workstreams" }
            };

            // Analisar cada usuário
            foreach (var genesysUser in genesysUsers.Take(100)) // Limitar para performance
            {
                var entityMapping = new EntityMapping
                {
                    GenesysId = genesysUser.Id,
                    GenesysName = genesysUser.Name,
                    GenesysData = genesysUser,
                    MappingType = "OneToOne",
                    LastAnalyzed = DateTime.UtcNow
                };

                // Tentar encontrar agente correspondente no Dynamics
                var matchingAgent = dynamicsAgents.FirstOrDefault(a => 
                    a.Email.Equals(genesysUser.Email, StringComparison.OrdinalIgnoreCase) ||
                    a.Username.Equals(genesysUser.Username, StringComparison.OrdinalIgnoreCase));

                if (matchingAgent != null)
                {
                    entityMapping.DynamicsId = matchingAgent.Id;
                    entityMapping.DynamicsName = matchingAgent.Name;
                    entityMapping.MappingConfidence = 95;
                }
                else
                {
                    // Sugerir criação de novo agente
                    entityMapping.SuggestedDynamicsData = CreateSuggestedDynamicsAgent(genesysUser);
                    entityMapping.MappingConfidence = 85;
                }

                // Analisar problemas potenciais
                AnalyzeUserMappingIssues(genesysUser, entityMapping);

                // Definir ações de migração
                entityMapping.MigrationActions = GenerateUserMigrationActions(genesysUser, matchingAgent);

                mapping.Entities.Add(entityMapping);
            }

            mapping.MappingStatus = DetermineMappingStatus(mapping.Entities);
        }

        private async Task AnalyzeQueueMappingAsync(EntityTypeMapping mapping)
        {
            var genesysQueues = await GetGenesysQueuesAsync();
            var dynamicsWorkstreams = await GetDynamicsWorkstreamsAsync();

            mapping.FieldMappings = new List<FieldMapping>
            {
                new() { GenesysField = "id", DynamicsField = "msdyn_liveworkstreamid", MappingType = "Direct", DataType = "string", IsRequired = true },
                new() { GenesysField = "name", DynamicsField = "msdyn_name", MappingType = "Direct", DataType = "string", IsRequired = true },
                new() { GenesysField = "description", DynamicsField = "msdyn_description", MappingType = "Direct", DataType = "string", IsRequired = false },
                new() { GenesysField = "mediaSettings", DynamicsField = "msdyn_channeltype", MappingType = "Transform", DataType = "object", IsRequired = true, TransformationRule = "Extract primary media type" },
                new() { GenesysField = "routingRules", DynamicsField = "msdyn_routingrules", MappingType = "Complex", DataType = "array", IsRequired = false, Notes = "Requires routing rule conversion" },
                new() { GenesysField = "memberCount", DynamicsField = "msdyn_agentcount", MappingType = "Calculated", DataType = "int", IsRequired = false, Notes = "Calculate from agent assignments" },
                new() { GenesysField = "state", DynamicsField = "statecode", MappingType = "Transform", DataType = "int", IsRequired = true, TransformationRule = "active -> 0, inactive -> 1" }
            };

            foreach (var genesysQueue in genesysQueues.Take(50))
            {
                var entityMapping = new EntityMapping
                {
                    GenesysId = genesysQueue.Id,
                    GenesysName = genesysQueue.Name,
                    GenesysData = genesysQueue,
                    MappingType = "OneToOne",
                    LastAnalyzed = DateTime.UtcNow
                };

                var matchingWorkstream = dynamicsWorkstreams.FirstOrDefault(w => 
                    w.Name.Equals(genesysQueue.Name, StringComparison.OrdinalIgnoreCase));

                if (matchingWorkstream != null)
                {
                    entityMapping.DynamicsId = matchingWorkstream.Id;
                    entityMapping.DynamicsName = matchingWorkstream.Name;
                    entityMapping.MappingConfidence = 90;
                }
                else
                {
                    entityMapping.SuggestedDynamicsData = CreateSuggestedDynamicsWorkstream(genesysQueue);
                    entityMapping.MappingConfidence = 80;
                }

                AnalyzeQueueMappingIssues(genesysQueue, entityMapping);
                entityMapping.MigrationActions = GenerateQueueMigrationActions(genesysQueue, matchingWorkstream);

                mapping.Entities.Add(entityMapping);
            }

            mapping.MappingStatus = DetermineMappingStatus(mapping.Entities);
        }

        private async Task AnalyzeFlowMappingAsync(EntityTypeMapping mapping)
        {
            var genesysFlows = await GetGenesysFlowsAsync();

            mapping.FieldMappings = new List<FieldMapping>
            {
                new() { GenesysField = "id", DynamicsField = "N/A", MappingType = "Manual", DataType = "string", IsRequired = false, Notes = "Flows require manual recreation in Power Automate" },
                new() { GenesysField = "name", DynamicsField = "displayname", MappingType = "Direct", DataType = "string", IsRequired = true },
                new() { GenesysField = "description", DynamicsField = "description", MappingType = "Direct", DataType = "string", IsRequired = false },
                new() { GenesysField = "type", DynamicsField = "category", MappingType = "Transform", DataType = "string", IsRequired = true, TransformationRule = "Map flow types to Power Automate categories" },
                new() { GenesysField = "definition", DynamicsField = "definition", MappingType = "Complex", DataType = "object", IsRequired = true, Notes = "Requires complete flow logic recreation" }
            };

            foreach (var genesysFlow in genesysFlows.Take(25))
            {
                var entityMapping = new EntityMapping
                {
                    GenesysId = genesysFlow.Id,
                    GenesysName = genesysFlow.Name,
                    GenesysData = genesysFlow,
                    MappingType = "Manual",
                    MappingConfidence = 30, // Baixa confiança devido à complexidade
                    LastAnalyzed = DateTime.UtcNow
                };

                // Flows requerem recriação manual
                entityMapping.Issues.Add(new MappingIssue
                {
                    IssueType = "Complexity",
                    Severity = "High",
                    Description = "Genesys flows cannot be directly migrated to Dynamics. Requires manual recreation in Power Automate.",
                    Impact = "High development effort required",
                    SuggestedResolution = "Analyze flow logic and recreate using Power Automate or Omnichannel routing rules"
                });

                entityMapping.MigrationActions = GenerateFlowMigrationActions(genesysFlow);

                mapping.Entities.Add(entityMapping);
            }

            mapping.MappingStatus = "Complex";
            mapping.ComplexityScore = 9;
            mapping.MigrationPriority = "High";
            mapping.MigrationNotes = "Flows require complete redesign and manual recreation in Power Automate or Omnichannel routing rules.";
        }

        private async Task AnalyzeBotMappingAsync(EntityTypeMapping mapping)
        {
            var genesysBots = await GetGenesysBotsAsync();
            var dynamicsBots = await GetDynamicsBotsAsync();

            mapping.FieldMappings = new List<FieldMapping>
            {
                new() { GenesysField = "id", DynamicsField = "msdyn_botid", MappingType = "Direct", DataType = "string", IsRequired = true },
                new() { GenesysField = "name", DynamicsField = "msdyn_name", MappingType = "Direct", DataType = "string", IsRequired = true },
                new() { GenesysField = "description", DynamicsField = "msdyn_description", MappingType = "Direct", DataType = "string", IsRequired = false },
                new() { GenesysField = "botType", DynamicsField = "msdyn_botframeworkid", MappingType = "Transform", DataType = "string", IsRequired = true, TransformationRule = "Map to Bot Framework bot ID" },
                new() { GenesysField = "intents", DynamicsField = "N/A", MappingType = "Manual", DataType = "array", IsRequired = false, Notes = "Intents must be recreated in Bot Framework" },
                new() { GenesysField = "entities", DynamicsField = "N/A", MappingType = "Manual", DataType = "array", IsRequired = false, Notes = "Entities must be recreated in Bot Framework" },
                new() { GenesysField = "languages", DynamicsField = "msdyn_language", MappingType = "Transform", DataType = "array", IsRequired = true, TransformationRule = "Map to primary language" }
            };

            foreach (var genesysBot in genesysBots.Take(20))
            {
                var entityMapping = new EntityMapping
                {
                    GenesysId = genesysBot.Id,
                    GenesysName = genesysBot.Name,
                    GenesysData = genesysBot,
                    MappingType = "Complex",
                    MappingConfidence = 40, // Baixa devido à complexidade
                    LastAnalyzed = DateTime.UtcNow
                };

                var matchingBot = dynamicsBots.FirstOrDefault(b => 
                    b.Name.Equals(genesysBot.Name, StringComparison.OrdinalIgnoreCase));

                if (matchingBot != null)
                {
                    entityMapping.DynamicsId = matchingBot.Id;
                    entityMapping.DynamicsName = matchingBot.Name;
                    entityMapping.MappingConfidence = 60;
                }
                else
                {
                    entityMapping.SuggestedDynamicsData = CreateSuggestedDynamicsBot(genesysBot);
                }

                AnalyzeBotMappingIssues(genesysBot, entityMapping);
                entityMapping.MigrationActions = GenerateBotMigrationActions(genesysBot, matchingBot);

                mapping.Entities.Add(entityMapping);
            }

            mapping.MappingStatus = "Complex";
            mapping.ComplexityScore = 8;
            mapping.MigrationPriority = "Medium";
            mapping.MigrationNotes = "Bots require significant rework to integrate with Bot Framework and Omnichannel.";
        }

        private async Task AnalyzeSkillMappingAsync(EntityTypeMapping mapping)
        {
            var genesysSkills = await GetGenesysSkillsAsync();
            var dynamicsCharacteristics = await GetDynamicsCharacteristicsAsync();

            mapping.FieldMappings = new List<FieldMapping>
            {
                new() { GenesysField = "id", DynamicsField = "msdyn_characteristicid", MappingType = "Direct", DataType = "string", IsRequired = true },
                new() { GenesysField = "name", DynamicsField = "msdyn_name", MappingType = "Direct", DataType = "string", IsRequired = true },
                new() { GenesysField = "type", DynamicsField = "msdyn_characteristictype", MappingType = "Transform", DataType = "int", IsRequired = true, TransformationRule = "ACD -> 1 (Skill), Language -> 1 (Skill)" },
                new() { GenesysField = "proficiency", DynamicsField = "msdyn_ratingvalue", MappingType = "Transform", DataType = "decimal", IsRequired = false, TransformationRule = "Scale 0-5 to 0-10 or percentage" }
            };

            // Implementar lógica similar para skills...
            mapping.MappingStatus = "Direct";
            mapping.ComplexityScore = 3;
            mapping.MigrationPriority = "Low";
        }

        private async Task AnalyzeRoleMappingAsync(EntityTypeMapping mapping)
        {
            // Implementar análise de roles
            mapping.MappingStatus = "Partial";
            mapping.ComplexityScore = 6;
            mapping.MigrationPriority = "Medium";
            mapping.MigrationNotes = "Genesys roles need to be mapped to Dynamics security roles and Omnichannel agent roles.";
        }

        private async Task AnalyzeWorkstreamMappingAsync(EntityTypeMapping mapping)
        {
            // Workstreams são conceito do Dynamics, não existe equivalente direto no Genesys
            mapping.MappingStatus = "New";
            mapping.ComplexityScore = 4;
            mapping.MigrationPriority = "High";
            mapping.MigrationNotes = "Workstreams are new concept in Dynamics. Need to be created based on Genesys queue structure.";
        }

        private async Task AnalyzeChannelMappingAsync(EntityTypeMapping mapping)
        {
            // Implementar análise de channels
            mapping.MappingStatus = "Partial";
            mapping.ComplexityScore = 5;
            mapping.MigrationPriority = "Medium";
        }

        public async Task<EntityComparison> CompareEntitiesAsync(
            string genesysEntityId, 
            string? dynamicsEntityId, 
            string entityType)
        {
            _logger.LogInformation($"Comparando entidades: Genesys {genesysEntityId} com Dynamics {dynamicsEntityId} (Tipo: {entityType})");

            var comparison = new EntityComparison
            {
                EntityType = entityType
            };

            try
            {
                // Obter dados das entidades
                comparison.GenesysEntity = await GetGenesysEntityAsync(genesysEntityId, entityType);
                
                if (!string.IsNullOrEmpty(dynamicsEntityId))
                {
                    comparison.DynamicsEntity = await GetDynamicsEntityAsync(dynamicsEntityId, entityType);
                }

                // Comparar propriedades
                comparison.Differences = CompareEntityProperties(comparison.GenesysEntity, comparison.DynamicsEntity, entityType);

                // Gerar recomendação
                comparison.MigrationRecommendation = GenerateMigrationRecommendation(comparison);

                return comparison;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao comparar entidades {genesysEntityId} e {dynamicsEntityId}");
                throw;
            }
        }

        public async Task<RiskAssessment> AssessMigrationRisksAsync(List<string>? entityTypes = null)
        {
            _logger.LogInformation("Avaliando riscos da migração");

            var assessment = new RiskAssessment();
            var riskFactors = new List<RiskFactor>();

            // Riscos técnicos
            riskFactors.Add(new RiskFactor
            {
                Category = "Technical",
                Description = "Incompatibilidade de funcionalidades entre Genesys e Dynamics",
                Probability = "High",
                Impact = "High",
                RiskScore = 80,
                AffectedEntities = new List<string> { "Flows", "Bots", "Advanced Routing" }
            });

            riskFactors.Add(new RiskFactor
            {
                Category = "Data",
                Description = "Perda de dados históricos durante migração",
                Probability = "Medium",
                Impact = "High",
                RiskScore = 60,
                AffectedEntities = new List<string> { "Interactions", "Analytics", "Reports" }
            });

            riskFactors.Add(new RiskFactor
            {
                Category = "Process",
                Description = "Interrupção das operações durante migração",
                Probability = "Medium",
                Impact = "High",
                RiskScore = 65,
                AffectedEntities = new List<string> { "All" }
            });

            riskFactors.Add(new RiskFactor
            {
                Category = "Business",
                Description = "Resistência dos usuários à mudança de plataforma",
                Probability = "High",
                Impact = "Medium",
                RiskScore = 70,
                AffectedEntities = new List<string> { "Users", "Agents" }
            });

            assessment.RiskFactors = riskFactors;
            assessment.RiskScore = riskFactors.Average(r => r.RiskScore);
            assessment.OverallRiskLevel = assessment.RiskScore switch
            {
                >= 80 => "Critical",
                >= 60 => "High",
                >= 40 => "Medium",
                _ => "Low"
            };

            // Estratégias de mitigação
            assessment.MitigationStrategies = GenerateMitigationStrategies(riskFactors);
            assessment.ContingencyPlans = GenerateContingencyPlans();

            return assessment;
        }

        public async Task<DataQualityAnalysis> AnalyzeDataQualityAsync(List<string>? entityTypes = null)
        {
            _logger.LogInformation("Analisando qualidade dos dados reais do Genesys Cloud");

            var analysis = new DataQualityAnalysis();
            var issues = new List<DataQualityIssue>();
            var recordsByType = new Dictionary<string, long>();
            long totalRecords = 0;

            // Definir tipos de entidade para análise se não especificados
            var typesToAnalyze = entityTypes ?? new List<string> { "Users", "Queues", "Flows", "Skills" };

            try
            {
                // Analisar cada tipo de entidade
                foreach (var entityType in typesToAnalyze)
                {
                    _logger.LogInformation($"Analisando qualidade dos dados para: {entityType}");

                    switch (entityType.ToLower())
                    {
                        case "users":
                            await AnalyzeUsersDataQualityAsync(issues, recordsByType);
                            break;
                        case "queues":
                            await AnalyzeQueuesDataQualityAsync(issues, recordsByType);
                            break;
                        case "flows":
                            await AnalyzeFlowsDataQualityAsync(issues, recordsByType);
                            break;
                        case "skills":
                            await AnalyzeSkillsDataQualityAsync(issues, recordsByType);
                            break;
                        default:
                            _logger.LogWarning($"Tipo de entidade não suportado para análise de qualidade: {entityType}");
                            break;
                    }
                }

                // Calcular total de registros
                totalRecords = recordsByType.Values.Sum();

                // Calcular métricas de qualidade baseadas nos problemas encontrados
                var qualityMetrics = CalculateQualityMetrics(issues, totalRecords);

                analysis.DataIssues = issues;
                analysis.OverallQualityScore = qualityMetrics.OverallScore;
                analysis.CompletenessScore = qualityMetrics.CompletenessScore;
                analysis.ConsistencyScore = qualityMetrics.ConsistencyScore;
                analysis.AccuracyScore = qualityMetrics.AccuracyScore;

                // Gerar recomendações baseadas nos problemas encontrados
                analysis.CleanupRecommendations = GenerateCleanupRecommendations(issues);

                // Análise de volume
                analysis.DataVolume = new DataVolumeAnalysis
                {
                    TotalRecords = totalRecords,
                    RecordsByType = recordsByType,
                    EstimatedMigrationTime = CalculateEstimatedMigrationTime(totalRecords),
                    RecommendedBatchSize = CalculateRecommendedBatchSize(totalRecords),
                    StorageRequirements = CalculateStorageRequirements(totalRecords)
                };

                _logger.LogInformation($"Análise de qualidade concluída. Score geral: {analysis.OverallQualityScore:F1}%, Total de registros: {totalRecords}, Problemas encontrados: {issues.Count}");

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao analisar qualidade dos dados do Genesys");
                
                // Retornar análise com erro
                analysis.DataIssues = new List<DataQualityIssue>
                {
                    new DataQualityIssue
                    {
                        EntityType = "System",
                        Field = "Connection",
                        IssueType = "Error",
                        AffectedRecords = 0,
                        Percentage = 0,
                        SuggestedFix = $"Verificar conectividade com Genesys Cloud: {ex.Message}"
                    }
                };
                analysis.OverallQualityScore = 0;
                analysis.CompletenessScore = 0;
                analysis.ConsistencyScore = 0;
                analysis.AccuracyScore = 0;
                analysis.CleanupRecommendations = new List<string> { "Resolver problemas de conectividade antes de prosseguir" };
                
                return analysis;
            }
        }

        private async Task AnalyzeUsersDataQualityAsync(List<DataQualityIssue> issues, Dictionary<string, long> recordsByType)
        {
            try
            {
                var users = await GetGenesysUsersAsync();
                recordsByType["Users"] = users.Count;

                if (users.Count == 0)
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Users",
                        Field = "General",
                        IssueType = "Missing",
                        AffectedRecords = 0,
                        Percentage = 100,
                        SuggestedFix = "Verificar se existem usuários no Genesys Cloud ou se há problemas de permissão"
                    });
                    return;
                }

                // Analisar emails faltantes
                var usersWithoutEmail = users.Where(u => string.IsNullOrWhiteSpace(u.Email)).ToList();
                if (usersWithoutEmail.Any())
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Users",
                        Field = "email",
                        IssueType = "Missing",
                        AffectedRecords = usersWithoutEmail.Count,
                        Percentage = (decimal)usersWithoutEmail.Count / users.Count * 100,
                        Examples = usersWithoutEmail.Take(3).Select(u => $"ID: {u.Id}, Nome: {u.Name}").ToList(),
                        SuggestedFix = "Obter emails dos usuários através de Active Directory ou solicitar preenchimento manual"
                    });
                }

                // Analisar emails inválidos
                var usersWithInvalidEmail = users.Where(u => !string.IsNullOrWhiteSpace(u.Email) && !IsValidEmail(u.Email)).ToList();
                if (usersWithInvalidEmail.Any())
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Users",
                        Field = "email",
                        IssueType = "Invalid",
                        AffectedRecords = usersWithInvalidEmail.Count,
                        Percentage = (decimal)usersWithInvalidEmail.Count / users.Count * 100,
                        Examples = usersWithInvalidEmail.Take(3).Select(u => $"Email: {u.Email}").ToList(),
                        SuggestedFix = "Corrigir formato dos emails ou obter emails válidos"
                    });
                }

                // Analisar departamentos faltantes
                var usersWithoutDepartment = users.Where(u => string.IsNullOrWhiteSpace(u.Department)).ToList();
                if (usersWithoutDepartment.Any())
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Users",
                        Field = "department",
                        IssueType = "Missing",
                        AffectedRecords = usersWithoutDepartment.Count,
                        Percentage = (decimal)usersWithoutDepartment.Count / users.Count * 100,
                        Examples = usersWithoutDepartment.Take(3).Select(u => $"Nome: {u.Name}").ToList(),
                        SuggestedFix = "Definir departamentos baseados na estrutura organizacional"
                    });
                }

                // Analisar usuários duplicados (mesmo email)
                var duplicateEmails = users.Where(u => !string.IsNullOrWhiteSpace(u.Email))
                    .GroupBy(u => u.Email.ToLower())
                    .Where(g => g.Count() > 1)
                    .ToList();

                if (duplicateEmails.Any())
                {
                    var totalDuplicates = duplicateEmails.Sum(g => g.Count() - 1);
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Users",
                        Field = "email",
                        IssueType = "Duplicate",
                        AffectedRecords = totalDuplicates,
                        Percentage = (decimal)totalDuplicates / users.Count * 100,
                        Examples = duplicateEmails.Take(3).Select(g => $"Email duplicado: {g.Key} ({g.Count()} ocorrências)").ToList(),
                        SuggestedFix = "Consolidar usuários duplicados ou corrigir emails"
                    });
                }

                // Analisar usuários inativos
                var inactiveUsers = users.Where(u => u.State != "active").ToList();
                if (inactiveUsers.Any())
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Users",
                        Field = "state",
                        IssueType = "Inconsistent",
                        AffectedRecords = inactiveUsers.Count,
                        Percentage = (decimal)inactiveUsers.Count / users.Count * 100,
                        Examples = inactiveUsers.Take(3).Select(u => $"Nome: {u.Name}, Estado: {u.State}").ToList(),
                        SuggestedFix = "Decidir se usuários inativos devem ser migrados ou excluídos"
                    });
                }

                _logger.LogInformation($"Análise de usuários concluída: {users.Count} usuários, {issues.Count(i => i.EntityType == "Users")} problemas encontrados");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao analisar qualidade dos dados de usuários");
                issues.Add(new DataQualityIssue
                {
                    EntityType = "Users",
                    Field = "General",
                    IssueType = "Error",
                    AffectedRecords = 0,
                    Percentage = 0,
                    SuggestedFix = $"Erro ao acessar dados de usuários: {ex.Message}"
                });
            }
        }

        private async Task AnalyzeQueuesDataQualityAsync(List<DataQualityIssue> issues, Dictionary<string, long> recordsByType)
        {
            try
            {
                var queues = await GetGenesysQueuesAsync();
                recordsByType["Queues"] = queues.Count;

                if (queues.Count == 0)
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Queues",
                        Field = "General",
                        IssueType = "Missing",
                        AffectedRecords = 0,
                        Percentage = 100,
                        SuggestedFix = "Verificar se existem filas no Genesys Cloud ou se há problemas de permissão"
                    });
                    return;
                }

                // Analisar descrições faltantes
                var queuesWithoutDescription = queues.Where(q => string.IsNullOrWhiteSpace(q.Description)).ToList();
                if (queuesWithoutDescription.Any())
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Queues",
                        Field = "description",
                        IssueType = "Missing",
                        AffectedRecords = queuesWithoutDescription.Count,
                        Percentage = (decimal)queuesWithoutDescription.Count / queues.Count * 100,
                        Examples = queuesWithoutDescription.Take(3).Select(q => $"Fila: {q.Name}").ToList(),
                        SuggestedFix = "Adicionar descrições baseadas no nome da fila ou função"
                    });
                }

                // Analisar nomes duplicados
                var duplicateNames = queues.GroupBy(q => q.Name.ToLower())
                    .Where(g => g.Count() > 1)
                    .ToList();

                if (duplicateNames.Any())
                {
                    var totalDuplicates = duplicateNames.Sum(g => g.Count() - 1);
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Queues",
                        Field = "name",
                        IssueType = "Duplicate",
                        AffectedRecords = totalDuplicates,
                        Percentage = (decimal)totalDuplicates / queues.Count * 100,
                        Examples = duplicateNames.Take(3).Select(g => $"Nome duplicado: {g.Key} ({g.Count()} ocorrências)").ToList(),
                        SuggestedFix = "Renomear filas duplicadas ou consolidar se apropriado"
                    });
                }

                // Analisar filas sem configurações de mídia
                var queuesWithoutMediaSettings = queues.Where(q => q.MediaSettings == null || !q.MediaSettings.Any()).ToList();
                if (queuesWithoutMediaSettings.Any())
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Queues",
                        Field = "mediaSettings",
                        IssueType = "Missing",
                        AffectedRecords = queuesWithoutMediaSettings.Count,
                        Percentage = (decimal)queuesWithoutMediaSettings.Count / queues.Count * 100,
                        Examples = queuesWithoutMediaSettings.Take(3).Select(q => $"Fila: {q.Name}").ToList(),
                        SuggestedFix = "Configurar tipos de mídia suportados para cada fila"
                    });
                }

                _logger.LogInformation($"Análise de filas concluída: {queues.Count} filas, {issues.Count(i => i.EntityType == "Queues")} problemas encontrados");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao analisar qualidade dos dados de filas");
                issues.Add(new DataQualityIssue
                {
                    EntityType = "Queues",
                    Field = "General",
                    IssueType = "Error",
                    AffectedRecords = 0,
                    Percentage = 0,
                    SuggestedFix = $"Erro ao acessar dados de filas: {ex.Message}"
                });
            }
        }

        private async Task AnalyzeFlowsDataQualityAsync(List<DataQualityIssue> issues, Dictionary<string, long> recordsByType)
        {
            try
            {
                var flows = await GetGenesysFlowsAsync();
                recordsByType["Flows"] = flows.Count;

                if (flows.Count == 0)
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Flows",
                        Field = "General",
                        IssueType = "Missing",
                        AffectedRecords = 0,
                        Percentage = 100,
                        SuggestedFix = "Verificar se existem flows no Genesys Cloud ou se há problemas de permissão"
                    });
                    return;
                }

                // Analisar flows não publicados
                var unpublishedFlows = flows.Where(f => !f.Published).ToList();
                if (unpublishedFlows.Any())
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Flows",
                        Field = "published",
                        IssueType = "Inconsistent",
                        AffectedRecords = unpublishedFlows.Count,
                        Percentage = (decimal)unpublishedFlows.Count / flows.Count * 100,
                        Examples = unpublishedFlows.Take(3).Select(f => $"Flow: {f.Name}").ToList(),
                        SuggestedFix = "Decidir se flows não publicados devem ser migrados"
                    });
                }

                // Analisar flows sem descrição
                var flowsWithoutDescription = flows.Where(f => string.IsNullOrWhiteSpace(f.Description)).ToList();
                if (flowsWithoutDescription.Any())
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Flows",
                        Field = "description",
                        IssueType = "Missing",
                        AffectedRecords = flowsWithoutDescription.Count,
                        Percentage = (decimal)flowsWithoutDescription.Count / flows.Count * 100,
                        Examples = flowsWithoutDescription.Take(3).Select(f => $"Flow: {f.Name}").ToList(),
                        SuggestedFix = "Adicionar descrições para facilitar a migração e manutenção"
                    });
                }

                // Analisar flows inativos
                var inactiveFlows = flows.Where(f => f.State != "active").ToList();
                if (inactiveFlows.Any())
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Flows",
                        Field = "state",
                        IssueType = "Inconsistent",
                        AffectedRecords = inactiveFlows.Count,
                        Percentage = (decimal)inactiveFlows.Count / flows.Count * 100,
                        Examples = inactiveFlows.Take(3).Select(f => $"Flow: {f.Name}, Estado: {f.State}").ToList(),
                        SuggestedFix = "Decidir se flows inativos devem ser migrados ou excluídos"
                    });
                }

                _logger.LogInformation($"Análise de flows concluída: {flows.Count} flows, {issues.Count(i => i.EntityType == "Flows")} problemas encontrados");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao analisar qualidade dos dados de flows");
                issues.Add(new DataQualityIssue
                {
                    EntityType = "Flows",
                    Field = "General",
                    IssueType = "Error",
                    AffectedRecords = 0,
                    Percentage = 0,
                    SuggestedFix = $"Erro ao acessar dados de flows: {ex.Message}"
                });
            }
        }

        private async Task AnalyzeSkillsDataQualityAsync(List<DataQualityIssue> issues, Dictionary<string, long> recordsByType)
        {
            try
            {
                var skills = await GetGenesysSkillsAsync();
                recordsByType["Skills"] = skills.Count;

                if (skills.Count == 0)
                {
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Skills",
                        Field = "General",
                        IssueType = "Missing",
                        AffectedRecords = 0,
                        Percentage = 100,
                        SuggestedFix = "Verificar se existem skills no Genesys Cloud ou se há problemas de permissão"
                    });
                    return;
                }

                // Analisar skills duplicadas
                var duplicateSkills = skills.GroupBy(s => s.Name.ToLower())
                    .Where(g => g.Count() > 1)
                    .ToList();

                if (duplicateSkills.Any())
                {
                    var totalDuplicates = duplicateSkills.Sum(g => g.Count() - 1);
                    issues.Add(new DataQualityIssue
                    {
                        EntityType = "Skills",
                        Field = "name",
                        IssueType = "Duplicate",
                        AffectedRecords = totalDuplicates,
                        Percentage = (decimal)totalDuplicates / skills.Count * 100,
                        Examples = duplicateSkills.Take(3).Select(g => $"Skill duplicada: {g.Key} ({g.Count()} ocorrências)").ToList(),
                        SuggestedFix = "Consolidar skills duplicadas ou renomear se necessário"
                    });
                }

                _logger.LogInformation($"Análise de skills concluída: {skills.Count} skills, {issues.Count(i => i.EntityType == "Skills")} problemas encontrados");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao analisar qualidade dos dados de skills");
                issues.Add(new DataQualityIssue
                {
                    EntityType = "Skills",
                    Field = "General",
                    IssueType = "Error",
                    AffectedRecords = 0,
                    Percentage = 0,
                    SuggestedFix = $"Erro ao acessar dados de skills: {ex.Message}"
                });
            }
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private (decimal OverallScore, decimal CompletenessScore, decimal ConsistencyScore, decimal AccuracyScore) CalculateQualityMetrics(List<DataQualityIssue> issues, long totalRecords)
        {
            if (totalRecords == 0)
            {
                return (0, 0, 0, 0);
            }

            // Calcular pontuações baseadas nos tipos de problemas
            var missingIssues = issues.Where(i => i.IssueType == "Missing").Sum(i => i.AffectedRecords);
            var invalidIssues = issues.Where(i => i.IssueType == "Invalid").Sum(i => i.AffectedRecords);
            var duplicateIssues = issues.Where(i => i.IssueType == "Duplicate").Sum(i => i.AffectedRecords);
            var inconsistentIssues = issues.Where(i => i.IssueType == "Inconsistent").Sum(i => i.AffectedRecords);

            var completenessScore = Math.Max(0, 100 - (decimal)missingIssues / totalRecords * 100);
            var accuracyScore = Math.Max(0, 100 - (decimal)invalidIssues / totalRecords * 100);
            var consistencyScore = Math.Max(0, 100 - (decimal)(duplicateIssues + inconsistentIssues) / totalRecords * 100);

            var overallScore = (completenessScore + accuracyScore + consistencyScore) / 3;

            return (overallScore, completenessScore, consistencyScore, accuracyScore);
        }

        private List<string> GenerateCleanupRecommendations(List<DataQualityIssue> issues)
        {
            var recommendations = new List<string>();

            if (issues.Any(i => i.IssueType == "Missing" && i.Field == "email"))
            {
                recommendations.Add("Obter emails faltantes através de integração com Active Directory");
            }

            if (issues.Any(i => i.IssueType == "Invalid" && i.Field == "email"))
            {
                recommendations.Add("Padronizar e validar formato de emails");
            }

            if (issues.Any(i => i.IssueType == "Missing" && i.Field == "description"))
            {
                recommendations.Add("Completar descrições faltantes baseadas no contexto");
            }

            if (issues.Any(i => i.IssueType == "Duplicate"))
            {
                recommendations.Add("Identificar e consolidar registros duplicados");
            }

            if (issues.Any(i => i.IssueType == "Inconsistent"))
            {
                recommendations.Add("Padronizar estados e valores inconsistentes");
            }

            if (issues.Any(i => i.IssueType == "Missing" && i.Field == "department"))
            {
                recommendations.Add("Definir estrutura organizacional e departamentos");
            }

            if (!recommendations.Any())
            {
                recommendations.Add("Dados estão em boa qualidade - prosseguir com a migração");
            }

            return recommendations;
        }

        private TimeSpan CalculateEstimatedMigrationTime(long totalRecords)
        {
            // Estimar tempo baseado no volume de dados (aproximadamente 100 registros por minuto)
            var minutes = Math.Max(30, totalRecords / 100); // Mínimo de 30 minutos
            return TimeSpan.FromMinutes(minutes);
        }

        private int CalculateRecommendedBatchSize(long totalRecords)
        {
            // Tamanho do lote baseado no volume total
            if (totalRecords < 100) return 10;
            if (totalRecords < 1000) return 50;
            if (totalRecords < 10000) return 100;
            return 200;
        }

        private string CalculateStorageRequirements(long totalRecords)
        {
            // Estimar requisitos de armazenamento (aproximadamente 2KB por registro)
            var sizeInKB = totalRecords * 2;
            if (sizeInKB < 1024) return $"~{sizeInKB}KB para dados de configuração";
            if (sizeInKB < 1024 * 1024) return $"~{sizeInKB / 1024:F1}MB para dados de configuração";
            return $"~{sizeInKB / (1024 * 1024):F1}GB para dados de configuração";
        }

        public async Task<MigrationPlan> GenerateMigrationPlanAsync(
            string migrationStrategy = "Phased", 
            List<string>? priorityEntityTypes = null)
        {
            _logger.LogInformation($"Gerando plano de migração com estratégia: {migrationStrategy}");

            var plan = new MigrationPlan();

            // Definir fases baseadas na estratégia
            switch (migrationStrategy.ToLower())
            {
                case "phased":
                    plan.Phases = GeneratePhasedMigrationPlan();
                    break;
                case "bigbang":
                    plan.Phases = GenerateBigBangMigrationPlan();
                    break;
                case "parallel":
                    plan.Phases = GenerateParallelMigrationPlan();
                    break;
                default:
                    plan.Phases = GeneratePhasedMigrationPlan();
                    break;
            }

            plan.TotalEstimatedDuration = TimeSpan.FromDays(plan.Phases.Sum(p => p.EstimatedDuration.TotalDays));

            plan.ResourceRequirements = new ResourceRequirements
            {
                TechnicalTeam = 4,
                BusinessAnalysts = 2,
                ProjectManagers = 1,
                TestingTeam = 2,
                InfrastructureRequirements = new List<string>
                {
                    "Dynamics 365 Customer Service licenses",
                    "Power Platform licenses",
                    "Azure integration services",
                    "Development/Test environments"
                },
                ToolsRequired = new List<string>
                {
                    "Data migration tools",
                    "Testing frameworks",
                    "Monitoring solutions",
                    "Backup systems"
                }
            };

            plan.Milestones = GenerateMilestones(plan.Phases);
            plan.RollbackStrategy = "Maintain parallel Genesys environment for 30 days post-migration";
            plan.TestingStrategy = "Comprehensive UAT with pilot user groups before full rollout";

            return plan;
        }

        public async Task<List<MigrationRecommendation>> GetMigrationRecommendationsAsync(
            string? category = null, 
            string? entityType = null)
        {
            var recommendations = new List<MigrationRecommendation>();

            // Recomendações estratégicas
            if (category == null || category == "Strategy")
            {
                recommendations.AddRange(GetStrategyRecommendations());
            }

            // Recomendações técnicas
            if (category == null || category == "Technical")
            {
                recommendations.AddRange(GetTechnicalRecommendations());
            }

            // Recomendações de processo
            if (category == null || category == "Process")
            {
                recommendations.AddRange(GetProcessRecommendations());
            }

            // Recomendações de risco
            if (category == null || category == "Risk")
            {
                recommendations.AddRange(GetRiskRecommendations());
            }

            return recommendations;
        }

        public async Task<MigrationValidationResult> ValidateEntityMigrationAsync(
            string genesysEntityId, 
            string entityType)
        {
            var result = new MigrationValidationResult();

            try
            {
                var entity = await GetGenesysEntityAsync(genesysEntityId, entityType);
                
                // Validações específicas por tipo
                switch (entityType.ToLower())
                {
                    case "users":
                        ValidateUserMigration(entity, result);
                        break;
                    case "queues":
                        ValidateQueueMigration(entity, result);
                        break;
                    case "flows":
                        ValidateFlowMigration(entity, result);
                        break;
                    default:
                        result.ValidationWarnings.Add($"Validação não implementada para tipo {entityType}");
                        break;
                }

                result.IsValid = result.ValidationErrors.Count == 0;
                return result;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ValidationErrors.Add($"Erro na validação: {ex.Message}");
                return result;
            }
        }

        public async Task<MappingSummary> GetMappingSummaryAsync()
        {
            var summary = new MappingSummary();

            try
            {
                _logger.LogInformation("Gerando resumo de mapeamento com dados reais");

                // Coletar dados reais de todas as entidades
                var genesysUsers = await GetGenesysUsersAsync();
                var dynamicsAgents = await GetDynamicsAgentsAsync();
                var genesysQueues = await GetGenesysQueuesAsync();
                var dynamicsWorkstreams = await GetDynamicsWorkstreamsAsync();
                var genesysFlows = await GetGenesysFlowsAsync();
                var genesysBots = await GetGenesysBotsAsync();
                var dynamicsBots = await GetDynamicsBotsAsync();
                var genesysSkills = await GetGenesysSkillsAsync();
                var dynamicsCharacteristics = await GetDynamicsCharacteristicsAsync();

                // Calcular contagens reais
                var totalGenesysEntities = genesysUsers.Count + genesysQueues.Count + genesysFlows.Count + 
                                         genesysBots.Count + genesysSkills.Count;
                var totalDynamicsEntities = dynamicsAgents.Count + dynamicsWorkstreams.Count + 
                                          dynamicsBots.Count + dynamicsCharacteristics.Count;

                summary.TotalGenesysEntities = totalGenesysEntities;
                summary.TotalDynamicsEntities = totalDynamicsEntities;

                // Calcular mapeamentos baseados em correspondências reais
                var userMappings = CalculateUserMappings(genesysUsers, dynamicsAgents);
                var queueMappings = CalculateQueueMappings(genesysQueues, dynamicsWorkstreams);
                var skillMappings = CalculateSkillMappings(genesysSkills, dynamicsCharacteristics);
                var botMappings = CalculateBotMappings(genesysBots, dynamicsBots);

                var totalMappable = userMappings.Mappable + queueMappings.Mappable + 
                                  skillMappings.Mappable + botMappings.Mappable;
                var totalUnmappable = userMappings.Unmappable + queueMappings.Unmappable + 
                                     skillMappings.Unmappable + botMappings.Unmappable;
                var totalPartial = userMappings.Partial + queueMappings.Partial + 
                                 skillMappings.Partial + botMappings.Partial;

                summary.MappableEntities = totalMappable;
                summary.UnmappableEntities = totalUnmappable;
                summary.PartialMappingEntities = totalPartial;

                // Calcular métricas de complexidade baseadas em dados reais
                summary.MigrationComplexity = CalculateMigrationComplexity(totalGenesysEntities, totalMappable, totalUnmappable);
                summary.EstimatedMigrationTime = CalculateEstimatedTime(totalGenesysEntities, totalUnmappable, genesysFlows.Count);
                summary.ConfidenceScore = CalculateConfidenceScore(totalMappable, totalUnmappable, totalPartial, totalGenesysEntities);

                // Contagens por tipo de entidade com dados reais
                summary.EntityTypeCounts = new Dictionary<string, EntityTypeCount>
                {
                    { "Users", new EntityTypeCount 
                        { 
                            GenesysCount = genesysUsers.Count, 
                            DynamicsCount = dynamicsAgents.Count, 
                            MappedCount = userMappings.Mappable, 
                            UnmappedCount = userMappings.Unmappable 
                        } 
                    },
                    { "Queues", new EntityTypeCount 
                        { 
                            GenesysCount = genesysQueues.Count, 
                            DynamicsCount = dynamicsWorkstreams.Count, 
                            MappedCount = queueMappings.Mappable, 
                            UnmappedCount = queueMappings.Unmappable 
                        } 
                    },
                    { "Flows", new EntityTypeCount 
                        { 
                            GenesysCount = genesysFlows.Count, 
                            DynamicsCount = 0, // Flows não têm equivalente direto no Dynamics
                            MappedCount = 0, 
                            UnmappedCount = genesysFlows.Count 
                        } 
                    },
                    { "Bots", new EntityTypeCount 
                        { 
                            GenesysCount = genesysBots.Count, 
                            DynamicsCount = dynamicsBots.Count, 
                            MappedCount = botMappings.Mappable, 
                            UnmappedCount = botMappings.Unmappable 
                        } 
                    },
                    { "Skills", new EntityTypeCount 
                        { 
                            GenesysCount = genesysSkills.Count, 
                            DynamicsCount = dynamicsCharacteristics.Count, 
                            MappedCount = skillMappings.Mappable, 
                            UnmappedCount = skillMappings.Unmappable 
                        } 
                    }
                };

                _logger.LogInformation($"Resumo gerado: {totalGenesysEntities} entidades Genesys, {totalDynamicsEntities} entidades Dynamics");
                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao gerar resumo de mapeamento");
                throw;
            }
        }

        // Métodos auxiliares para cálculo de mapeamentos
        private (int Mappable, int Unmappable, int Partial) CalculateUserMappings(
            List<GenesysUser> genesysUsers, 
            List<DynamicsAgent> dynamicsAgents)
        {
            int mappable = 0, unmappable = 0, partial = 0;

            foreach (var user in genesysUsers)
            {
                var match = dynamicsAgents.FirstOrDefault(a => 
                    a.Email?.Equals(user.Email, StringComparison.OrdinalIgnoreCase) == true ||
                    a.Username?.Equals(user.Username, StringComparison.OrdinalIgnoreCase) == true);

                if (match != null)
                {
                    // Verificar se é mapeamento completo ou parcial
                    bool hasCompleteMapping = !string.IsNullOrEmpty(match.Name) && 
                                            !string.IsNullOrEmpty(match.Email) &&
                                            !string.IsNullOrEmpty(match.Department);
                    
                    if (hasCompleteMapping)
                        mappable++;
                    else
                        partial++;
                }
                else
                {
                    unmappable++;
                }
            }

            return (mappable, unmappable, partial);
        }

        private (int Mappable, int Unmappable, int Partial) CalculateQueueMappings(
            List<GenesysQueue> genesysQueues, 
            List<Models.DynamicsWorkstream> dynamicsWorkstreams)
        {
            int mappable = 0, unmappable = 0, partial = 0;

            foreach (var queue in genesysQueues)
            {
                var match = dynamicsWorkstreams.FirstOrDefault(w => 
                    w.Name?.Equals(queue.Name, StringComparison.OrdinalIgnoreCase) == true);

                if (match != null)
                {
                    // Verificar se é mapeamento completo ou parcial
                    bool hasCompleteMapping = !string.IsNullOrEmpty(match.Name) && 
                                            !string.IsNullOrEmpty(match.Description);
                    
                    if (hasCompleteMapping)
                        mappable++;
                    else
                        partial++;
                }
                else
                {
                    unmappable++;
                }
            }

            return (mappable, unmappable, partial);
        }

        private (int Mappable, int Unmappable, int Partial) CalculateSkillMappings(
            List<GenesysSkill> genesysSkills, 
            List<DynamicsCharacteristic> dynamicsCharacteristics)
        {
            int mappable = 0, unmappable = 0, partial = 0;

            foreach (var skill in genesysSkills)
            {
                var match = dynamicsCharacteristics.FirstOrDefault(c => 
                    c.Name?.Equals(skill.Name, StringComparison.OrdinalIgnoreCase) == true);

                if (match != null)
                {
                    mappable++;
                }
                else
                {
                    unmappable++;
                }
            }

            return (mappable, unmappable, partial);
        }

        private (int Mappable, int Unmappable, int Partial) CalculateBotMappings(
            List<GenesysBotConfiguration> genesysBots, 
            List<Models.DynamicsBotConfiguration> dynamicsBots)
        {
            int mappable = 0, unmappable = 0, partial = 0;

            foreach (var bot in genesysBots)
            {
                var match = dynamicsBots.FirstOrDefault(b => 
                    b.Name?.Equals(bot.Name, StringComparison.OrdinalIgnoreCase) == true);

                if (match != null)
                {
                    mappable++;
                }
                else
                {
                    unmappable++;
                }
            }

            return (mappable, unmappable, partial);
        }

        private string CalculateMigrationComplexity(int totalEntities, int mappable, int unmappable)
        {
            if (totalEntities == 0) return "Low";

            double unmappablePercentage = (double)unmappable / totalEntities * 100;
            
            if (unmappablePercentage > 30) return "High";
            if (unmappablePercentage > 15) return "Medium";
            return "Low";
        }

        private TimeSpan CalculateEstimatedTime(int totalEntities, int unmappable, int flowsCount)
        {
            // Base: 1 dia por 50 entidades mapeáveis
            int baseDays = Math.Max(1, totalEntities / 50);
            
            // Adicionar tempo extra para entidades não mapeáveis (2x mais tempo)
            int extraDays = unmappable / 25;
            
            // Adicionar tempo extra para flows (mais complexos)
            int flowDays = flowsCount / 10;
            
            return TimeSpan.FromDays(baseDays + extraDays + flowDays);
        }

        private decimal CalculateConfidenceScore(int mappable, int unmappable, int partial, int total)
        {
            if (total == 0) return 0;

            // Score baseado na porcentagem de entidades mapeáveis
            decimal mappableScore = (decimal)mappable / total * 100;
            decimal partialScore = (decimal)partial / total * 50; // Parciais valem metade
            
            return Math.Round(mappableScore + partialScore, 1);
        }

        public async Task<Dictionary<string, List<string>>> AnalyzeEntityDependenciesAsync(string entityType)
        {
            _logger.LogInformation($"Analisando dependências para tipo de entidade: {entityType}");
            
            var dependencies = new Dictionary<string, List<string>>();

            switch (entityType.ToLower())
            {
                case "users":
                    dependencies["Users"] = new List<string> { "Roles", "Skills" };
                    break;
                case "queues":
                    dependencies["Queues"] = new List<string> { "Users", "Skills", "Flows" };
                    break;
                case "flows":
                    dependencies["Flows"] = new List<string> { "Queues", "Bots" };
                    break;
                case "bots":
                    dependencies["Bots"] = new List<string> { "Flows" };
                    break;
                case "skills":
                    dependencies["Skills"] = new List<string> { "Users" };
                    break;
                case "roles":
                    dependencies["Roles"] = new List<string> { "Users" };
                    break;
                case "workstreams":
                    dependencies["Workstreams"] = new List<string> { "Users", "Queues" };
                    break;
                case "channels":
                    dependencies["Channels"] = new List<string> { "Workstreams" };
                    break;
                default:
                    _logger.LogWarning($"Tipo de entidade não reconhecido: {entityType}");
                    dependencies[entityType] = new List<string>();
                    break;
            }

            _logger.LogInformation($"Dependências encontradas para {entityType}: {string.Join(", ", dependencies.SelectMany(d => d.Value))}");
            
            return dependencies;
        }

        public async Task<List<EntityMapping>> SuggestEntityMappingAsync(
            string genesysEntityId, 
            string entityType)
        {
            var suggestions = new List<EntityMapping>();

            // Implementar lógica de sugestão baseada em similaridade
            // Por enquanto, retorna lista vazia
            
            return suggestions;
        }

        public async Task<byte[]> ExportMappingReportAsync(
            MappingAnalysisReport report, 
            string format = "JSON")
        {
            switch (format.ToUpper())
            {
                case "JSON":
                    var json = JsonConvert.SerializeObject(report, Formatting.Indented);
                    return Encoding.UTF8.GetBytes(json);
                
                case "EXCEL":
                    // Implementar exportação para Excel
                    throw new NotImplementedException("Exportação para Excel não implementada");
                
                case "PDF":
                    // Implementar exportação para PDF
                    throw new NotImplementedException("Exportação para PDF não implementada");
                
                default:
                    throw new ArgumentException($"Formato não suportado: {format}");
            }
        }

        public async Task<bool> RefreshDataCacheAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && DateTime.UtcNow - _lastCacheUpdate < _cacheValidityPeriod)
            {
                return true;
            }

            try
            {
                _dataCache.Clear();
                _lastCacheUpdate = DateTime.UtcNow;
                _logger.LogInformation("Cache de dados atualizado com sucesso");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar cache de dados");
                return false;
            }
        }

        #region Métodos Auxiliares

        private string GetGenesysEntityType(string entityType) => entityType switch
        {
            "Users" => "User",
            "Queues" => "Queue", 
            "Flows" => "Flow",
            "Bots" => "BotConfiguration",
            "Skills" => "Skill",
            "Roles" => "Role",
            _ => entityType
        };

        private string GetDynamicsEntityType(string entityType) => entityType switch
        {
            "Users" => "SystemUser",
            "Queues" => "LiveWorkstream",
            "Flows" => "Workflow",
            "Bots" => "BotConfiguration", 
            "Skills" => "Characteristic",
            "Roles" => "Role",
            "Workstreams" => "LiveWorkstream",
            "Channels" => "Channel",
            _ => entityType
        };

        private int CalculateComplexityScore(EntityTypeMapping mapping)
        {
            int score = 1;
            
            if (mapping.MappingStatus == "Complex") score += 4;
            if (mapping.MappingStatus == "Partial") score += 2;
            if (mapping.FieldMappings.Any(f => f.MappingType == "Complex")) score += 2;
            if (mapping.FieldMappings.Any(f => f.MappingType == "Manual")) score += 3;
            if (mapping.Entities.Any(e => e.Issues.Any(i => i.Severity == "High"))) score += 2;

            return Math.Min(score, 10);
        }

        private string DetermineMigrationPriority(EntityTypeMapping mapping)
        {
            if (mapping.ComplexityScore >= 8) return "Critical";
            if (mapping.ComplexityScore >= 6) return "High";
            if (mapping.ComplexityScore >= 4) return "Medium";
            return "Low";
        }

        private string DetermineMappingStatus(List<EntityMapping> entities)
        {
            if (entities.All(e => e.MappingConfidence >= 90)) return "Direct";
            if (entities.Any(e => e.MappingConfidence < 50)) return "Complex";
            if (entities.Any(e => e.Issues.Any(i => i.Severity == "High"))) return "Complex";
            return "Partial";
        }

        // Métodos para obter dados (implementar com clientes reais)
        private async Task<List<GenesysUser>> GetGenesysUsersAsync()
        {
            try
            {
                _logger.LogInformation("=== INICIANDO GetGenesysUsersAsync ===");
                _logger.LogInformation("Chamando _genesysClient.GetUsersAsync()...");
                
                var result = await _genesysClient.GetUsersAsync();
                
                _logger.LogInformation($"Resultado obtido: {result?.GetType().Name ?? "null"}");
                
                if (result == null)
                {
                    _logger.LogWarning("Resultado é null - usando dados simulados");
                    return GetMockGenesysUsers();
                }
                
                // O GenesysCloudClient retorna Dictionary<string, object>, não JsonElement
                if (result is Dictionary<string, object> dictionary)
                {
                    _logger.LogInformation("Resultado é Dictionary - processando usuários reais");
                    
                    if (dictionary.TryGetValue("users", out var usersObj) && usersObj is List<object> usersList)
                    {
                        _logger.LogInformation($"Encontrados {usersList.Count} usuários reais do Genesys Cloud");
                        
                        var users = new List<GenesysUser>();
                        foreach (var userObj in usersList)
                        {
                            if (userObj is Dictionary<string, object?> userDict)
                            {
                                var user = new GenesysUser
                                {
                                    Id = userDict.TryGetValue("id", out var id) ? id?.ToString() ?? "" : "",
                                    Name = userDict.TryGetValue("name", out var name) ? name?.ToString() ?? "" : "",
                                    Email = userDict.TryGetValue("email", out var email) ? email?.ToString() ?? "" : "",
                                    Username = userDict.TryGetValue("username", out var username) ? username?.ToString() ?? "" : "",
                                    State = userDict.TryGetValue("state", out var state) ? state?.ToString() ?? "active" : "active",
                                    Department = null,
                                    Title = null,
                                    DateCreated = DateTime.UtcNow,
                                    IsSimulated = false,
                                    DataSource = "GENESYS_CLOUD_API"
                                };
                                
                                users.Add(user);
                            }
                        }
                        
                        _logger.LogInformation($"Processados {users.Count} usuários reais do Genesys Cloud");
                        return users;
                    }
                    else
                    {
                        _logger.LogWarning("Dictionary não contém lista de usuários válida - usando dados simulados");
                        return GetMockGenesysUsers();
                    }
                }
                else if (result is JsonElement jsonElement)
                {
                    _logger.LogInformation($"JsonElement obtido com ValueKind: {jsonElement.ValueKind}");
                    
                    if (jsonElement.TryGetProperty("entities", out var entitiesElement))
                    {
                        _logger.LogInformation($"Propriedade 'entities' encontrada com {entitiesElement.GetArrayLength()} elementos");
                    var users = new List<GenesysUser>();
                    foreach (var userElement in entitiesElement.EnumerateArray())
                    {
                        var user = new GenesysUser
                        {
                            Id = userElement.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                            Name = userElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                            Email = userElement.TryGetProperty("email", out var emailProp) ? emailProp.GetString() ?? "" : "",
                            Username = userElement.TryGetProperty("username", out var usernameProp) ? usernameProp.GetString() ?? "" : "",
                            State = userElement.TryGetProperty("state", out var stateProp) ? stateProp.GetString() ?? "active" : "active",
                            Department = userElement.TryGetProperty("department", out var deptProp) ? deptProp.GetString() : null,
                            Title = userElement.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null,
                            DateCreated = userElement.TryGetProperty("dateCreated", out var dateProp) && DateTime.TryParse(dateProp.GetString(), out var date) ? date : DateTime.UtcNow,
                            IsSimulated = false,
                            DataSource = "GENESYS_CLOUD_API"
                        };
                        
                        // Obter roles se disponível
                        if (userElement.TryGetProperty("roles", out var rolesElement))
                        {
                            user.Roles = rolesElement.EnumerateArray()
                                .Select(r => new GenesysRole
                                {
                                    Id = r.TryGetProperty("id", out var roleIdProp) ? roleIdProp.GetString() ?? "" : "",
                                    Name = r.TryGetProperty("name", out var roleName) ? roleName.GetString() ?? "" : "",
                                    Description = r.TryGetProperty("description", out var roleDescProp) ? roleDescProp.GetString() : null
                                })
                                .ToList();
                        }
                        
                        users.Add(user);
                    }
                    
                    _logger.LogInformation($"Obtidos {users.Count} usuários do Genesys Cloud");
                    return users;
                }
                else
                {
                    _logger.LogWarning($"Propriedade 'entities' não encontrada no JsonElement. Propriedades disponíveis: {string.Join(", ", jsonElement.EnumerateObject().Select(p => p.Name))}");
                    return GetMockGenesysUsers();
                }
            }
            else
            {
                _logger.LogWarning($"Resultado não é JsonElement. Tipo: {result.GetType().Name} - usando dados simulados");
                return GetMockGenesysUsers();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== ERRO em GetGenesysUsersAsync ===");
            _logger.LogError($"Tipo da exceção: {ex.GetType().Name}");
            _logger.LogError($"Mensagem: {ex.Message}");
            _logger.LogError($"StackTrace: {ex.StackTrace}");
            return GetMockGenesysUsers();
        }
        }

        private async Task<List<DynamicsAgent>> GetDynamicsAgentsAsync()
        {
            try
            {
                _logger.LogInformation("Obtendo agentes do Dynamics 365");
                var result = await _dynamicsClient.GetAgentsAsync();
                
                if (result is JsonElement jsonElement && jsonElement.TryGetProperty("value", out var valueElement))
                {
                    var agents = new List<DynamicsAgent>();
                    foreach (var agentElement in valueElement.EnumerateArray())
                    {
                        var agent = new DynamicsAgent
                        {
                            Id = agentElement.TryGetProperty("systemuserid", out var idProp) ? idProp.GetString() ?? "" : "",
                            Name = agentElement.TryGetProperty("fullname", out var nameProp) ? nameProp.GetString() ?? "" : "",
                            Email = agentElement.TryGetProperty("internalemailaddress", out var emailProp) ? emailProp.GetString() ?? "" : "",
                            Username = agentElement.TryGetProperty("domainname", out var usernameProp) ? usernameProp.GetString() ?? "" : "",
                            Status = agentElement.TryGetProperty("isdisabled", out var disabledProp) ? (!disabledProp.GetBoolean() ? "Active" : "Inactive") : "Active",
                            Department = agentElement.TryGetProperty("businessunitid", out var buProp) ? buProp.GetString() : null,
                            MigrationDate = agentElement.TryGetProperty("createdon", out var dateProp) && DateTime.TryParse(dateProp.GetString(), out var createdDate) ? createdDate : DateTime.UtcNow,
                            IsSimulated = false,
                            DataSource = "DYNAMICS_365_API"
                        };
                        
                        // Obter skills se disponível
                        if (agentElement.TryGetProperty("skills", out var skillsElement))
                        {
                            agent.Skills = skillsElement.EnumerateArray()
                                .Select(s => new DynamicsSkill
                                {
                                    Name = s.TryGetProperty("name", out var skillNameProp) ? skillNameProp.GetString() ?? "" : "",
                                    ProficiencyValue = s.TryGetProperty("proficiency", out var profProp) ? profProp.GetDecimal() : 0
                                })
                                .ToList();
                        }
                        
                        agents.Add(agent);
                    }
                    
                    _logger.LogInformation($"Obtidos {agents.Count} agentes do Dynamics 365");
                    return agents;
                }
                
                _logger.LogWarning("Resposta do Dynamics não contém propriedade 'value' - usando dados simulados");
                return GetMockDynamicsAgents();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter agentes do Dynamics 365 - usando dados simulados como fallback");
                return GetMockDynamicsAgents();
            }
        }

        private async Task<List<GenesysQueue>> GetGenesysQueuesAsync()
        {
            try
            {
                _logger.LogInformation("Obtendo filas do Genesys Cloud");
                var result = await _genesysClient.GetQueuesAsync();
                
                if (result is JsonElement jsonElement && jsonElement.TryGetProperty("entities", out var entitiesElement))
                {
                    var queues = new List<GenesysQueue>();
                    foreach (var queueElement in entitiesElement.EnumerateArray())
                    {
                        var queue = new GenesysQueue
                        {
                            Id = queueElement.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                            Name = queueElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                            Description = queueElement.TryGetProperty("description", out var descProp) ? descProp.GetString() : null,
                            MediaSettings = new Dictionary<string, object>(),
                            RoutingRules = new List<GenesysRoutingRule>(),
                            DateCreated = queueElement.TryGetProperty("dateCreated", out var dateProp) && DateTime.TryParse(dateProp.GetString(), out var date) ? date : DateTime.UtcNow,
                            State = queueElement.TryGetProperty("state", out var stateProp) ? stateProp.GetString() ?? "active" : "active",
                            MemberCount = queueElement.TryGetProperty("memberCount", out var memberCountProp) ? memberCountProp.GetInt32() : 0
                        };
                        
                        // Obter configurações de mídia se disponível
                        if (queueElement.TryGetProperty("mediaSettings", out var mediaElement))
                        {
                            foreach (var mediaProp in mediaElement.EnumerateObject())
                            {
                                queue.MediaSettings[mediaProp.Name] = mediaProp.Value;
                            }
                        }
                        
                        queues.Add(queue);
                    }
                    
                    _logger.LogInformation($"Obtidas {queues.Count} filas do Genesys Cloud");
                    return queues;
                }
                
                _logger.LogWarning("Resposta do Genesys não contém propriedade 'entities' - retornando lista vazia");
                return new List<GenesysQueue>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter filas do Genesys Cloud - retornando lista vazia");
                return new List<GenesysQueue>();
            }
        }

        private async Task<List<Models.DynamicsWorkstream>> GetDynamicsWorkstreamsAsync()
        {
            try
            {
                _logger.LogInformation("Obtendo workstreams do Dynamics 365");
                var result = await _dynamicsClient.GetWorkstreamsAsync();
                
                if (result is JsonElement jsonElement && jsonElement.TryGetProperty("workstreams", out var workstreamsElement))
                {
                    var workstreams = new List<Models.DynamicsWorkstream>();
                    foreach (var workstreamElement in workstreamsElement.EnumerateArray())
                    {
                        var workstream = new Models.DynamicsWorkstream
                        {
                            Id = workstreamElement.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                            Name = workstreamElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                            Description = workstreamElement.TryGetProperty("description", out var descProp) ? descProp.GetString() : null,
                            ChannelType = workstreamElement.TryGetProperty("channelType", out var channelProp) ? channelProp.GetString() ?? "" : "Voice",
                            Status = workstreamElement.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "Active" : "Active"
                        };
                        
                        workstreams.Add(workstream);
                    }
                    
                    _logger.LogInformation($"Obtidos {workstreams.Count} workstreams do Dynamics 365");
                    return workstreams;
                }
                
                _logger.LogWarning("Resposta do Dynamics não contém propriedade 'workstreams' - retornando lista vazia");
                return new List<Models.DynamicsWorkstream>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter workstreams do Dynamics 365 - retornando lista vazia");
                return new List<Models.DynamicsWorkstream>();
            }
        }

        private async Task<List<GenesysFlow>> GetGenesysFlowsAsync()
        {
            try
            {
                _logger.LogInformation("Obtendo flows do Genesys Cloud");
                var result = await _genesysClient.GetFlowsAsync();
                
                if (result is JsonElement jsonElement && jsonElement.TryGetProperty("entities", out var entitiesElement))
                {
                    var flows = new List<GenesysFlow>();
                    foreach (var flowElement in entitiesElement.EnumerateArray())
                    {
                        var flow = new GenesysFlow
                        {
                            Id = flowElement.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                            Name = flowElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                            Description = flowElement.TryGetProperty("description", out var descProp) ? descProp.GetString() : null,
                            Type = flowElement.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "inboundcall" : "inboundcall",
                            State = flowElement.TryGetProperty("active", out var activeProp) ? (activeProp.GetBoolean() ? "active" : "inactive") : "active",
                            DateCreated = flowElement.TryGetProperty("dateCreated", out var dateProp) && DateTime.TryParse(dateProp.GetString(), out var date) ? date : DateTime.UtcNow,
                            Version = flowElement.TryGetProperty("version", out var versionProp) && int.TryParse(versionProp.GetString(), out var versionInt) ? versionInt : 1,
                            Published = flowElement.TryGetProperty("published", out var publishedProp) ? publishedProp.GetBoolean() : false,
                            CreatedBy = flowElement.TryGetProperty("createdBy", out var createdByProp) && createdByProp.TryGetProperty("name", out var createdByNameProp) ? createdByNameProp.GetString() : null
                        };
                        
                        flows.Add(flow);
                    }
                    
                    _logger.LogInformation($"Obtidos {flows.Count} flows do Genesys Cloud");
                    return flows;
                }
                
                _logger.LogWarning("Resposta do Genesys não contém propriedade 'entities' - retornando lista vazia");
                return new List<GenesysFlow>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter flows do Genesys Cloud - retornando lista vazia");
                return new List<GenesysFlow>();
            }
        }

        private async Task<List<GenesysBotConfiguration>> GetGenesysBotsAsync()
        {
            try
            {
                _logger.LogInformation("Obtendo bots do Genesys Cloud através de flows");
                
                // Buscar flows do tipo digitalbot e bot
                var digitalBotFlows = await GetBotFlowsByTypeAsync("digitalbot");
                var botFlows = await GetBotFlowsByTypeAsync("bot");
                
                var bots = new List<GenesysBotConfiguration>();
                
                // Processar digital bot flows
                foreach (var flow in digitalBotFlows)
                {
                    var bot = new GenesysBotConfiguration
                    {
                        Id = flow.Id,
                        Name = flow.Name,
                        Description = flow.Description ?? "Bot flow do Genesys Cloud",
                        BotType = "digitalbot",
                        DateCreated = flow.DateCreated,
                        IsSimulated = false,
                        DataSource = "GENESYS_CLOUD_API_FLOWS"
                    };
                    bots.Add(bot);
                }
                
                // Processar bot flows (dialog engine)
                foreach (var flow in botFlows)
                {
                    var bot = new GenesysBotConfiguration
                    {
                        Id = flow.Id,
                        Name = flow.Name,
                        Description = flow.Description ?? "Dialog engine bot flow do Genesys Cloud",
                        BotType = "bot",
                        DateCreated = flow.DateCreated,
                        IsSimulated = false,
                        DataSource = "GENESYS_CLOUD_API_FLOWS"
                    };
                    bots.Add(bot);
                }
                
                if (bots.Any())
                {
                    _logger.LogInformation($"Obtidos {bots.Count} bots reais do Genesys Cloud através de flows");
                    return bots;
                }
                
                _logger.LogWarning("Nenhum bot flow encontrado no Genesys Cloud - retornando lista vazia");
                return new List<GenesysBotConfiguration>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter bots do Genesys Cloud - retornando lista vazia");
                return new List<GenesysBotConfiguration>();
            }
        }

        private async Task<List<GenesysFlow>> GetBotFlowsByTypeAsync(string flowType)
        {
            try
            {
                var result = await _genesysClient.GetFlowsAsync(filterType: flowType);
                
                if (result is Dictionary<string, object> resultDict && 
                    resultDict.TryGetValue("flows", out var flowsObj) && 
                    flowsObj is List<object> flowsList)
                {
                    var flows = new List<GenesysFlow>();
                    foreach (var flowObj in flowsList)
                    {
                        if (flowObj is Dictionary<string, object> flowDict)
                        {
                            var flow = new GenesysFlow
                            {
                                Id = flowDict.TryGetValue("id", out var id) ? id?.ToString() ?? "" : "",
                                Name = flowDict.TryGetValue("name", out var name) ? name?.ToString() ?? "" : "",
                                Description = flowDict.TryGetValue("description", out var desc) ? desc?.ToString() : null,
                                Type = flowType,
                                State = flowDict.TryGetValue("status", out var status) ? status?.ToString() ?? "active" : "active",
                                DateCreated = DateTime.UtcNow, // Valor padrão
                                Published = true,
                                Version = 1
                            };
                            flows.Add(flow);
                        }
                    }
                    return flows;
                }
                
                return new List<GenesysFlow>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao buscar flows do tipo {flowType}");
                return new List<GenesysFlow>();
            }
        }



        private async Task<List<Models.DynamicsBotConfiguration>> GetDynamicsBotsAsync()
        {
            try
            {
                _logger.LogInformation("Obtendo bots do Dynamics 365");
                var result = await _dynamicsClient.GetBotsAsync();
                
                if (result is JsonElement jsonElement && jsonElement.TryGetProperty("bots", out var botsElement))
                {
                    var bots = new List<Models.DynamicsBotConfiguration>();
                    foreach (var botElement in botsElement.EnumerateArray())
                    {
                        var bot = new Models.DynamicsBotConfiguration
                        {
                            Id = botElement.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                            Name = botElement.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                            Description = botElement.TryGetProperty("description", out var descProp) ? descProp.GetString() : null,
                            Status = botElement.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "Active" : "Active",
                            Language = botElement.TryGetProperty("language", out var langProp) ? langProp.GetString() ?? "pt-BR" : "pt-BR",
                            DateCreated = botElement.TryGetProperty("createdon", out var dateProp) && DateTime.TryParse(dateProp.GetString(), out var botCreatedDate) ? botCreatedDate : DateTime.UtcNow,
                            BotFrameworkId = botElement.TryGetProperty("botframeworkid", out var frameworkIdProp) ? frameworkIdProp.GetString() ?? "" : "",
                            WorkstreamId = botElement.TryGetProperty("workstreamid", out var workstreamIdProp) ? workstreamIdProp.GetString() ?? "" : "",
                            BotType = botElement.TryGetProperty("bottype", out var botTypeProp) ? botTypeProp.GetString() ?? "" : ""
                        };
                        
                        // Obter idiomas suportados se disponível
                        if (botElement.TryGetProperty("languages", out var languagesElement))
                        {
                            bot.Languages = languagesElement.EnumerateArray()
                                .Select(l => l.GetString() ?? "")
                                .Where(l => !string.IsNullOrEmpty(l))
                                .ToList();
                        }
                        
                        // Obter contagem de tópicos se disponível
                        if (botElement.TryGetProperty("topiccount", out var topicCountProp))
                        {
                            bot.TopicCount = topicCountProp.GetInt32();
                        }
                        
                        bots.Add(bot);
                    }
                    
                    _logger.LogInformation($"Obtidos {bots.Count} bots do Dynamics 365");
                    return bots;
                }
                
                _logger.LogWarning("Resposta do Dynamics não contém propriedade 'bots' - retornando lista vazia");
                return new List<Models.DynamicsBotConfiguration>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter bots do Dynamics 365 - retornando lista vazia");
                return new List<Models.DynamicsBotConfiguration>();
            }
        }

        private async Task<List<GenesysSkill>> GetGenesysSkillsAsync()
        {
            try
            {
                _logger.LogInformation("Obtendo skills do Genesys Cloud");
                var result = await _genesysClient.GetSkillsAsync(100); // Buscar até 100 skills
                
                if (result is Dictionary<string, object> resultDict && 
                    resultDict.TryGetValue("skills", out var skillsObj) && 
                    skillsObj is List<object> skillsList)
                {
                    var skills = new List<GenesysSkill>();
                    foreach (var skillObj in skillsList)
                    {
                        if (skillObj is Dictionary<string, object?> skillDict)
                        {
                            var skill = new GenesysSkill
                            {
                                Id = skillDict.TryGetValue("id", out var id) ? id?.ToString() ?? "" : "",
                                Name = skillDict.TryGetValue("name", out var name) ? name?.ToString() ?? "" : "",
                                Type = "ACD", // Genesys skills são tipicamente ACD
                                Proficiency = 1.0m, // Valor padrão, pois proficiency é por usuário
                                IsSimulated = false,
                                DataSource = "GENESYS_CLOUD_API"
                            };
                            skills.Add(skill);
                        }
                    }
                    
                    _logger.LogInformation($"Obtidas {skills.Count} skills reais do Genesys Cloud");
                    return skills;
                }
                
                _logger.LogWarning("Nenhuma skill encontrada no Genesys Cloud - retornando lista vazia");
                return new List<GenesysSkill>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter skills do Genesys Cloud - retornando lista vazia");
                return new List<GenesysSkill>();
            }
        }



        private async Task<List<DynamicsCharacteristic>> GetDynamicsCharacteristicsAsync()
        {
            _logger.LogWarning("Endpoint para características do Dynamics não implementado - retornando lista vazia");
            return new List<DynamicsCharacteristic>();
        }

        private async Task<object?> GetGenesysEntityAsync(string entityId, string entityType)
        {
            // Implementar busca específica por entidade
            return null;
        }

        private async Task<object?> GetDynamicsEntityAsync(string entityId, string entityType)
        {
            // Implementar busca específica por entidade
            return null;
        }

        // Métodos auxiliares para criação de objetos sugeridos
        private Models.DynamicsAgent CreateSuggestedDynamicsAgent(GenesysUser genesysUser)
        {
            return new Models.DynamicsAgent
            {
                Name = genesysUser.Name,
                Email = genesysUser.Email,
                Username = genesysUser.Username,
                Department = genesysUser.Department,
                GenesysUserId = genesysUser.Id,
                Status = genesysUser.State == "active" ? "Active" : "Inactive"
            };
        }

        private Models.DynamicsWorkstream CreateSuggestedDynamicsWorkstream(GenesysQueue genesysQueue)
        {
            return new Models.DynamicsWorkstream
            {
                Name = genesysQueue.Name,
                Description = genesysQueue.Description,
                GenesysQueueId = genesysQueue.Id,
                Status = genesysQueue.State == "active" ? "Active" : "Inactive",
                ChannelType = "Voice", // Default channel type
                RoutingMethod = "RoundRobin" // Default routing method
            };
        }

        private Models.DynamicsBotConfiguration CreateSuggestedDynamicsBot(GenesysBotConfiguration genesysBot)
        {
            return new Models.DynamicsBotConfiguration
            {
                Name = genesysBot.Name,
                Description = genesysBot.Description,
                GenesysBotId = genesysBot.Id,
                BotType = genesysBot.BotType,
                Languages = genesysBot.Languages
            };
        }

        // Métodos para análise de problemas
        private void AnalyzeUserMappingIssues(GenesysUser user, EntityMapping mapping)
        {
            if (string.IsNullOrEmpty(user.Email))
            {
                mapping.Issues.Add(new MappingIssue
                {
                    IssueType = "Missing",
                    Severity = "High",
                    Description = "Email obrigatório não encontrado",
                    SuggestedResolution = "Obter email do usuário antes da migração"
                });
            }
        }

        private void AnalyzeQueueMappingIssues(GenesysQueue queue, EntityMapping mapping)
        {
            if (queue.MediaSettings.Count > 1)
            {
                mapping.Issues.Add(new MappingIssue
                {
                    IssueType = "Complexity",
                    Severity = "Medium",
                    Description = "Fila suporta múltiplos tipos de mídia",
                    SuggestedResolution = "Criar workstreams separados para cada tipo de mídia"
                });
            }
        }

        private void AnalyzeBotMappingIssues(GenesysBotConfiguration bot, EntityMapping mapping)
        {
            mapping.Issues.Add(new MappingIssue
            {
                IssueType = "Complexity",
                Severity = "High",
                Description = "Bot requer recriação completa no Bot Framework",
                SuggestedResolution = "Analisar intents e entities para recriação manual"
            });
        }

        // Métodos para geração de ações de migração
        private List<MigrationAction> GenerateUserMigrationActions(GenesysUser user, DynamicsAgent? existingAgent)
        {
            var actions = new List<MigrationAction>();

            if (existingAgent == null)
            {
                actions.Add(new MigrationAction
                {
                    ActionType = "Create",
                    Description = "Criar novo agente no Dynamics",
                    Priority = 1,
                    EstimatedDuration = TimeSpan.FromMinutes(5),
                    Automatable = true,
                    RiskLevel = "Low"
                });
            }

            actions.Add(new MigrationAction
            {
                ActionType = "Transform",
                Description = "Mapear skills e proficiências",
                Priority = 2,
                EstimatedDuration = TimeSpan.FromMinutes(10),
                Automatable = true,
                RiskLevel = "Medium"
            });

            return actions;
        }

        private List<MigrationAction> GenerateQueueMigrationActions(GenesysQueue queue, Models.DynamicsWorkstream? existingWorkstream)
        {
            var actions = new List<MigrationAction>();

            actions.Add(new MigrationAction
            {
                ActionType = "Create",
                Description = "Criar workstream no Dynamics",
                Priority = 1,
                EstimatedDuration = TimeSpan.FromMinutes(15),
                Automatable = true,
                RiskLevel = "Low"
            });

            return actions;
        }

        private List<MigrationAction> GenerateFlowMigrationActions(GenesysFlow flow)
        {
            return new List<MigrationAction>
            {
                new()
                {
                    ActionType = "Manual",
                    Description = "Analisar lógica do flow e recriar no Power Automate",
                    Priority = 1,
                    EstimatedDuration = TimeSpan.FromHours(4),
                    Automatable = false,
                    RiskLevel = "High"
                }
            };
        }

        private List<MigrationAction> GenerateBotMigrationActions(GenesysBotConfiguration bot, Models.DynamicsBotConfiguration? existingBot)
        {
            return new List<MigrationAction>
            {
                new()
                {
                    ActionType = "Manual",
                    Description = "Recriar bot no Bot Framework",
                    Priority = 1,
                    EstimatedDuration = TimeSpan.FromDays(2),
                    Automatable = false,
                    RiskLevel = "High"
                }
            };
        }

        private List<PropertyDifference> CompareEntityProperties(object? genesysEntity, object? dynamicsEntity, string entityType)
        {
            // Implementar comparação detalhada de propriedades
            return new List<PropertyDifference>();
        }

        private string GenerateMigrationRecommendation(EntityComparison comparison)
        {
            return "Recomendação baseada na análise de diferenças entre entidades";
        }

        private List<MitigationStrategy> GenerateMitigationStrategies(List<RiskFactor> riskFactors)
        {
            return new List<MitigationStrategy>
            {
                new()
                {
                    RiskCategory = "Technical",
                    Strategy = "Implementar migração em fases com testes extensivos",
                    Implementation = "Criar ambiente de teste paralelo",
                    Effectiveness = "High",
                    Cost = "Medium",
                    Timeline = "2-3 semanas"
                }
            };
        }

        private List<ContingencyPlan> GenerateContingencyPlans()
        {
            return new List<ContingencyPlan>
            {
                new()
                {
                    Scenario = "Falha crítica durante migração",
                    Trigger = "Sistema indisponível por mais de 2 horas",
                    Actions = new List<string> { "Ativar rollback", "Comunicar stakeholders", "Investigar causa raiz" },
                    RollbackPlan = "Restaurar ambiente Genesys original",
                    CommunicationPlan = "Notificar todos os usuários via email e portal"
                }
            };
        }

        private List<MigrationPhase> GeneratePhasedMigrationPlan()
        {
            return new List<MigrationPhase>
            {
                new()
                {
                    PhaseNumber = 1,
                    Name = "Preparação e Configuração Base",
                    Description = "Configurar ambiente Dynamics e migrar dados mestres",
                    EntityTypes = new List<string> { "Skills", "Roles" },
                    EstimatedDuration = TimeSpan.FromDays(7),
                    Prerequisites = new List<string> { "Ambiente Dynamics provisionado", "Licenças configuradas" },
                    RiskLevel = "Low"
                },
                new()
                {
                    PhaseNumber = 2,
                    Name = "Migração de Usuários",
                    Description = "Migrar usuários e configurar permissões",
                    EntityTypes = new List<string> { "Users" },
                    EstimatedDuration = TimeSpan.FromDays(10),
                    Prerequisites = new List<string> { "Fase 1 completa", "Validação de dados" },
                    RiskLevel = "Medium"
                },
                new()
                {
                    PhaseNumber = 3,
                    Name = "Configuração de Workstreams",
                    Description = "Criar workstreams baseados nas filas do Genesys",
                    EntityTypes = new List<string> { "Queues", "Workstreams" },
                    EstimatedDuration = TimeSpan.FromDays(14),
                    Prerequisites = new List<string> { "Fase 2 completa", "Usuários validados" },
                    RiskLevel = "Medium"
                },
                new()
                {
                    PhaseNumber = 4,
                    Name = "Migração de Flows e Bots",
                    Description = "Recriar flows e configurar bots",
                    EntityTypes = new List<string> { "Flows", "Bots" },
                    EstimatedDuration = TimeSpan.FromDays(21),
                    Prerequisites = new List<string> { "Fase 3 completa", "Power Automate configurado" },
                    RiskLevel = "High"
                }
            };
        }

        private List<MigrationPhase> GenerateBigBangMigrationPlan()
        {
            return new List<MigrationPhase>
            {
                new()
                {
                    PhaseNumber = 1,
                    Name = "Migração Completa",
                    Description = "Migrar todos os componentes simultaneamente",
                    EntityTypes = new List<string> { "Users", "Queues", "Flows", "Bots", "Skills", "Roles" },
                    EstimatedDuration = TimeSpan.FromDays(30),
                    RiskLevel = "Critical"
                }
            };
        }

        private List<MigrationPhase> GenerateParallelMigrationPlan()
        {
            return new List<MigrationPhase>
            {
                new()
                {
                    PhaseNumber = 1,
                    Name = "Configuração Paralela",
                    Description = "Configurar Dynamics em paralelo ao Genesys",
                    EntityTypes = new List<string> { "All" },
                    EstimatedDuration = TimeSpan.FromDays(45),
                    RiskLevel = "Medium"
                },
                new()
                {
                    PhaseNumber = 2,
                    Name = "Cutover",
                    Description = "Alternar do Genesys para Dynamics",
                    EntityTypes = new List<string> { "All" },
                    EstimatedDuration = TimeSpan.FromDays(2),
                    RiskLevel = "High"
                }
            };
        }

        private List<Milestone> GenerateMilestones(List<MigrationPhase> phases)
        {
            var milestones = new List<Milestone>();
            var currentDate = DateTime.UtcNow;

            foreach (var phase in phases)
            {
                milestones.Add(new Milestone
                {
                    Name = $"Conclusão da {phase.Name}",
                    Description = $"Fase {phase.PhaseNumber} completada com sucesso",
                    TargetDate = currentDate.Add(phase.EstimatedDuration),
                    SuccessCriteria = new List<string> { "Todos os testes passaram", "Validação do usuário completa" }
                });

                currentDate = currentDate.Add(phase.EstimatedDuration);
            }

            return milestones;
        }

        private List<MigrationRecommendation> GetStrategyRecommendations()
        {
            return new List<MigrationRecommendation>
            {
                new()
                {
                    Category = "Strategy",
                    Title = "Adotar Migração em Fases",
                    Description = "Implementar migração gradual para reduzir riscos",
                    Priority = "High",
                    Impact = "Reduz risco de interrupção operacional",
                    Effort = "Medium",
                    Timeline = "6-8 semanas"
                }
            };
        }

        private List<MigrationRecommendation> GetTechnicalRecommendations()
        {
            return new List<MigrationRecommendation>
            {
                new()
                {
                    Category = "Technical",
                    Title = "Implementar Ambiente de Teste",
                    Description = "Criar ambiente espelho para testes antes da migração",
                    Priority = "High",
                    Impact = "Permite validação completa antes da produção",
                    Effort = "High",
                    Timeline = "2-3 semanas"
                }
            };
        }

        private List<MigrationRecommendation> GetProcessRecommendations()
        {
            return new List<MigrationRecommendation>
            {
                new()
                {
                    Category = "Process",
                    Title = "Estabelecer Plano de Comunicação",
                    Description = "Definir comunicação clara com todos os stakeholders",
                    Priority = "Medium",
                    Impact = "Reduz resistência à mudança",
                    Effort = "Low",
                    Timeline = "1 semana"
                }
            };
        }

        private List<MigrationRecommendation> GetRiskRecommendations()
        {
            return new List<MigrationRecommendation>
            {
                new()
                {
                    Category = "Risk",
                    Title = "Manter Ambiente Genesys Ativo",
                    Description = "Manter Genesys operacional por 30 dias após migração",
                    Priority = "High",
                    Impact = "Permite rollback rápido se necessário",
                    Effort = "Low",
                    Timeline = "30 dias pós-migração"
                }
            };
        }

        private void ValidateUserMigration(object entity, MigrationValidationResult result)
        {
            // Implementar validações específicas para usuários
            result.EstimatedEffort = "Medium";
            result.MigrationComplexity = "Medium";
            result.RecommendedApproach = "Automated migration with manual validation";
        }

        private void ValidateQueueMigration(object entity, MigrationValidationResult result)
        {
            // Implementar validações específicas para filas
            result.EstimatedEffort = "High";
            result.MigrationComplexity = "High";
            result.RecommendedApproach = "Manual configuration with automated data transfer";
        }

        private void ValidateFlowMigration(object entity, MigrationValidationResult result)
        {
            // Implementar validações específicas para flows
            result.EstimatedEffort = "High";
            result.MigrationComplexity = "Critical";
            result.RecommendedApproach = "Complete manual recreation in Power Automate";
            result.ValidationErrors.Add("Flows cannot be automatically migrated");
            result.RequiredActions.Add("Analyze flow logic and recreate manually");
        }

        private async Task AnalyzeAllEntityTypesAsync(EntityTypeMapping mapping)
        {
            _logger.LogInformation("Analisando todos os tipos de entidade");

            // Definir tipos de entidade para análise
            var entityTypes = new List<string> 
            { 
                "Users", "Queues", "Flows", "Bots", "Skills", "Roles", "Workstreams", "Channels" 
            };

            var allEntities = new List<EntityMapping>();
            var allFieldMappings = new List<FieldMapping>();

            // Analisar cada tipo de entidade
            foreach (var entityType in entityTypes)
            {
                try
                {
                    var typeMapping = new EntityTypeMapping
                    {
                        EntityType = entityType,
                        GenesysEntityType = GetGenesysEntityType(entityType),
                        DynamicsEntityType = GetDynamicsEntityType(entityType)
                    };

                    // Analisar o tipo específico
                    switch (entityType.ToLower())
                    {
                        case "users":
                            await AnalyzeUserMappingAsync(typeMapping);
                            break;
                        case "queues":
                            await AnalyzeQueueMappingAsync(typeMapping);
                            break;
                        case "flows":
                            await AnalyzeFlowMappingAsync(typeMapping);
                            break;
                        case "bots":
                            await AnalyzeBotMappingAsync(typeMapping);
                            break;
                        case "skills":
                            await AnalyzeSkillMappingAsync(typeMapping);
                            break;
                        case "roles":
                            await AnalyzeRoleMappingAsync(typeMapping);
                            break;
                        case "workstreams":
                            await AnalyzeWorkstreamMappingAsync(typeMapping);
                            break;
                        case "channels":
                            await AnalyzeChannelMappingAsync(typeMapping);
                            break;
                    }

                    // Adicionar entidades e mapeamentos de campo
                    allEntities.AddRange(typeMapping.Entities);
                    allFieldMappings.AddRange(typeMapping.FieldMappings);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Erro ao analisar tipo de entidade {entityType}");
                }
            }

            // Configurar o mapeamento consolidado
            mapping.Entities = allEntities;
            mapping.FieldMappings = allFieldMappings;
            mapping.MappingStatus = DetermineMappingStatus(allEntities);
            mapping.MigrationNotes = $"Análise consolidada de {entityTypes.Count} tipos de entidade. " +
                                   $"Total de entidades: {allEntities.Count}. " +
                                   $"Mapeamentos de campo: {allFieldMappings.Count}.";
        }

        #endregion

        #region Métodos para dados simulados/mock

        private List<GenesysUser> GetMockGenesysUsers()
        {
            _logger.LogInformation("Usando dados simulados para usuários do Genesys");
            return new List<GenesysUser>
            {
                new GenesysUser
                {
                    Id = "user-001",
                    Name = "João Silva",
                    Email = "joao.silva@empresa.com",
                    Username = "joao.silva",
                    State = "active",
                    Department = "Atendimento",
                    Title = "Agente Senior",
                    DateCreated = DateTime.UtcNow.AddDays(-30),
                    IsSimulated = true,
                    DataSource = "MOCK_DATA_FALLBACK",
                    Roles = new List<GenesysRole>
                    {
                        new GenesysRole { Id = "role-001", Name = "Agent", Description = "Agente de atendimento" }
                    }
                },
                new GenesysUser
                {
                    Id = "user-002",
                    Name = "Maria Santos",
                    Email = "maria.santos@empresa.com",
                    Username = "maria.santos",
                    State = "active",
                    Department = "Supervisão",
                    Title = "Supervisor",
                    DateCreated = DateTime.UtcNow.AddDays(-45),
                    IsSimulated = true,
                    DataSource = "MOCK_DATA_FALLBACK",
                    Roles = new List<GenesysRole>
                    {
                        new GenesysRole { Id = "role-002", Name = "Supervisor", Description = "Supervisor de equipe" }
                    }
                },
                new GenesysUser
                {
                    Id = "user-003",
                    Name = "Carlos Oliveira",
                    Email = "carlos.oliveira@empresa.com",
                    Username = "carlos.oliveira",
                    State = "active",
                    Department = "Atendimento",
                    Title = "Agente",
                    DateCreated = DateTime.UtcNow.AddDays(-15),
                    IsSimulated = true,
                    DataSource = "MOCK_DATA_FALLBACK",
                    Roles = new List<GenesysRole>
                    {
                        new GenesysRole { Id = "role-001", Name = "Agent", Description = "Agente de atendimento" }
                    }
                }
            };
        }

        private List<DynamicsAgent> GetMockDynamicsAgents()
        {
            _logger.LogInformation("Usando dados simulados para agentes do Dynamics");
            return new List<DynamicsAgent>
            {
                new DynamicsAgent
                {
                    Id = "agent-001",
                    Name = "João Silva",
                    Email = "joao.silva@empresa.com",
                    Username = "joao.silva@domain.com",
                    Status = "Active",
                    Department = "Customer Service",
                    MigrationDate = DateTime.UtcNow.AddDays(-10),
                    IsSimulated = true,
                    DataSource = "MOCK_DATA_FALLBACK",
                    Skills = new List<DynamicsSkill>
                    {
                        new DynamicsSkill { Name = "Customer Support", ProficiencyValue = 85 }
                    }
                },
                new DynamicsAgent
                {
                    Id = "agent-002",
                    Name = "Ana Costa",
                    Email = "ana.costa@empresa.com",
                    Username = "ana.costa@domain.com",
                    Status = "Active",
                    Department = "Technical Support",
                    MigrationDate = DateTime.UtcNow.AddDays(-5),
                    IsSimulated = true,
                    DataSource = "MOCK_DATA_FALLBACK",
                    Skills = new List<DynamicsSkill>
                    {
                        new DynamicsSkill { Name = "Technical Support", ProficiencyValue = 90 }
                    }
                }
            };
        }



        #endregion
    }
}
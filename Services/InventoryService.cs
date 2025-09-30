using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GenesysMigrationMCP.Services
{
    public interface IInventoryService
    {
        Task<InventoryReport> GetCompleteInventoryAsync();
        Task<GenesysInventory> GetGenesysInventoryAsync();
        Task<DynamicsInventory> GetDynamicsInventoryAsync();
        Task<InventoryComparison> CompareInventoriesAsync();
        Task<string> ExportInventoryReportAsync(InventoryReport report, string format = "json");
    }

    public class InventoryService : IInventoryService
    {
        private readonly ILogger<InventoryService> _logger;
        private readonly IMcpService _mcpService;

        public InventoryService(ILogger<InventoryService> logger, IMcpService mcpService)
        {
            _logger = logger;
            _mcpService = mcpService;
        }

        public async Task<InventoryReport> GetCompleteInventoryAsync()
        {
            try
            {
                _logger.LogInformation("Iniciando coleta de inventário completo...");

                var genesysTask = GetGenesysInventoryAsync();
                var dynamicsTask = GetDynamicsInventoryAsync();

                await Task.WhenAll(genesysTask, dynamicsTask);

                var genesysInventory = await genesysTask;
                var dynamicsInventory = await dynamicsTask;

                var report = new InventoryReport
                {
                    Id = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.UtcNow,
                    GenesysInventory = genesysInventory,
                    DynamicsInventory = dynamicsInventory,
                    Summary = GenerateInventorySummary(genesysInventory, dynamicsInventory)
                };

                _logger.LogInformation("Inventário completo coletado com sucesso");
                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao coletar inventário completo");
                throw;
            }
        }

        public async Task<GenesysInventory> GetGenesysInventoryAsync()
        {
            try
            {
                _logger.LogInformation("Coletando inventário do Genesys Cloud...");

                var inventory = new GenesysInventory
                {
                    CollectionTimestamp = DateTime.UtcNow
                };

                // Coletar Flows
                var flowsResult = await _mcpService.CallTool("list_genesys_flows", new Dictionary<string, object>());
                if (!flowsResult.IsError && flowsResult.Content != null && flowsResult.Content.Length > 0)
                {
                    var flowsData = JsonSerializer.Deserialize<Dictionary<string, object>>(flowsResult.Content[0].Text);
                    if (flowsData.ContainsKey("flows") && flowsData["flows"] is JsonElement flowsElement)
                    {
                        if (flowsElement.ValueKind == JsonValueKind.Array)
                        {
                            inventory.FlowsCount = flowsElement.GetArrayLength();
                        }
                        else
                        {
                            inventory.FlowsCount = 0;
                        }
                    }
                }

                // Coletar Usuários
                var usersResult = await _mcpService.CallTool("list_genesys_users", new Dictionary<string, object>());
                if (!usersResult.IsError && usersResult.Content != null && usersResult.Content.Length > 0)
                {
                    var usersData = JsonSerializer.Deserialize<Dictionary<string, object>>(usersResult.Content[0].Text);
                    if (usersData.ContainsKey("users") && usersData["users"] is JsonElement usersElement)
                    {
                        if (usersElement.ValueKind == JsonValueKind.Array)
                        {
                            inventory.UsersCount = usersElement.GetArrayLength();
                        }
                        else
                        {
                            inventory.UsersCount = 0;
                        }
                    }
                }

                // Coletar Filas
                var queuesResult = await _mcpService.CallTool("list_genesys_queues", new Dictionary<string, object>());
                if (!queuesResult.IsError && queuesResult.Content != null && queuesResult.Content.Length > 0)
                {
                    var queuesData = JsonSerializer.Deserialize<Dictionary<string, object>>(queuesResult.Content[0].Text);
                    if (queuesData.ContainsKey("queues") && queuesData["queues"] is JsonElement queuesElement)
                    {
                        if (queuesElement.ValueKind == JsonValueKind.Array)
                        {
                            inventory.QueuesCount = queuesElement.GetArrayLength();
                        }
                        else
                        {
                            inventory.QueuesCount = 0;
                        }
                    }
                }

                // Coletar Bots
                var botsResult = await _mcpService.CallTool("list_genesys_bots", new Dictionary<string, object>());
                if (!botsResult.IsError && botsResult.Content != null && botsResult.Content.Length > 0)
                {
                    var botsData = JsonSerializer.Deserialize<Dictionary<string, object>>(botsResult.Content[0].Text);
                    if (botsData.ContainsKey("bots") && botsData["bots"] is JsonElement botsElement)
                    {
                        if (botsElement.ValueKind == JsonValueKind.Array)
                        {
                            inventory.BotsCount = botsElement.GetArrayLength();
                        }
                        else
                        {
                            inventory.BotsCount = 0;
                        }
                    }
                }

                // Coletar Skills
                var skillsResult = await _mcpService.CallTool("list_genesys_skills", new Dictionary<string, object>());
                if (!skillsResult.IsError && skillsResult.Content != null && skillsResult.Content.Length > 0)
                {
                    var skillsData = JsonSerializer.Deserialize<Dictionary<string, object>>(skillsResult.Content[0].Text);
                    if (skillsData.ContainsKey("skills") && skillsData["skills"] is JsonElement skillsElement)
                    {
                        if (skillsElement.ValueKind == JsonValueKind.Array)
                        {
                            inventory.SkillsCount = skillsElement.GetArrayLength();
                        }
                        else
                        {
                            inventory.SkillsCount = 0;
                        }
                    }
                }

                // Coletar Routing Rules
                var routingResult = await _mcpService.CallTool("list_genesys_routing_rules", new Dictionary<string, object>());
                if (!routingResult.IsError && routingResult.Content != null && routingResult.Content.Length > 0)
                {
                    var routingData = JsonSerializer.Deserialize<Dictionary<string, object>>(routingResult.Content[0].Text);
                    if (routingData.ContainsKey("routingRules") && routingData["routingRules"] is JsonElement routingElement)
                    {
                        if (routingElement.ValueKind == JsonValueKind.Array)
                        {
                            inventory.RoutingRulesCount = routingElement.GetArrayLength();
                        }
                        else
                        {
                            inventory.RoutingRulesCount = 0;
                        }
                    }
                }

                // Coletar Workspaces
                var workspacesResult = await _mcpService.CallTool("list_genesys_workspaces", new Dictionary<string, object>());
                if (!workspacesResult.IsError && workspacesResult.Content != null && workspacesResult.Content.Length > 0)
                {
                    var workspacesData = JsonSerializer.Deserialize<Dictionary<string, object>>(workspacesResult.Content[0].Text);
                    if (workspacesData.ContainsKey("workspaces") && workspacesData["workspaces"] is JsonElement workspacesElement)
                    {
                        if (workspacesElement.ValueKind == JsonValueKind.Array)
                        {
                            inventory.WorkspacesCount = workspacesElement.GetArrayLength();
                        }
                        else
                        {
                            inventory.WorkspacesCount = 0;
                        }
                    }
                }

                // Coletar Divisions
                var divisionsResult = await _mcpService.CallTool("list_genesys_divisions", new Dictionary<string, object>());
                if (!divisionsResult.IsError && divisionsResult.Content != null && divisionsResult.Content.Length > 0)
                {
                    var divisionsData = JsonSerializer.Deserialize<Dictionary<string, object>>(divisionsResult.Content[0].Text);
                    if (divisionsData.ContainsKey("divisions") && divisionsData["divisions"] is JsonElement divisionsElement)
                    {
                        if (divisionsElement.ValueKind == JsonValueKind.Array)
                        {
                            inventory.DivisionsCount = divisionsElement.GetArrayLength();
                        }
                        else
                        {
                            inventory.DivisionsCount = 0;
                        }
                    }
                }

                inventory.TotalEntities = inventory.FlowsCount + inventory.UsersCount + 
                                        inventory.QueuesCount + inventory.BotsCount + 
                                        inventory.SkillsCount + inventory.RoutingRulesCount + 
                                        inventory.WorkspacesCount + inventory.DivisionsCount;

                _logger.LogInformation($"Inventário Genesys coletado: {inventory.TotalEntities} entidades totais");
                return inventory;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao coletar inventário do Genesys");
                throw;
            }
        }

        public async Task<DynamicsInventory> GetDynamicsInventoryAsync()
        {
            try
            {
                _logger.LogInformation("Coletando inventário do Dynamics Contact Center...");

                var inventory = new DynamicsInventory
                {
                    CollectionTimestamp = DateTime.UtcNow
                };

                // Coletar Agentes
                var agentsResult = await _mcpService.CallTool("list_dynamics_agents", new Dictionary<string, object>());
                if (!agentsResult.IsError && agentsResult.Content != null && agentsResult.Content.Length > 0)
                {
                    var agentsData = JsonSerializer.Deserialize<Dictionary<string, object>>(agentsResult.Content[0].Text);
                    if (agentsData.ContainsKey("agents") && agentsData["agents"] is JsonElement agentsElement)
                    {
                        inventory.AgentsCount = agentsElement.GetArrayLength();
                    }
                }

                // Coletar Workstreams
                var workstreamsResult = await _mcpService.CallTool("list_dynamics_workstreams", new Dictionary<string, object>());
                if (!workstreamsResult.IsError && workstreamsResult.Content != null && workstreamsResult.Content.Length > 0)
                {
                    var workstreamsData = JsonSerializer.Deserialize<Dictionary<string, object>>(workstreamsResult.Content[0].Text);
                    if (workstreamsData.ContainsKey("workstreams") && workstreamsData["workstreams"] is JsonElement workstreamsElement)
                    {
                        inventory.WorkstreamsCount = workstreamsElement.GetArrayLength();
                    }
                }

                // Coletar Bots
                var botsResult = await _mcpService.CallTool("list_dynamics_bots", new Dictionary<string, object>());
                if (!botsResult.IsError && botsResult.Content != null && botsResult.Content.Length > 0)
                {
                    var botsData = JsonSerializer.Deserialize<Dictionary<string, object>>(botsResult.Content[0].Text);
                    if (botsData.ContainsKey("bots") && botsData["bots"] is JsonElement botsElement)
                    {
                        inventory.BotsCount = botsElement.GetArrayLength();
                    }
                }

                // NOTA: Outras ferramentas do Dynamics não estão implementadas ainda
                // Por enquanto, definindo valores padrão para as entidades não coletadas
                inventory.WorkflowsCount = 0;
                inventory.BusinessProcessFlowsCount = 0;
                inventory.PowerVirtualAgentsCount = 0;
                inventory.RoutingRulesCount = 0;
                inventory.TeamsCount = 0;

                inventory.TotalEntities = inventory.AgentsCount + inventory.WorkstreamsCount + 
                                        inventory.BotsCount + inventory.WorkflowsCount + 
                                        inventory.BusinessProcessFlowsCount + inventory.PowerVirtualAgentsCount + 
                                        inventory.RoutingRulesCount + inventory.TeamsCount;

                _logger.LogInformation($"Inventário Dynamics coletado usando ferramentas disponíveis: Agentes={inventory.AgentsCount}, Workstreams={inventory.WorkstreamsCount}, Bots={inventory.BotsCount}");

                _logger.LogInformation($"Inventário Dynamics coletado: {inventory.TotalEntities} entidades totais");
                return inventory;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao coletar inventário do Dynamics");
                throw;
            }
        }

        public async Task<InventoryComparison> CompareInventoriesAsync()
        {
            try
            {
                var completeInventory = await GetCompleteInventoryAsync();
                
                var comparison = new InventoryComparison
                {
                    ComparisonTimestamp = DateTime.UtcNow,
                    GenesysTotal = completeInventory.GenesysInventory.TotalEntities,
                    DynamicsTotal = completeInventory.DynamicsInventory.TotalEntities,
                    Differences = new List<InventoryDifference>()
                };

                // Comparar entidades similares
                comparison.Differences.Add(new InventoryDifference
                {
                    EntityType = "Users/Agents",
                    GenesysCount = completeInventory.GenesysInventory.UsersCount,
                    DynamicsCount = completeInventory.DynamicsInventory.AgentsCount,
                    Difference = completeInventory.GenesysInventory.UsersCount - completeInventory.DynamicsInventory.AgentsCount
                });

                comparison.Differences.Add(new InventoryDifference
                {
                    EntityType = "Flows/Workstreams",
                    GenesysCount = completeInventory.GenesysInventory.FlowsCount,
                    DynamicsCount = completeInventory.DynamicsInventory.WorkstreamsCount,
                    Difference = completeInventory.GenesysInventory.FlowsCount - completeInventory.DynamicsInventory.WorkstreamsCount
                });

                comparison.Differences.Add(new InventoryDifference
                {
                    EntityType = "Bots",
                    GenesysCount = completeInventory.GenesysInventory.BotsCount,
                    DynamicsCount = completeInventory.DynamicsInventory.BotsCount,
                    Difference = completeInventory.GenesysInventory.BotsCount - completeInventory.DynamicsInventory.BotsCount
                });

                comparison.Differences.Add(new InventoryDifference
                {
                    EntityType = "Routing Rules",
                    GenesysCount = completeInventory.GenesysInventory.RoutingRulesCount,
                    DynamicsCount = completeInventory.DynamicsInventory.RoutingRulesCount,
                    Difference = completeInventory.GenesysInventory.RoutingRulesCount - completeInventory.DynamicsInventory.RoutingRulesCount
                });

                comparison.TotalDifference = comparison.GenesysTotal - comparison.DynamicsTotal;
                comparison.MigrationCompleteness = comparison.DynamicsTotal > 0 ? 
                    (double)comparison.DynamicsTotal / comparison.GenesysTotal * 100 : 0;

                return comparison;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao comparar inventários");
                throw;
            }
        }

        public async Task<string> ExportInventoryReportAsync(InventoryReport report, string format = "json")
        {
            try
            {
                switch (format.ToLower())
                {
                    case "json":
                        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                    
                    case "csv":
                        return GenerateCsvReport(report);
                    
                    case "html":
                        return GenerateHtmlReport(report);
                    
                    default:
                        throw new ArgumentException($"Formato não suportado: {format}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao exportar relatório no formato {format}");
                throw;
            }
        }

        private InventorySummary GenerateInventorySummary(GenesysInventory genesys, DynamicsInventory dynamics)
        {
            return new InventorySummary
            {
                TotalGenesysEntities = genesys.TotalEntities,
                TotalDynamicsEntities = dynamics.TotalEntities,
                MigrationGap = genesys.TotalEntities - dynamics.TotalEntities,
                MigrationCompleteness = dynamics.TotalEntities > 0 ? 
                    (double)dynamics.TotalEntities / genesys.TotalEntities * 100 : 0,
                RecommendedActions = GenerateRecommendations(genesys, dynamics)
            };
        }

        private List<string> GenerateRecommendations(GenesysInventory genesys, DynamicsInventory dynamics)
        {
            var recommendations = new List<string>();

            if (genesys.UsersCount > dynamics.AgentsCount)
                recommendations.Add($"Migrar {genesys.UsersCount - dynamics.AgentsCount} usuários restantes");

            if (genesys.FlowsCount > dynamics.WorkstreamsCount)
                recommendations.Add($"Migrar {genesys.FlowsCount - dynamics.WorkstreamsCount} flows restantes");

            if (genesys.BotsCount > dynamics.BotsCount)
                recommendations.Add($"Migrar {genesys.BotsCount - dynamics.BotsCount} bots restantes");

            if (genesys.QueuesCount > 0 && dynamics.WorkstreamsCount == 0)
                recommendations.Add("Configurar workstreams no Dynamics para substituir as filas do Genesys");

            if (recommendations.Count == 0)
                recommendations.Add("Migração aparenta estar completa. Executar validação final.");

            return recommendations;
        }

        private string GenerateCsvReport(InventoryReport report)
        {
            var csv = "Sistema,Entidade,Quantidade\n";
            csv += $"Genesys,Flows,{report.GenesysInventory.FlowsCount}\n";
            csv += $"Genesys,Users,{report.GenesysInventory.UsersCount}\n";
            csv += $"Genesys,Queues,{report.GenesysInventory.QueuesCount}\n";
            csv += $"Genesys,Bots,{report.GenesysInventory.BotsCount}\n";
            csv += $"Genesys,Skills,{report.GenesysInventory.SkillsCount}\n";
            csv += $"Genesys,Routing Rules,{report.GenesysInventory.RoutingRulesCount}\n";
            csv += $"Genesys,Workspaces,{report.GenesysInventory.WorkspacesCount}\n";
            csv += $"Genesys,Divisions,{report.GenesysInventory.DivisionsCount}\n";
            csv += $"Dynamics,Agents,{report.DynamicsInventory.AgentsCount}\n";
            csv += $"Dynamics,Workstreams,{report.DynamicsInventory.WorkstreamsCount}\n";
            csv += $"Dynamics,Bots,{report.DynamicsInventory.BotsCount}\n";
            csv += $"Dynamics,Workflows,{report.DynamicsInventory.WorkflowsCount}\n";
            csv += $"Dynamics,Business Process Flows,{report.DynamicsInventory.BusinessProcessFlowsCount}\n";
            csv += $"Dynamics,Power Virtual Agents,{report.DynamicsInventory.PowerVirtualAgentsCount}\n";
            csv += $"Dynamics,Routing Rules,{report.DynamicsInventory.RoutingRulesCount}\n";
            csv += $"Dynamics,Teams,{report.DynamicsInventory.TeamsCount}\n";
            return csv;
        }

        private string GenerateHtmlReport(InventoryReport report)
        {
            var html = "<!DOCTYPE html>\n" +
                      "<html>\n" +
                      "<head>\n" +
                      "    <title>Relatório de Inventário - Genesys vs Dynamics</title>\n" +
                      "    <style>\n" +
                      "        body { font-family: Arial, sans-serif; margin: 20px; }\n" +
                      "        table { border-collapse: collapse; width: 100%; margin: 20px 0; }\n" +
                      "        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n" +
                      "        th { background-color: #f2f2f2; }\n" +
                      "        .summary { background-color: #e8f4fd; padding: 15px; border-radius: 5px; margin: 20px 0; }\n" +
                      "        .genesys { background-color: #fff3cd; }\n" +
                      "        .dynamics { background-color: #d1ecf1; }\n" +
                      "    </style>\n" +
                      "</head>\n" +
                      "<body>\n" +
                      "    <h1>Relatório de Inventário Completo</h1>\n" +
                      $"    <p><strong>Data/Hora:</strong> {report.Timestamp:yyyy-MM-dd HH:mm:ss} UTC</p>\n" +
                      "    <div class=\"summary\">\n" +
                      "        <h2>Resumo Executivo</h2>\n" +
                      $"        <p><strong>Total Genesys:</strong> {report.Summary.TotalGenesysEntities} entidades</p>\n" +
                      $"        <p><strong>Total Dynamics:</strong> {report.Summary.TotalDynamicsEntities} entidades</p>\n" +
                      $"        <p><strong>Gap de Migração:</strong> {report.Summary.MigrationGap} entidades</p>\n" +
                      $"        <p><strong>Completude da Migração:</strong> {report.Summary.MigrationCompleteness:F1}%</p>\n" +
                      "    </div>\n" +
                      "    <h2>Inventário Detalhado</h2>\n" +
                      "    <table>\n" +
                      "        <tr><th>Sistema</th><th>Tipo de Entidade</th><th>Quantidade</th></tr>\n" +
                      $"        <tr class=\"genesys\"><td>Genesys</td><td>Flows</td><td>{report.GenesysInventory.FlowsCount}</td></tr>\n" +
                      $"        <tr class=\"genesys\"><td>Genesys</td><td>Users</td><td>{report.GenesysInventory.UsersCount}</td></tr>\n" +
                      $"        <tr class=\"genesys\"><td>Genesys</td><td>Queues</td><td>{report.GenesysInventory.QueuesCount}</td></tr>\n" +
                      $"        <tr class=\"genesys\"><td>Genesys</td><td>Bots</td><td>{report.GenesysInventory.BotsCount}</td></tr>\n" +
                      $"        <tr class=\"genesys\"><td>Genesys</td><td>Skills</td><td>{report.GenesysInventory.SkillsCount}</td></tr>\n" +
                      $"        <tr class=\"genesys\"><td>Genesys</td><td>Routing Rules</td><td>{report.GenesysInventory.RoutingRulesCount}</td></tr>\n" +
                      $"        <tr class=\"genesys\"><td>Genesys</td><td>Workspaces</td><td>{report.GenesysInventory.WorkspacesCount}</td></tr>\n" +
                      $"        <tr class=\"genesys\"><td>Genesys</td><td>Divisions</td><td>{report.GenesysInventory.DivisionsCount}</td></tr>\n" +
                      $"        <tr class=\"dynamics\"><td>Dynamics</td><td>Agents</td><td>{report.DynamicsInventory.AgentsCount}</td></tr>\n" +
                      $"        <tr class=\"dynamics\"><td>Dynamics</td><td>Workstreams</td><td>{report.DynamicsInventory.WorkstreamsCount}</td></tr>\n" +
                      $"        <tr class=\"dynamics\"><td>Dynamics</td><td>Bots</td><td>{report.DynamicsInventory.BotsCount}</td></tr>\n" +
                      $"        <tr class=\"dynamics\"><td>Dynamics</td><td>Workflows</td><td>{report.DynamicsInventory.WorkflowsCount}</td></tr>\n" +
                      $"        <tr class=\"dynamics\"><td>Dynamics</td><td>Business Process Flows</td><td>{report.DynamicsInventory.BusinessProcessFlowsCount}</td></tr>\n" +
                      $"        <tr class=\"dynamics\"><td>Dynamics</td><td>Power Virtual Agents</td><td>{report.DynamicsInventory.PowerVirtualAgentsCount}</td></tr>\n" +
                      $"        <tr class=\"dynamics\"><td>Dynamics</td><td>Routing Rules</td><td>{report.DynamicsInventory.RoutingRulesCount}</td></tr>\n" +
                      $"        <tr class=\"dynamics\"><td>Dynamics</td><td>Teams</td><td>{report.DynamicsInventory.TeamsCount}</td></tr>\n" +
                      "    </table>\n" +
                      "    <h2>Recomendações</h2>\n" +
                      "    <ul>\n" +
                      $"        {string.Join("", report.Summary.RecommendedActions.Select(r => $"<li>{r}</li>"))}\n" +
                      "    </ul>\n" +
                      "</body>\n" +
                      "</html>";
            
            return html;
        }
    }

    // Modelos de dados
    public class InventoryReport
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public GenesysInventory GenesysInventory { get; set; }
        public DynamicsInventory DynamicsInventory { get; set; }
        public InventorySummary Summary { get; set; }
    }

    public class GenesysInventory
    {
        public DateTime CollectionTimestamp { get; set; }
        public int FlowsCount { get; set; }
        public int UsersCount { get; set; }
        public int QueuesCount { get; set; }
        public int BotsCount { get; set; }
        public int SkillsCount { get; set; }
        public int RoutingRulesCount { get; set; }
        public int WorkspacesCount { get; set; }
        public int DivisionsCount { get; set; }
        public int TotalEntities { get; set; }
    }

    public class DynamicsInventory
    {
        public DateTime CollectionTimestamp { get; set; }
        public int AgentsCount { get; set; }
        public int WorkstreamsCount { get; set; }
        public int BotsCount { get; set; }
        public int WorkflowsCount { get; set; }
        public int BusinessProcessFlowsCount { get; set; }
        public int PowerVirtualAgentsCount { get; set; }
        public int RoutingRulesCount { get; set; }
        public int TeamsCount { get; set; }
        public int TotalEntities { get; set; }
    }

    public class InventorySummary
    {
        public int TotalGenesysEntities { get; set; }
        public int TotalDynamicsEntities { get; set; }
        public int MigrationGap { get; set; }
        public double MigrationCompleteness { get; set; }
        public List<string> RecommendedActions { get; set; } = new List<string>();
    }

    public class InventoryComparison
    {
        public DateTime ComparisonTimestamp { get; set; }
        public int GenesysTotal { get; set; }
        public int DynamicsTotal { get; set; }
        public int TotalDifference { get; set; }
        public double MigrationCompleteness { get; set; }
        public List<InventoryDifference> Differences { get; set; } = new List<InventoryDifference>();
    }

    public class InventoryDifference
    {
        public string EntityType { get; set; }
        public int GenesysCount { get; set; }
        public int DynamicsCount { get; set; }
        public int Difference { get; set; }
    }
}
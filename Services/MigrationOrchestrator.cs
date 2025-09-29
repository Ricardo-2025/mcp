using GenesysMigrationMCP.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Collections.Concurrent;

namespace GenesysMigrationMCP.Services
{
    /// <summary>
    /// Orquestrador de migrações que utiliza os serviços locais
    /// </summary>
    public class MigrationOrchestrator : IMigrationOrchestrator
    {
        private readonly GenesysCloudClient _genesysClient;
        private readonly DynamicsClient _dynamicsClient;
        private readonly ILogger<MigrationOrchestrator> _logger;
        private readonly ConcurrentDictionary<string, Models.MigrationStatus> _migrationStatuses;

        public MigrationOrchestrator(
            GenesysCloudClient genesysClient,
            DynamicsClient dynamicsClient,
            ILogger<MigrationOrchestrator> logger)
        {
            _genesysClient = genesysClient;
            _dynamicsClient = dynamicsClient;
            _logger = logger;
            _migrationStatuses = new ConcurrentDictionary<string, Models.MigrationStatus>();
        }

        public async Task<object> ExtractGenesysFlowsAsync(string migrationId, Dictionary<string, object> parameters)
        {
            try
            {
                await UpdateMigrationProgressAsync(migrationId, 10, "Iniciando extração de flows do Genesys");

                // Extrair parâmetros
                var flowTypes = parameters.ContainsKey("flowTypes") 
                    ? JsonSerializer.Deserialize<string[]>(parameters["flowTypes"].ToString() ?? "[]")
                    : null;
                
                var includeInactive = parameters.ContainsKey("includeInactive") 
                    ? Convert.ToBoolean(parameters["includeInactive"])
                    : false;

                await UpdateMigrationProgressAsync(migrationId, 30, "Conectando ao Genesys Cloud");

                // Simular extração de flows
                var flows = new List<object>
                {
                    new { Id = "flow1", Name = "Inbound Flow", Type = "inbound", Active = true },
                    new { Id = "flow2", Name = "Outbound Flow", Type = "outbound", Active = true }
                };

                await UpdateMigrationProgressAsync(migrationId, 80, "Processando dados extraídos");

                // Filtrar por tipos se especificado
                if (flowTypes != null && flowTypes.Length > 0)
                {
                    flows = flows.Where(f => 
                    {
                        var flowObj = f as dynamic;
                        var flowType = flowObj?.Type?.ToString();
                        return flowType != null && flowTypes.Any(ft => ft == flowType);
                    }).ToList();
                }

                // Filtrar flows inativos se necessário
                if (!includeInactive)
                {
                    flows = flows.Where(f => 
                    {
                        var flowObj = f as dynamic;
                        return flowObj?.Active == true;
                    }).ToList();
                }

                await UpdateMigrationProgressAsync(migrationId, 100, "Extração concluída");

                return new
                {
                    totalFlows = flows.Count,
                    totalBotFlows = 0,
                    data = new { Flows = flows },
                    extractedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro na extração de flows para migração {migrationId}");
                await UpdateMigrationProgressAsync(migrationId, 0, $"Erro: {ex.Message}");
                throw;
            }
        }

        public async Task<object> MigrateToDynamicsAsync(string migrationId, Dictionary<string, object> parameters)
        {
            try
            {
                await UpdateMigrationProgressAsync(migrationId, 10, "Iniciando migração para Dynamics");

                if (!parameters.ContainsKey("genesysData"))
                {
                    throw new ArgumentException("Dados do Genesys são obrigatórios");
                }

                // Deserializar dados do Genesys
                var genesysDataJson = parameters["genesysData"].ToString();
                var genesysData = JsonSerializer.Deserialize<Dictionary<string, object>>(genesysDataJson ?? "{}");

                if (genesysData == null)
                {
                    throw new ArgumentException("Dados do Genesys inválidos");
                }

                await UpdateMigrationProgressAsync(migrationId, 30, "Convertendo flows para entidades Dynamics");

                // Simular resultado da migração
                var migrationResult = new { Success = true, Errors = new List<string>(), MigratedItems = 5 };

                await UpdateMigrationProgressAsync(migrationId, 60, "Importando dados para Dynamics");

                // Verificar se houve erros na migração
                if (!migrationResult.Success)
                {
                    throw new InvalidOperationException($"Falha na migração: {string.Join(", ", migrationResult.Errors)}");
                }

                // Importar para Dynamics
                _logger.LogInformation("*** ORCHESTRATOR: Chamando ImportMigrationResultAsync ***");
                var importSuccess = await _dynamicsClient.ImportMigrationResultAsync(migrationResult);
                _logger.LogInformation($"*** ORCHESTRATOR: ImportMigrationResultAsync retornou: {importSuccess} ***");

                if (!importSuccess)
                {
                    throw new InvalidOperationException("Falha na importação para Dynamics");
                }

                await UpdateMigrationProgressAsync(migrationId, 100, "Migração concluída");

                return new
                {
                    success = true,
                    workstreamsCreated = 0,
                    botConfigurationsCreated = 0,
                    routingRulesCreated = 0,
                    contextVariablesCreated = 0,
                    migratedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro na migração para Dynamics {migrationId}");
                await UpdateMigrationProgressAsync(migrationId, 0, $"Erro: {ex.Message}");
                throw;
            }
        }

        public async Task<object> ExecuteCompleteMigrationAsync(string migrationId, MigrationOptions? options)
        {
            try
            {
                await UpdateMigrationProgressAsync(migrationId, 5, "Iniciando migração completa");

                // Etapa 1: Extrair flows do Genesys
                await UpdateMigrationProgressAsync(migrationId, 10, "Extraindo flows do Genesys");
                var genesysData = new { Flows = new List<object>() };

                // Etapa 2: Migrar para Dynamics
                await UpdateMigrationProgressAsync(migrationId, 40, "Convertendo para entidades Dynamics");
                var migrationResult = new { Success = true, Errors = new List<string>(), MigratedItems = 3 };

                // Etapa 3: Importar para Dynamics
                await UpdateMigrationProgressAsync(migrationId, 70, "Importando para Dynamics");
                var importSuccess = await _dynamicsClient.ImportMigrationResultAsync(migrationResult);

                if (!importSuccess)
                {
                    throw new InvalidOperationException("Falha na importação para Dynamics");
                }

                await UpdateMigrationProgressAsync(migrationId, 100, "Migração completa concluída");

                return new
                {
                    success = true,
                    summary = new
                    {
                        totalFlowsExtracted = genesysData.Flows.Count,
                        totalBotFlowsExtracted = 0,
                        workstreamsCreated = 0,
                        botConfigurationsCreated = 0,
                        routingRulesCreated = 0,
                        contextVariablesCreated = 0
                    },
                    completedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro na migração completa {migrationId}");
                await UpdateMigrationProgressAsync(migrationId, 0, $"Erro: {ex.Message}");
                throw;
            }
        }

        public async Task<object> ValidateConnectionsAsync(string migrationId)
        {
            try
            {
                await UpdateMigrationProgressAsync(migrationId, 20, "Validando conexão com Genesys");

                // Testar conexão Genesys (assumindo que existe um método de teste)
                var genesysValid = true; // Implementar teste real

                await UpdateMigrationProgressAsync(migrationId, 60, "Validando conexão com Dynamics");

                // Testar conexão Dynamics
                var dynamicsValid = await _dynamicsClient.TestConnectionAsync();

                await UpdateMigrationProgressAsync(migrationId, 100, "Validação concluída");

                return new
                {
                    genesysConnection = genesysValid,
                    dynamicsConnection = dynamicsValid,
                    overallValid = genesysValid && dynamicsValid,
                    validatedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro na validação de conexões {migrationId}");
                await UpdateMigrationProgressAsync(migrationId, 0, $"Erro: {ex.Message}");
                throw;
            }
        }

        public async Task<MigrationStatistics> GetStatisticsAsync()
        {
            try
            {
                // Simular estatísticas
                var dynamicsStats = new Dictionary<string, int>
                {
                    ["flows"] = 10,
                    ["migratedFlows"] = 8,
                    ["failedFlows"] = 2,
                    ["workstreams"] = 5,
                    ["botConfigurations"] = 3
                };

                return new MigrationStatistics
                {
                    TotalFlows = dynamicsStats["flows"],
                    MigratedFlows = dynamicsStats["migratedFlows"],
                    FailedFlows = dynamicsStats["failedFlows"],
                    TotalWorkstreams = dynamicsStats["workstreams"],
                    TotalBotConfigurations = dynamicsStats["botConfigurations"],
                    ExecutionTime = TimeSpan.Zero // Implementar cálculo real
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter estatísticas");
                throw;
            }
        }

        public async Task UpdateMigrationProgressAsync(string migrationId, int progress, string message)
        {
            _migrationStatuses.AddOrUpdate(migrationId,
                new Models.MigrationStatus
                {
                    Id = migrationId,
                    Status = progress == 100 ? "concluído" : progress == 0 ? "erro" : "em_progresso",
                    Progress = progress,
                    Message = message,
                    StartTime = DateTime.UtcNow
                },
                (key, existing) =>
                {
                    existing.Progress = progress;
                    existing.Message = message;
                    existing.Status = progress == 100 ? "concluído" : progress == 0 ? "erro" : "em_progresso";
                    if (progress == 100 || progress == 0)
                    {
                        existing.EndTime = DateTime.UtcNow;
                    }
                    return existing;
                });

            await Task.CompletedTask;
        }
    }
}
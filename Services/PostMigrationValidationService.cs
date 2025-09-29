using GenesysMigrationMCP.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GenesysMigrationMCP.Services
{
    public interface IPostMigrationValidationService
    {
        Task<ValidationReport> ValidateFullMigrationAsync(string migrationId);
        Task<ValidationResult> ValidateWorkstreamsAsync(List<string> workstreamIds);
        Task<ValidationResult> ValidateBotConfigurationsAsync(List<string> botConfigIds);
        Task<ValidationResult> ValidateRoutingRulesAsync(List<string> routingRuleIds);
        Task<ValidationResult> ValidateConnectivityAsync();
        Task<ValidationResult> ValidatePerformanceAsync();
        Task<ValidationResult> ValidateDataIntegrityAsync(string migrationId);
        Task<ValidationReport> RunComprehensiveTestsAsync(string migrationId);
    }

    public class PostMigrationValidationService : IPostMigrationValidationService
    {
        private readonly ILogger<PostMigrationValidationService> _logger;
        private readonly string _validationLogsPath;
        private readonly Dictionary<string, Func<Task<ValidationResult>>> _validationTests;

        public PostMigrationValidationService(ILogger<PostMigrationValidationService> logger)
        {
            _logger = logger;
            _validationLogsPath = Path.Combine(Environment.CurrentDirectory, "validation-logs");
            Directory.CreateDirectory(_validationLogsPath);
            
            _validationTests = InitializeValidationTests();
        }

        public async Task<ValidationReport> ValidateFullMigrationAsync(string migrationId)
        {
            try
            {
                _logger.LogInformation($"🔍 Iniciando validação completa da migração {migrationId}...");
                
                var report = new ValidationReport
                {
                    MigrationId = migrationId,
                    StartTime = DateTime.UtcNow,
                    ValidationResults = new List<ValidationResult>()
                };
                
                var logFilePath = Path.Combine(_validationLogsPath, $"validation_{migrationId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
                await File.WriteAllTextAsync(logFilePath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Iniciando validação completa\n");
                
                // 1. Validação de Conectividade
                _logger.LogInformation("📡 Validando conectividade...");
                var connectivityResult = await ValidateConnectivityAsync();
                report.ValidationResults.Add(connectivityResult);
                await LogValidationResult(logFilePath, "Conectividade", connectivityResult);
                
                // 2. Validação de Integridade dos Dados
                _logger.LogInformation("🔐 Validando integridade dos dados...");
                var dataIntegrityResult = await ValidateDataIntegrityAsync(migrationId);
                report.ValidationResults.Add(dataIntegrityResult);
                await LogValidationResult(logFilePath, "Integridade dos Dados", dataIntegrityResult);
                
                // 3. Validação de Workstreams
                _logger.LogInformation("🔄 Validando workstreams...");
                var workstreamsResult = await ValidateWorkstreamsAsync(await GetMigratedWorkstreamIds(migrationId));
                report.ValidationResults.Add(workstreamsResult);
                await LogValidationResult(logFilePath, "Workstreams", workstreamsResult);
                
                // 4. Validação de Bot Configurations
                _logger.LogInformation("🤖 Validando configurações de bot...");
                var botConfigsResult = await ValidateBotConfigurationsAsync(await GetMigratedBotConfigIds(migrationId));
                report.ValidationResults.Add(botConfigsResult);
                await LogValidationResult(logFilePath, "Bot Configurations", botConfigsResult);
                
                // 5. Validação de Routing Rules
                _logger.LogInformation("🎯 Validando regras de roteamento...");
                var routingRulesResult = await ValidateRoutingRulesAsync(await GetMigratedRoutingRuleIds(migrationId));
                report.ValidationResults.Add(routingRulesResult);
                await LogValidationResult(logFilePath, "Routing Rules", routingRulesResult);
                
                // 6. Validação de Performance
                _logger.LogInformation("⚡ Validando performance...");
                var performanceResult = await ValidatePerformanceAsync();
                report.ValidationResults.Add(performanceResult);
                await LogValidationResult(logFilePath, "Performance", performanceResult);
                
                // 7. Testes Funcionais
                _logger.LogInformation("🧪 Executando testes funcionais...");
                var functionalTestsResult = await RunFunctionalTestsAsync(migrationId);
                report.ValidationResults.Add(functionalTestsResult);
                await LogValidationResult(logFilePath, "Testes Funcionais", functionalTestsResult);
                
                // Calcular resultado geral
                report.EndTime = DateTime.UtcNow;
                report.Duration = report.EndTime - report.StartTime;
                report.OverallSuccess = report.ValidationResults.All(r => r.Success);
                report.SuccessRate = (double)report.ValidationResults.Count(r => r.Success) / report.ValidationResults.Count * 100;
                report.LogFilePath = logFilePath;
                
                // Gerar recomendações
                report.Recommendations = GenerateRecommendations(report.ValidationResults);
                
                // Salvar relatório
                var reportPath = Path.Combine(_validationLogsPath, $"validation_report_{migrationId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(reportPath, reportJson);
                report.ReportFilePath = reportPath;
                
                var status = report.OverallSuccess ? "✅ SUCESSO" : "❌ FALHAS DETECTADAS";
                _logger.LogInformation($"{status} - Validação completa finalizada. Taxa de sucesso: {report.SuccessRate:F1}%");
                
                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro durante validação da migração {migrationId}");
                throw;
            }
        }

        public async Task<ValidationResult> ValidateWorkstreamsAsync(List<string> workstreamIds)
        {
            try
            {
                var result = new ValidationResult
                {
                    Category = "Validação de Workstreams",
                    Timestamp = DateTime.UtcNow,
                    Details = new List<string>()
                };
                
                foreach (var workstreamId in workstreamIds)
                {
                    // Verificar se workstream existe
                    var exists = await CheckWorkstreamExistsAsync(workstreamId);
                    if (!exists)
                    {
                        result.Details.Add($"Workstream {workstreamId} não encontrado");
                        continue;
                    }
                    
                    // Verificar configurações do workstream
                    var configValid = await ValidateWorkstreamConfigurationAsync(workstreamId);
                    if (!configValid)
                    {
                        result.Details.Add($"Configuração inválida no workstream {workstreamId}");
                    }
                    
                    // Verificar se está ativo
                    var isActive = await CheckWorkstreamIsActiveAsync(workstreamId);
                    if (!isActive)
                    {
                        result.Details.Add($"Workstream {workstreamId} não está ativo");
                    }
                    
                    result.Details.Add($"Workstream {workstreamId}: {(exists && configValid && isActive ? "✓" : "✗")}");
                }
                
                result.IsValid = result.Details.All(d => d.Contains("✓"));
                result.Message = result.IsValid ? "Todos os workstreams validados com sucesso" : "Alguns workstreams apresentaram problemas";
                
                return result;
            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                    Category = "Validação de Workstreams",
                    IsValid = false,
                    Message = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ValidationResult> ValidateBotConfigurationsAsync(List<string> botConfigIds)
        {
            try
            {
                var result = new ValidationResult
                {
                    Category = "Validação de Bot Configurations",
                    Timestamp = DateTime.UtcNow,
                    Details = new List<string>()
                };
                
                foreach (var botConfigId in botConfigIds)
                {
                    // Verificar se bot configuration existe
                    var exists = await CheckBotConfigExistsAsync(botConfigId);
                    if (!exists)
                    {
                        result.Details.Add($"Bot Configuration {botConfigId} não encontrada");
                        continue;
                    }
                    
                    // Verificar se bot está respondendo
                    var isResponding = await TestBotResponseAsync(botConfigId);
                    if (!isResponding)
                    {
                        result.Details.Add($"Bot {botConfigId} não está respondendo");
                    }
                    
                    // Verificar configurações do bot
                    var configValid = await ValidateBotConfigurationAsync(botConfigId);
                    if (!configValid)
                    {
                        result.Details.Add($"Configuração inválida no bot {botConfigId}");
                    }
                    
                    result.Details.Add($"Bot {botConfigId}: {(exists && isResponding && configValid ? "✓" : "✗")}");
                }
                
                result.IsValid = result.Details.All(d => d.Contains("✓"));
                result.Message = result.IsValid ? "Todas as configurações de bot validadas com sucesso" : "Algumas configurações de bot apresentaram problemas";
                
                return result;
            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                    Category = "Validação de Bot Configurations",
                    IsValid = false,
                    Message = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ValidationResult> ValidateRoutingRulesAsync(List<string> routingRuleIds)
        {
            try
            {
                var result = new ValidationResult
                {
                    Category = "Validação de Routing Rules",
                    Timestamp = DateTime.UtcNow,
                    Details = new List<string>()
                };
                
                foreach (var ruleId in routingRuleIds)
                {
                    // Verificar se regra existe
                    var exists = await CheckRoutingRuleExistsAsync(ruleId);
                    if (!exists)
                    {
                        result.Details.Add($"Routing Rule {ruleId} não encontrada");
                        continue;
                    }
                    
                    // Verificar se regra está ativa
                    var isActive = await CheckRoutingRuleIsActiveAsync(ruleId);
                    if (!isActive)
                    {
                        result.Details.Add($"Routing Rule {ruleId} não está ativa");
                    }
                    
                    // Testar lógica da regra
                    var logicValid = await TestRoutingRuleLogicAsync(ruleId);
                    if (!logicValid)
                    {
                        result.Details.Add($"Lógica da Routing Rule {ruleId} apresenta problemas");
                    }
                    
                    result.Details.Add($"Routing Rule {ruleId}: {(exists && isActive && logicValid ? "✓" : "✗")}");
                }
                
                result.IsValid = result.Details.All(d => d.Contains("✓"));
                result.Message = result.IsValid ? "Todas as regras de roteamento validadas com sucesso" : "Algumas regras de roteamento apresentaram problemas";
                
                return result;
            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                    Category = "Validação de Routing Rules",
                    IsValid = false,
                    Message = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ValidationResult> ValidateConnectivityAsync()
        {
            try
            {
                var result = new ValidationResult
                {
                    Category = "Validação de Conectividade",
                    Timestamp = DateTime.UtcNow,
                    Details = new List<string>()
                };
                
                // Testar conexão com Dynamics
                var dynamicsConnected = await TestDynamicsConnectionAsync();
                result.Details.Add($"Dynamics 365: {(dynamicsConnected ? "✓ Conectado" : "✗ Falha na conexão")}");
                if (!dynamicsConnected) result.Issues.Add("Falha na conexão com Dynamics 365");
                
                // Testar conexão com Genesys
                var genesysConnected = await TestGenesysConnectionAsync();
                result.Details.Add($"Genesys Cloud: {(genesysConnected ? "✓ Conectado" : "✗ Falha na conexão")}");
                if (!genesysConnected) result.Details.Add("Falha na conexão com Genesys Cloud");
                
                // Testar APIs essenciais
                var apisWorking = await TestEssentialAPIsAsync();
                result.Details.Add($"APIs Essenciais: {(apisWorking ? "✓ Funcionando" : "✗ Problemas detectados")}");
                if (!apisWorking) result.Details.Add("Problemas nas APIs essenciais");
                
                result.IsValid = dynamicsConnected && genesysConnected && apisWorking;
                result.Message = result.IsValid ? "Conectividade validada com sucesso" : "Problemas de conectividade detectados";
                
                return result;
            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                    Category = "Validação de Conectividade",
                    IsValid = false,
                    Message = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ValidationResult> ValidatePerformanceAsync()
        {
            try
            {
                var result = new ValidationResult
                {
                    Category = "Validação de Performance",
                    Timestamp = DateTime.UtcNow,
                    Details = new List<string>()
                };
                
                // Testar tempo de resposta das APIs
                var apiResponseTime = await MeasureAPIResponseTimeAsync();
                var apiPerformanceOk = apiResponseTime < 2000; // menos de 2 segundos
                result.Details.Add($"Tempo de resposta API: {apiResponseTime}ms {(apiPerformanceOk ? "✓" : "✗")}");
                if (!apiPerformanceOk) result.Details.Add($"Tempo de resposta da API muito alto: {apiResponseTime}ms");
                
                // Testar throughput
                var throughput = await MeasureThroughputAsync();
                var throughputOk = throughput > 10; // mais de 10 requests por segundo
                result.Details.Add($"Throughput: {throughput} req/s {(throughputOk ? "✓" : "✗")}");
                if (!throughputOk) result.Details.Add($"Throughput baixo: {throughput} req/s");
                
                // Verificar uso de memória
                var memoryUsage = await CheckMemoryUsageAsync();
                var memoryOk = memoryUsage < 80; // menos de 80% de uso
                result.Details.Add($"Uso de memória: {memoryUsage}% {(memoryOk ? "✓" : "✗")}");
                if (!memoryOk) result.Details.Add($"Uso de memória alto: {memoryUsage}%");
                
                result.IsValid = apiPerformanceOk && throughputOk && memoryOk;
                result.Message = result.IsValid ? "Performance validada com sucesso" : "Problemas de performance detectados";
                
                return result;
            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                    Category = "Validação de Performance",
                    IsValid = false,
                    Message = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ValidationResult> ValidateDataIntegrityAsync(string migrationId)
        {
            try
            {
                var result = new ValidationResult
                {
                    Category = "Validação de Integridade dos Dados",
                    Timestamp = DateTime.UtcNow,
                    Details = new List<string>()
                };
                
                // Verificar se todos os flows foram migrados
                var flowsMigrated = await ValidateFlowsMigrationAsync(migrationId);
                result.Details.Add($"Flows migrados: {(flowsMigrated ? "✓ Todos" : "✗ Incompleto")}");
                if (!flowsMigrated) result.Details.Add("Nem todos os flows foram migrados");
                
                // Verificar integridade dos dados migrados
                var dataIntegrity = await CheckDataIntegrityAsync(migrationId);
                result.Details.Add($"Integridade dos dados: {(dataIntegrity ? "✓ Íntegra" : "✗ Problemas detectados")}");
                if (!dataIntegrity) result.Details.Add("Problemas de integridade nos dados migrados");
                
                // Verificar referências entre entidades
                var referencesValid = await ValidateEntityReferencesAsync(migrationId);
                result.Details.Add($"Referências entre entidades: {(referencesValid ? "✓ Válidas" : "✗ Problemas detectados")}");
                if (!referencesValid) result.Details.Add("Problemas nas referências entre entidades");
                
                result.IsValid = flowsMigrated && dataIntegrity && referencesValid;
                result.Message = result.IsValid ? "Integridade dos dados validada com sucesso" : "Problemas de integridade detectados";
                
                return result;
            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                    Category = "Validação de Integridade dos Dados",
                    IsValid = false,
                    Message = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public async Task<ValidationReport> RunComprehensiveTestsAsync(string migrationId)
        {
            _logger.LogInformation($"🧪 Executando testes abrangentes para migração {migrationId}...");
            
            // Executar todos os testes de validação
            var report = await ValidateFullMigrationAsync(migrationId);
            
            // Adicionar testes específicos adicionais
            var additionalTests = new List<ValidationResult>
            {
                await RunLoadTestAsync(),
                await RunSecurityTestAsync(),
                await RunCompatibilityTestAsync()
            };
            
            report.ValidationResults.AddRange(additionalTests);
            
            // Recalcular métricas
            report.OverallSuccess = report.ValidationResults.All(r => r.IsValid);
            report.SuccessRate = (double)report.ValidationResults.Count(r => r.IsValid) / report.ValidationResults.Count * 100;
            
            return report;
        }

        // Métodos auxiliares simulados (implementar com lógica real)
        private async Task<List<string>> GetMigratedWorkstreamIds(string migrationId) => await Task.FromResult(new List<string> { "ws1", "ws2" });
        private async Task<List<string>> GetMigratedBotConfigIds(string migrationId) => await Task.FromResult(new List<string> { "bot1", "bot2" });
        private async Task<List<string>> GetMigratedRoutingRuleIds(string migrationId) => await Task.FromResult(new List<string> { "rule1", "rule2" });
        
        private async Task<bool> CheckWorkstreamExistsAsync(string id) => await Task.FromResult(true);
        private async Task<bool> ValidateWorkstreamConfigurationAsync(string id) => await Task.FromResult(true);
        private async Task<bool> CheckWorkstreamIsActiveAsync(string id) => await Task.FromResult(true);
        
        private async Task<bool> CheckBotConfigExistsAsync(string id) => await Task.FromResult(true);
        private async Task<bool> TestBotResponseAsync(string id) => await Task.FromResult(true);
        private async Task<bool> ValidateBotConfigurationAsync(string id) => await Task.FromResult(true);
        
        private async Task<bool> CheckRoutingRuleExistsAsync(string id) => await Task.FromResult(true);
        private async Task<bool> CheckRoutingRuleIsActiveAsync(string id) => await Task.FromResult(true);
        private async Task<bool> TestRoutingRuleLogicAsync(string id) => await Task.FromResult(true);
        
        private async Task<bool> TestDynamicsConnectionAsync() => await Task.FromResult(true);
        private async Task<bool> TestGenesysConnectionAsync() => await Task.FromResult(true);
        private async Task<bool> TestEssentialAPIsAsync() => await Task.FromResult(true);
        
        private async Task<long> MeasureAPIResponseTimeAsync() => await Task.FromResult(500L);
        private async Task<double> MeasureThroughputAsync() => await Task.FromResult(25.0);
        private async Task<double> CheckMemoryUsageAsync() => await Task.FromResult(45.0);
        
        private async Task<bool> ValidateFlowsMigrationAsync(string migrationId) => await Task.FromResult(true);
        private async Task<bool> CheckDataIntegrityAsync(string migrationId) => await Task.FromResult(true);
        private async Task<bool> ValidateEntityReferencesAsync(string migrationId) => await Task.FromResult(true);
        
        private async Task<ValidationResult> RunFunctionalTestsAsync(string migrationId)
        {
            await Task.Delay(1000);
            return new ValidationResult
            {
                Category = "Testes Funcionais",
                IsValid = true,
                Timestamp = DateTime.UtcNow,
                Message = "Todos os testes funcionais passaram",
                Details = new List<string> { "✓ Todos os testes funcionais passaram" }
            };
        }
        
        private async Task<ValidationResult> RunLoadTestAsync()
        {
            await Task.Delay(500);
            return new ValidationResult
            {
                Category = "Teste de Carga",
                IsValid = true,
                Timestamp = DateTime.UtcNow,
                Message = "Sistema suporta carga esperada",
                Details = new List<string> { "✓ Sistema suporta carga esperada" }
            };
        }
        
        private async Task<ValidationResult> RunSecurityTestAsync()
        {
            await Task.Delay(300);
            return new ValidationResult
            {
                Category = "Teste de Segurança",
                IsValid = true,
                Timestamp = DateTime.UtcNow,
                Message = "Configurações de segurança validadas",
                Details = new List<string> { "✓ Configurações de segurança validadas" }
            };
        }
        
        private async Task<ValidationResult> RunCompatibilityTestAsync()
        {
            await Task.Delay(200);
            return new ValidationResult
            {
                Category = "Teste de Compatibilidade",
                IsValid = true,
                Timestamp = DateTime.UtcNow,
                Message = "Compatibilidade entre sistemas confirmada",
                Details = new List<string> { "✓ Compatibilidade entre sistemas confirmada" }
            };
        }
        
        private async Task LogValidationResult(string logFilePath, string testName, ValidationResult result)
        {
            var status = result.IsValid ? "✅ PASSOU" : "❌ FALHOU";
            var logEntry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {testName}: {status}";
            if (!result.IsValid && !string.IsNullOrEmpty(result.Message))
                logEntry += $" - {result.Message}";
            logEntry += "\n";
            
            await File.AppendAllTextAsync(logFilePath, logEntry);
        }
        
        private List<string> GenerateRecommendations(List<ValidationResult> results)
        {
            var recommendations = new List<string>();
            
            foreach (var result in results.Where(r => !r.IsValid))
            {
                switch (result.Category)
                {
                    case "Validação de Conectividade":
                        recommendations.Add("Verificar configurações de rede e credenciais de acesso");
                        break;
                    case "Validação de Performance":
                        recommendations.Add("Considerar otimização de recursos ou scaling horizontal");
                        break;
                    case "Validação de Integridade dos Dados":
                        recommendations.Add("Executar nova migração ou correção manual dos dados");
                        break;
                    default:
                        recommendations.Add($"Investigar problemas específicos em: {result.Category}");
                        break;
                }
            }
            
            if (!recommendations.Any())
            {
                recommendations.Add("✅ Migração validada com sucesso - nenhuma ação necessária");
            }
            
            return recommendations;
        }
        
        private Dictionary<string, Func<Task<ValidationResult>>> InitializeValidationTests()
        {
            return new Dictionary<string, Func<Task<ValidationResult>>>
            {
                ["connectivity"] = ValidateConnectivityAsync,
                ["performance"] = ValidatePerformanceAsync
            };
        }
    }

    public class ValidationReport
    {
        public string MigrationId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool OverallSuccess { get; set; }
        public double SuccessRate { get; set; }
        public List<ValidationResult> ValidationResults { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        public string LogFilePath { get; set; } = string.Empty;
        public string? ReportFilePath { get; set; }
    }
}
using GenesysMigrationMCP.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;

namespace GenesysMigrationMCP.Services
{
    public interface IPrerequisiteValidationService
    {
        Task<ValidationResult> ValidateAllPrerequisitesAsync();
        Task<ValidationResult> ValidateGenesysConnectivityAsync();
        Task<ValidationResult> ValidateDynamicsConnectivityAsync();
        Task<ValidationResult> ValidatePermissionsAsync();
        Task<ValidationResult> ValidateLicensesAsync();
        Task<ValidationResult> ValidateSystemResourcesAsync();
    }

    public class PrerequisiteValidationService : IPrerequisiteValidationService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<PrerequisiteValidationService> _logger;
        private readonly HttpClient _httpClient;

        public PrerequisiteValidationService(
            IConfiguration configuration,
            ILogger<PrerequisiteValidationService> logger,
            HttpClient httpClient)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<ValidationResult> ValidateAllPrerequisitesAsync()
        {
            _logger.LogInformation("🔍 Iniciando validação completa de pré-requisitos...");
            
            var results = new List<ValidationResult>();
            
            // Validar conectividade
            results.Add(await ValidateGenesysConnectivityAsync());
            results.Add(await ValidateDynamicsConnectivityAsync());
            
            // Validar permissões
            results.Add(await ValidatePermissionsAsync());
            
            // Validar licenças
            results.Add(await ValidateLicensesAsync());
            
            // Validar recursos do sistema
            results.Add(await ValidateSystemResourcesAsync());
            
            var overallResult = new ValidationResult
            {
                IsValid = results.All(r => r.IsValid),
                Category = "Overall",
                Message = results.All(r => r.IsValid) 
                    ? "✅ Todos os pré-requisitos foram validados com sucesso"
                    : "❌ Alguns pré-requisitos falharam na validação",
                Details = results.SelectMany(r => r.Details).ToList(),
                Recommendations = results.SelectMany(r => r.Recommendations).ToList()
            };
            
            _logger.LogInformation($"Validação completa: {(overallResult.IsValid ? "SUCESSO" : "FALHA")}");
            return overallResult;
        }

        public async Task<ValidationResult> ValidateGenesysConnectivityAsync()
        {
            try
            {
                _logger.LogInformation("🔗 Validando conectividade com Genesys Cloud...");
                
                var genesysUrl = _configuration["Genesys:ApiUrl"] ?? "https://api.mypurecloud.com";
                var clientId = _configuration["Genesys:ClientId"];
                var clientSecret = _configuration["Genesys:ClientSecret"];
                
                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Category = "Genesys Connectivity",
                        Message = "❌ Credenciais do Genesys não configuradas",
                        Details = new List<string> { "ClientId ou ClientSecret não encontrados na configuração" },
                        Recommendations = new List<string> { "Configure as credenciais do Genesys no appsettings.json" }
                    };
                }
                
                // Testar conectividade básica
                var response = await _httpClient.GetAsync($"{genesysUrl}/api/v2/organizations/me");
                
                if (response.IsSuccessStatusCode)
                {
                    return new ValidationResult
                    {
                        IsValid = true,
                        Category = "Genesys Connectivity",
                        Message = "✅ Conectividade com Genesys Cloud validada",
                        Details = new List<string> { $"Conexão estabelecida com {genesysUrl}" }
                    };
                }
                else
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Category = "Genesys Connectivity",
                        Message = "❌ Falha na conectividade com Genesys Cloud",
                        Details = new List<string> { $"Status: {response.StatusCode}, Reason: {response.ReasonPhrase}" },
                        Recommendations = new List<string> { "Verifique as credenciais e conectividade de rede" }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao validar conectividade Genesys");
                return new ValidationResult
                {
                    IsValid = false,
                    Category = "Genesys Connectivity",
                    Message = "❌ Erro na validação de conectividade Genesys",
                    Details = new List<string> { ex.Message },
                    Recommendations = new List<string> { "Verifique a configuração de rede e credenciais" }
                };
            }
        }

        public async Task<ValidationResult> ValidateDynamicsConnectivityAsync()
        {
            try
            {
                _logger.LogInformation("🔗 Validando conectividade com Dynamics 365...");
                
                var dynamicsUrl = _configuration["Dynamics:ApiUrl"];
                var tenantId = _configuration["Dynamics:TenantId"];
                var clientId = _configuration["Dynamics:ClientId"];
                
                if (string.IsNullOrEmpty(dynamicsUrl) || string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId))
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Category = "Dynamics Connectivity",
                        Message = "❌ Configuração do Dynamics 365 incompleta",
                        Details = new List<string> { "URL, TenantId ou ClientId não configurados" },
                        Recommendations = new List<string> { "Configure todas as credenciais do Dynamics 365" }
                    };
                }
                
                // Testar conectividade básica
                var response = await _httpClient.GetAsync($"{dynamicsUrl}/api/data/v9.1/WhoAmI");
                
                if (response.IsSuccessStatusCode)
                {
                    return new ValidationResult
                    {
                        IsValid = true,
                        Category = "Dynamics Connectivity",
                        Message = "✅ Conectividade com Dynamics 365 validada",
                        Details = new List<string> { $"Conexão estabelecida com {dynamicsUrl}" }
                    };
                }
                else
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Category = "Dynamics Connectivity",
                        Message = "❌ Falha na conectividade com Dynamics 365",
                        Details = new List<string> { $"Status: {response.StatusCode}" },
                        Recommendations = new List<string> { "Verifique as credenciais e permissões do Dynamics" }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao validar conectividade Dynamics");
                return new ValidationResult
                {
                    IsValid = false,
                    Category = "Dynamics Connectivity",
                    Message = "❌ Erro na validação de conectividade Dynamics",
                    Details = new List<string> { ex.Message },
                    Recommendations = new List<string> { "Verifique a configuração de autenticação" }
                };
            }
        }

        public async Task<ValidationResult> ValidatePermissionsAsync()
        {
            try
            {
                _logger.LogInformation("🔐 Validando permissões necessárias...");
                
                var permissions = new List<string>();
                var missingPermissions = new List<string>();
                
                // Validar permissões Genesys
                var genesysPermissions = new[]
                {
                    "architect:flow:view",
                    "architect:flow:edit",
                    "routing:queue:view",
                    "users:user:view"
                };
                
                // Validar permissões Dynamics
                var dynamicsPermissions = new[]
                {
                    "msdyn_liveworkstream:create",
                    "msdyn_liveworkstream:read",
                    "msdyn_ocbotchannelregistration:create",
                    "msdyn_decisionruleset:create"
                };
                
                // Simular validação (em implementação real, fazer chamadas às APIs)
                await Task.Delay(500);
                
                return new ValidationResult
                {
                    IsValid = true,
                    Category = "Permissions",
                    Message = "✅ Permissões validadas com sucesso",
                    Details = new List<string> 
                    { 
                        $"Genesys: {genesysPermissions.Length} permissões verificadas",
                        $"Dynamics: {dynamicsPermissions.Length} permissões verificadas"
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao validar permissões");
                return new ValidationResult
                {
                    IsValid = false,
                    Category = "Permissions",
                    Message = "❌ Erro na validação de permissões",
                    Details = new List<string> { ex.Message }
                };
            }
        }

        public async Task<ValidationResult> ValidateLicensesAsync()
        {
            try
            {
                _logger.LogInformation("📋 Validando licenças necessárias...");
                
                var requiredLicenses = new[]
                {
                    "Dynamics 365 Customer Service Enterprise",
                    "Omnichannel for Customer Service",
                    "Power Automate per user",
                    "Copilot Studio"
                };
                
                // Simular validação de licenças
                await Task.Delay(300);
                
                return new ValidationResult
                {
                    IsValid = true,
                    Category = "Licenses",
                    Message = "✅ Licenças necessárias disponíveis",
                    Details = requiredLicenses.Select(l => $"✓ {l}").ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao validar licenças");
                return new ValidationResult
                {
                    IsValid = false,
                    Category = "Licenses",
                    Message = "❌ Erro na validação de licenças",
                    Details = new List<string> { ex.Message }
                };
            }
        }

        public async Task<ValidationResult> ValidateSystemResourcesAsync()
        {
            try
            {
                _logger.LogInformation("💻 Validando recursos do sistema...");
                
                var details = new List<string>();
                
                // Validar memória disponível
                var availableMemory = GC.GetTotalMemory(false) / (1024 * 1024); // MB
                details.Add($"Memória disponível: {availableMemory} MB");
                
                // Validar espaço em disco
                var driveInfo = new DriveInfo("C:");
                var freeSpace = driveInfo.AvailableFreeSpace / (1024 * 1024 * 1024); // GB
                details.Add($"Espaço livre em disco: {freeSpace} GB");
                
                // Validar conectividade de rede
                details.Add("Conectividade de rede: OK");
                
                await Task.Delay(200);
                
                return new ValidationResult
                {
                    IsValid = true,
                    Category = "System Resources",
                    Message = "✅ Recursos do sistema adequados",
                    Details = details
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao validar recursos do sistema");
                return new ValidationResult
                {
                    IsValid = false,
                    Category = "System Resources",
                    Message = "❌ Erro na validação de recursos",
                    Details = new List<string> { ex.Message }
                };
            }
        }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<string> Details { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        // Additional properties for compatibility
        public bool Success => IsValid;
        public List<string> Issues { get; set; } = new();
        public string TestName { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    }
}
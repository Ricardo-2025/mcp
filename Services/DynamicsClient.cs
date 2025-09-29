using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Newtonsoft.Json;
using GenesysMigrationMCP.Models;

namespace GenesysMigrationMCP.Services
{
    public class DynamicsClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DynamicsClient> _logger;
        private string? _accessToken;
        private DateTime _tokenExpiration;
        private readonly string _tenantId;
        private readonly string _clientId;
        private readonly string _certificateThumbprint;
        private readonly string _certificatePath;
        private readonly string _certificatePassword;
        private readonly string _baseUrl;
        private readonly int _tokenExpirationBuffer;
        private IConfidentialClientApplication? _app;

        public DynamicsClient(HttpClient httpClient, IConfiguration configuration, ILogger<DynamicsClient> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;

            try
            {
                //_logger.LogInformation("Iniciando carregamento das configurações do Dynamics...");


                //_tenantId = _configuration["DynamicsTenantId"] ?? _configuration["Values:DynamicsTenantId"] ?? "d2f14a40-7f24-475d-b6b3-67ea354c63fc";
                //_clientId = _configuration["DynamicsClientId"] ?? _configuration["Values:DynamicsClientId"] ?? "a88a2983-f7ed-4376-8708-b87812c21133";
                //_certificateThumbprint = _configuration["DynamicsCertificateThumbprint"] ?? _configuration["Values:DynamicsCertificateThumbprint"] ?? "35EC1CE94E02B31C222C8B5820FBAAE2AD825AA5";
                //_baseUrl = _configuration["DynamicsBaseUrl"] ?? _configuration["Values:DynamicsBaseUrl"] ?? "https://orga884c2fa.crm.dynamics.com";

                //var relativePath = _configuration["DynamicsCertificatePath"] ?? _configuration["Values:DynamicsCertificatePath"] ?? "certificates/GenesysMigrationApp.pfx";
                //_certificatePassword = _configuration["DynamicsCertificatePassword"] ?? _configuration["Values:DynamicsCertificatePassword"] ?? "SuaSenhaSegura123!";

                //_logger.LogInformation($"Configurações lidas - TenantId: {(!string.IsNullOrEmpty(_tenantId) ? "CONFIGURADO" : "VAZIO")}");
                //_logger.LogInformation($"Configurações lidas - ClientId: {(!string.IsNullOrEmpty(_clientId) ? "CONFIGURADO" : "VAZIO")}");
                //_logger.LogInformation($"Configurações lidas - CertificateThumbprint: {(!string.IsNullOrEmpty(_certificateThumbprint) ? "CONFIGURADO" : "VAZIO")}");
                //_logger.LogInformation($"Configurações lidas - CertificatePath: {(!string.IsNullOrEmpty(relativePath) ? relativePath : "VAZIO")}");

                //// Resolver caminho do certificado
                //if (!string.IsNullOrEmpty(relativePath))
                //{
                //    if (Path.IsPathRooted(relativePath))
                //    {
                //        _certificatePath = relativePath;
                //    }
                //    else
                //    {
                //        // Usar o diretório base da aplicação para resolver caminhos relativos
                //        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                //        _certificatePath = Path.Combine(baseDirectory, relativePath);
                //    }
                //}
                //else
                //{
                //    _certificatePath = "";
                //}

                //_certificatePassword = _configuration["DynamicsCertificatePassword"] ?? _configuration["Values:DynamicsCertificatePassword"] ?? "";
                //_baseUrl = _configuration["DynamicsBaseUrl"] ?? _configuration["Values:DynamicsBaseUrl"] ?? "";
                //_tokenExpirationBuffer = 300;

                //_logger.LogInformation($"Configurações carregadas - CertificatePath: {_certificatePath}, CertificateThumbprint: {_certificateThumbprint}");
                //_logger.LogInformation($"Certificado existe no caminho: {(!string.IsNullOrEmpty(_certificatePath) ? File.Exists(_certificatePath).ToString() : "N/A")}");

                //if (string.IsNullOrEmpty(_tenantId) || string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_baseUrl))
                //{
                //    _logger.LogWarning("DynamicsClient: Configurações obrigatórias ausentes. Cliente será criado mas não funcionará até configuração completa.");
                //}
                //else
                //{
                //    _logger.LogInformation($"DynamicsClient configurado - TenantId: {_tenantId}, ClientId: {_clientId}, BaseUrl: {_baseUrl}");
                //    InitializeConfidentialClient();
                //    ConfigureHttpClient();
                //}


                _logger.LogInformation("Iniciando carregamento das configurações do Dynamics...");

                // Lê pelos formatos Dynamics:Key, Dynamics__Key, DynamicsKey e Values:*
                _tenantId = GetCfg("TenantId");
                _clientId = GetCfg("ClientId");
                _certificateThumbprint = GetCfg("CertificateThumbprint");

                // BaseUrl com fallback para OrganizationUrl
                _baseUrl = GetCfg("BaseUrl");
                if (string.IsNullOrWhiteSpace(_baseUrl))
                    _baseUrl = GetCfg("OrganizationUrl");

                // Certificado: path e senha (NÃO sobrescrever depois!)
                var relativePath = GetCfg("CertificatePath");
                _certificatePassword = GetCfg("CertificatePassword");

                _logger.LogInformation($"Configurações lidas - TenantId: {(string.IsNullOrEmpty(_tenantId) ? "VAZIO" : "CONFIGURADO")}");
                _logger.LogInformation($"Configurações lidas - ClientId: {(string.IsNullOrEmpty(_clientId) ? "VAZIO" : "CONFIGURADO")}");
                _logger.LogInformation($"Configurações lidas - CertificateThumbprint: {(string.IsNullOrEmpty(_certificateThumbprint) ? "VAZIO" : "CONFIGURADO")}");
                _logger.LogInformation($"Configurações lidas - CertificatePath: {(string.IsNullOrEmpty(relativePath) ? "VAZIO" : relativePath)}");
                _logger.LogInformation($"Configurações lidas - BaseUrl/OrganizationUrl: {(string.IsNullOrEmpty(_baseUrl) ? "VAZIO" : _baseUrl)}");

                // Resolver caminho do certificado (mantendo relativo ao AppDirectory)
                if (!string.IsNullOrEmpty(relativePath))
                {
                    _certificatePath = Path.IsPathRooted(relativePath)
                        ? relativePath
                        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
                }
                else
                {
                    _certificatePath = "";
                }

                // Buffer de expiração do token
                _tokenExpirationBuffer = 300;

                _logger.LogInformation($"Configurações carregadas - CertificatePath: {_certificatePath}, CertificateThumbprint: {_certificateThumbprint}");
                _logger.LogInformation($"Certificado existe no caminho: {(!string.IsNullOrEmpty(_certificatePath) ? File.Exists(_certificatePath).ToString() : "N/A")}");

                // Validação com log explícito do que falta
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(_tenantId)) missing.Add("TenantId");
                if (string.IsNullOrWhiteSpace(_clientId)) missing.Add("ClientId");
                if (string.IsNullOrWhiteSpace(_baseUrl)) missing.Add("BaseUrl/OrganizationUrl");

                if (missing.Count > 0)
                {
                    _logger.LogWarning("DynamicsClient: campos ausentes -> {Missing}", string.Join(", ", missing));
                }
                else
                {
                    _logger.LogInformation($"DynamicsClient configurado - TenantId: {_tenantId}, ClientId: {_clientId}, BaseUrl: {_baseUrl}");
                    InitializeConfidentialClient();
                    ConfigureHttpClient();
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao inicializar DynamicsClient. Cliente será criado mas não funcionará.");
                _tenantId = "";
                _clientId = "";
                _certificateThumbprint = "";
                _certificatePath = "";
                _certificatePassword = "";
                _baseUrl = "";
                _tokenExpirationBuffer = 300;
            }
        }

        private string GetCfg(string key)
        {
            // Ordem: seção com ":", seção com "__", antiga "flat", e dentro de Values
            return _configuration[$"Dynamics:{key}"]
                ?? _configuration[$"Dynamics__{key}"]
                ?? _configuration[$"Dynamics{key}"] // ex: DynamicsTenantId (legado)
                ?? _configuration[$"Values:Dynamics:{key}"]
                ?? _configuration[$"Values:Dynamics__{key}"]
                ?? _configuration[$"Values:Dynamics{key}"]
                ?? "";
        }


        private void InitializeConfidentialClient()
        {
            try
            {
                X509Certificate2? certificate = null;
                
                // Carregar certificado
                if (!string.IsNullOrEmpty(_certificatePath) && File.Exists(_certificatePath))
                {
                    _logger.LogInformation($"Carregando certificado do arquivo: {_certificatePath}");
                    certificate = new X509Certificate2(_certificatePath, _certificatePassword, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
                }
                else if (!string.IsNullOrEmpty(_certificateThumbprint))
                {
                    _logger.LogInformation($"Procurando certificado por thumbprint: {_certificateThumbprint}");
                    var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                    store.Open(OpenFlags.ReadOnly);
                    var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, _certificateThumbprint, false);
                    store.Close();
                    
                    if (certificates.Count == 0)
                    {
                        throw new InvalidOperationException($"Certificado com thumbprint {_certificateThumbprint} não encontrado no store.");
                    }
                    certificate = certificates[0];
                }
                else
                {
                    throw new InvalidOperationException("Configurações de certificado ausentes. Configure DynamicsCertificatePath ou DynamicsCertificateThumbprint no local.settings.json.");
                }

                if (certificate != null)
                {
                    _logger.LogInformation($"Certificado carregado. Subject: {certificate.Subject}, Thumbprint: {certificate.Thumbprint}");
                    
                    _app = ConfidentialClientApplicationBuilder
                        .Create(_clientId)
                        .WithCertificate(certificate)
                        .WithAuthority(new Uri($"https://login.microsoftonline.com/{_tenantId}"))
                        .Build();
                        
                    _logger.LogInformation("ConfidentialClientApplication inicializado com sucesso");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao inicializar ConfidentialClientApplication");
                throw;
            }
        }

        private void ConfigureHttpClient()
        {
            _httpClient.BaseAddress = new Uri(_baseUrl.TrimEnd('/'));
            _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private async Task<string> GetAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiration.AddSeconds(-_tokenExpirationBuffer))
            {
                return _accessToken;
            }

            // Verificar se as configurações básicas estão disponíveis
            if (string.IsNullOrEmpty(_tenantId) || string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_baseUrl))
            {
                _logger.LogWarning("DynamicsClient não configurado adequadamente. TenantId, ClientId ou BaseUrl ausentes.");
                throw new InvalidOperationException("DynamicsClient não configurado adequadamente. Verifique as configurações no local.settings.json.");
            }

            if (_app == null)
            {
                _logger.LogWarning("ConfidentialClientApplication não inicializado. Tentando inicializar...");
                InitializeConfidentialClient();
            }

            try
            {
                _logger.LogInformation("Obtendo token OAuth2 para Dynamics usando certificado (MSAL)...");
                
                // Usar o escopo específico da instância do Dynamics
                var scopes = new[] { $"{_baseUrl}/.default" };
                
                _logger.LogInformation($"Solicitando token para scopes: {string.Join(", ", scopes)}");
                var result = await _app!.AcquireTokenForClient(scopes).ExecuteAsync();

                _accessToken = result.AccessToken;
                _tokenExpiration = result.ExpiresOn.DateTime;

                _logger.LogInformation("Token OAuth2 obtido com sucesso (MSAL). Expira em: {ExpirationTime}", _tokenExpiration);

                return _accessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter token OAuth2 para Dynamics usando MSAL");
                throw new Exception($"Falha na autenticação por certificado: {ex.Message}", ex);
            }
        }

        private async Task<string> MakeApiCallAsync(string endpoint, HttpMethod method = null, object? data = null)
        {
            method ??= HttpMethod.Get;
            var token = await GetAccessTokenAsync();
            var url = $"api/data/v9.2/{endpoint.TrimStart('/')}";

            _logger.LogInformation("Fazendo chamada para Dynamics: {Method} {Url}", method, url);
            _logger.LogInformation("Token obtido: {TokenLength} caracteres", token?.Length ?? 0);

            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
            // Adicionar headers OData explicitamente na requisição
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (method == HttpMethod.Post || method == HttpMethod.Patch)
            {
                request.Headers.Add("Prefer", "return=representation");
            }

            if (data != null && method != HttpMethod.Get)
            {
                request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
                _logger.LogInformation("Dados da requisição: {Data}", System.Text.Json.JsonSerializer.Serialize(data));
            }

            _logger.LogInformation("Enviando requisição para: {FullUrl}", $"{_httpClient.BaseAddress}{url}");
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Erro na API do Dynamics. Status: {StatusCode}, Conteúdo: {Content}", response.StatusCode, errorContent);
                throw new Exception($"Erro na API do Dynamics: {response.StatusCode} - {errorContent}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<object> GetAgentsAsync(string? organizationId = null)
        {
            try
            {
                _logger.LogInformation("Obtendo agentes do Dynamics...");
                var response = await MakeApiCallAsync("systemusers?$select=systemuserid,fullname,internalemailaddress,isdisabled&$filter=isdisabled eq false");
                var agentsData = JsonDocument.Parse(response);
                
                var agents = new List<object>();
                if (agentsData.RootElement.TryGetProperty("value", out var valueElement))
                {
                    foreach (var agent in valueElement.EnumerateArray())
                    {
                        agents.Add(new
                        {
                            id = agent.TryGetProperty("systemuserid", out var id) ? GetSafeString(id) : null,
                            name = agent.TryGetProperty("fullname", out var name) ? GetSafeString(name) : null,
                            email = agent.TryGetProperty("internalemailaddress", out var email) ? GetSafeString(email) : null,
                            status = agent.TryGetProperty("isdisabled", out var disabled) && disabled.GetBoolean() ? "inactive" : "active"
                        });
                    }
                }

                return new Dictionary<string, object>
                {
                    ["organizationId"] = organizationId,
                    ["agents"] = agents,
                    ["totalCount"] = agents.Count,
                    ["timestamp"] = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter agentes do Dynamics");
                throw;
            }
        }

        public async Task<object> GetWorkstreamsAsync(string? organizationId = null)
        {
            try
            {
                _logger.LogInformation("Obtendo workstreams do Dynamics...");
                var response = await MakeApiCallAsync("msdyn_liveworkstreams?$select=msdyn_liveworkstreamid,msdyn_name,msdyn_streamsource,statecode");
                var workstreamsData = JsonDocument.Parse(response);
                
                var workstreams = new List<object>();
                if (workstreamsData.RootElement.TryGetProperty("value", out var valueElement))
                {
                    foreach (var workstream in valueElement.EnumerateArray())
                    {
                        workstreams.Add(new
                        {
                            id = workstream.TryGetProperty("msdyn_liveworkstreamid", out var id) ? GetSafeString(id) : null,
                            name = workstream.TryGetProperty("msdyn_name", out var name) ? GetSafeString(name) : null,
                            source = workstream.TryGetProperty("msdyn_streamsource", out var source) ? GetSafeString(source) : null,
                            status = workstream.TryGetProperty("statecode", out var state) && state.GetInt32() == 0 ? "active" : "inactive"
                        });
                    }
                }

                return new Dictionary<string, object>
                {
                    ["organizationId"] = organizationId,
                    ["workstreams"] = workstreams,
                    ["totalCount"] = workstreams.Count,
                    ["timestamp"] = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter workstreams do Dynamics");
                throw;
            }
        }

        public async Task<object> GetBotsAsync(string? organizationId = null)
        {
            try
            {
                _logger.LogInformation("Obtendo bots do Dynamics...");
                var response = await MakeApiCallAsync("msdyn_ocbotchannelregistrations?$select=msdyn_ocbotchannelregistrationid,msdyn_name,msdyn_msappid,msdyn_bottype,statecode,statuscode");
                var botsData = JsonDocument.Parse(response);
                
                var bots = new List<object>();
                if (botsData.RootElement.TryGetProperty("value", out var valueElement))
                {
                    foreach (var bot in valueElement.EnumerateArray())
                    {
                        bots.Add(new
                        {
                            id = bot.TryGetProperty("msdyn_ocbotchannelregistrationid", out var id) ? GetSafeString(id) : null,
                            name = bot.TryGetProperty("msdyn_name", out var name) ? GetSafeString(name) : null,
                            msAppId = bot.TryGetProperty("msdyn_msappid", out var msAppId) ? GetSafeString(msAppId) : null,
                            botType = bot.TryGetProperty("msdyn_bottype", out var botType) ? botType.GetInt32() : 0,
                            status = bot.TryGetProperty("statecode", out var state) && state.GetInt32() == 0 ? "active" : "inactive"
                        });
                    }
                }

                return new Dictionary<string, object>
                {
                    ["organizationId"] = organizationId,
                    ["bots"] = bots,
                    ["totalCount"] = bots.Count,
                    ["timestamp"] = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter bots do Dynamics");
                throw;
            }
        }

        public async Task<object> GetAgentWorkstreamsAsync(string agentId)
        {
            try
            {
                _logger.LogInformation($"Obtendo workstreams do agente {agentId}...");
                var response = await MakeApiCallAsync($"systemusers({agentId})/msdyn_systemuser_msdyn_liveworkstream?$select=msdyn_liveworkstreamid,msdyn_name");
                var workstreamsData = JsonDocument.Parse(response);
                
                var workstreams = new List<string>();
                if (workstreamsData.RootElement.TryGetProperty("value", out var valueElement))
                {
                    foreach (var workstream in valueElement.EnumerateArray())
                    {
                        if (workstream.TryGetProperty("msdyn_liveworkstreamid", out var id))
                        {
                            workstreams.Add(GetSafeString(id) ?? "");
                        }
                    }
                }

                return workstreams;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao obter workstreams do agente {agentId}");
                return new List<string>();
            }
        }

        public async Task<object> GetAgentSkillsAsync(string agentId)
        {
            try
            {
                _logger.LogInformation($"Obtendo skills do agente {agentId}...");
                var response = await MakeApiCallAsync($"systemusers({agentId})/msdyn_systemuser_msdyn_skillattachmenttarget?$select=msdyn_skillattachmenttargetid,msdyn_name,msdyn_ratingvalue");
                var skillsData = JsonDocument.Parse(response);
                
                var skills = new List<object>();
                if (skillsData.RootElement.TryGetProperty("value", out var valueElement))
                {
                    foreach (var skill in valueElement.EnumerateArray())
                    {
                        skills.Add(new
                        {
                            name = skill.TryGetProperty("msdyn_name", out var name) ? GetSafeString(name) : null,
                            proficiency = skill.TryGetProperty("msdyn_ratingvalue", out var rating) ? rating.GetInt32() : 0
                        });
                    }
                }

                return skills;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao obter skills do agente {agentId}");
                return new List<object>();
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                await MakeApiCallAsync("systemusers?$top=1");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao testar conexão com Dynamics");
                return false;
            }
        }

        public async Task<bool> ImportMigrationResultAsync(object migrationResult)
        {
            try
            {
                _logger.LogInformation("*** MÉTODO ImportMigrationResultAsync CHAMADO - INICIANDO IMPORTAÇÃO REAL ***");
                _logger.LogInformation("Iniciando importação REAL para Dynamics Contact Center...");
                
                // Converter o objeto para o tipo correto
                var jsonString = System.Text.Json.JsonSerializer.Serialize(migrationResult);
                _logger.LogInformation($"JSON serializado: {jsonString.Substring(0, Math.Min(200, jsonString.Length))}...");
                var dynamicsMigrationResult = System.Text.Json.JsonSerializer.Deserialize<DynamicsMigrationResult>(jsonString);
                
                if (dynamicsMigrationResult == null)
                {
                    _logger.LogError("Resultado da migração é nulo");
                    return false;
                }
                
                var success = true;
                
                // Importar workstreams
                if (dynamicsMigrationResult.Workstreams?.Count > 0)
                {
                    success &= await ImportWorkstreamsAsync(dynamicsMigrationResult.Workstreams);
                }
                
                // Importar bot configurations
                if (dynamicsMigrationResult.BotConfigurations?.Count > 0)
                {
                    success &= await ImportBotConfigurationsAsync(dynamicsMigrationResult.BotConfigurations);
                }
                
                // Importar routing rules
                if (dynamicsMigrationResult.RoutingRules?.Count > 0)
                {
                    success &= await ImportRoutingRulesAsync(dynamicsMigrationResult.RoutingRules);
                }
                
                // Importar context variables
                if (dynamicsMigrationResult.ContextVariables?.Count > 0)
                {
                    success &= await ImportContextVariablesAsync(dynamicsMigrationResult.ContextVariables);
                }
                
                _logger.LogInformation($"Importação REAL concluída. Sucesso: {success}");
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro durante a importação REAL para Dynamics Contact Center");
                return false;
            }
        }
        
        private async Task<bool> ImportWorkstreamsAsync(List<DynamicsWorkstream> workstreams)
        {
            try
            {
                _logger.LogInformation($"Importando {workstreams.Count} workstreams REAIS...");
                
                var successCount = 0;
                
                foreach (var workstream in workstreams)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(workstream.Name))
                        {
                            _logger.LogWarning("Workstream com nome vazio ignorado");
                            continue;
                        }
                        
                        var result = await CreateWorkstreamAsync(workstream);
                        if (result)
                        {
                            successCount++;
                            _logger.LogInformation($"Workstream '{workstream.Name}' criado com sucesso");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Erro ao criar workstream '{workstream.Name}'");
                    }
                }
                
                _logger.LogInformation($"Workstreams importados: {successCount}/{workstreams.Count}");
                
                return successCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao importar workstreams");
                return false;
            }
        }
        
        private async Task<bool> ImportBotConfigurationsAsync(List<DynamicsBotConfiguration> botConfigurations)
        {
            try
            {
                _logger.LogInformation($"Importando {botConfigurations.Count} bot configurations REAIS...");
                
                var successCount = 0;
                
                foreach (var botConfig in botConfigurations)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(botConfig.Name))
                        {
                            _logger.LogWarning("Bot configuration com nome vazio ignorado");
                            continue;
                        }
                        
                        var result = await CreateBotConfigurationAsync(botConfig);
                        if (result)
                        {
                            successCount++;
                            _logger.LogInformation($"Bot configuration '{botConfig.Name}' criado com sucesso");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Erro ao criar bot configuration '{botConfig.Name}'");
                    }
                }
                
                _logger.LogInformation($"Bot configurations importados: {successCount}/{botConfigurations.Count}");
                
                return successCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao importar bot configurations");
                return false;
            }
        }
        
        private async Task<bool> ImportRoutingRulesAsync(List<DynamicsRoutingRule> routingRules)
        {
            try
            {
                _logger.LogInformation($"Importando {routingRules.Count} routing rules REAIS...");
                
                var successCount = 0;
                
                foreach (var rule in routingRules)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(rule.Name))
                        {
                            _logger.LogWarning("Routing rule com nome vazio ignorado");
                            continue;
                        }
                        
                        var result = await CreateRoutingRuleAsync(rule);
                        if (result)
                        {
                            successCount++;
                            _logger.LogInformation($"Routing rule '{rule.Name}' criado com sucesso");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Erro ao criar routing rule '{rule.Name}'");
                    }
                }
                
                _logger.LogInformation($"Routing rules importados: {successCount}/{routingRules.Count}");
                
                return successCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao importar routing rules");
                return false;
            }
        }
        
        private async Task<bool> ImportContextVariablesAsync(List<DynamicsContextVariable> contextVariables)
        {
            try
            {
                _logger.LogInformation($"Importando {contextVariables.Count} context variables REAIS...");
                
                var successCount = 0;
                
                foreach (var variable in contextVariables)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(variable.Name))
                        {
                            _logger.LogWarning("Context variable com nome vazio ignorado");
                            continue;
                        }
                        
                        var result = await CreateContextVariableAsync(variable);
                        if (result)
                        {
                            successCount++;
                            _logger.LogInformation($"Context variable '{variable.Name}' criado com sucesso");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Erro ao criar context variable '{variable.Name}'");
                    }
                }
                
                _logger.LogInformation($"Context variables importados: {successCount}/{contextVariables.Count}");
                
                return successCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao importar context variables");
                return false;
            }
        }
        
        private async Task<bool> CreateWorkstreamAsync(DynamicsWorkstream workstream)
        {
            try
            {
                _logger.LogInformation($"Criando workstream: {workstream.Name}");
                
                var workstreamData = new
                {
                    msdyn_name = workstream.Name,
                    msdyn_streamsource = workstream.StreamSource, // Usar propriedade correta
                    statecode = 0 // Ativo
                };
                
                await MakeApiCallAsync("msdyn_liveworkstreams", HttpMethod.Post, workstreamData);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao criar workstream '{workstream.Name}'");
                return false;
            }
        }
        
        private async Task<bool> CreateBotConfigurationAsync(DynamicsBotConfiguration botConfig)
        {
            try
            {
                _logger.LogInformation($"Criando bot configuration: {botConfig.Name}");
                
                var botData = new
                {
                    msdyn_name = botConfig.Name,
                    msdyn_msappid = botConfig.MsAppId,
                    statecode = 0 // Ativo
                };
                
                await MakeApiCallAsync("msdyn_ocbotchannelregistrations", HttpMethod.Post, botData);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao criar bot configuration '{botConfig.Name}'");
                return false;
            }
        }
        
        private async Task<bool> CreateRoutingRuleAsync(DynamicsRoutingRule rule)
        {
            try
            {
                _logger.LogInformation($"Criando routing rule: {rule.Name}");
                
                var ruleData = new
                {
                    msdyn_name = rule.Name,
                    msdyn_description = rule.Description,
                    statecode = 0 // Ativo
                };
                
                await MakeApiCallAsync("msdyn_routingrulesets", HttpMethod.Post, ruleData);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao criar routing rule '{rule.Name}'");
                return false;
            }
        }
        
        private async Task<bool> CreateContextVariableAsync(DynamicsContextVariable variable)
        {
            try
            {
                _logger.LogInformation($"Criando context variable: {variable.Name}");
                
                var variableData = new
                {
                    msdyn_name = variable.Name,
                    msdyn_displayname = variable.DisplayName ?? variable.Name,
                    msdyn_datatype = variable.DataType, // Já é int
                    statecode = 0 // Ativo
                };
                
                await MakeApiCallAsync("msdyn_ocliveworkitemcontextvariables", HttpMethod.Post, variableData);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao criar context variable '{variable.Name}'");
                return false;
            }
        }
        
        public async Task<object> CreateAgentAsync(string environmentId, dynamic agent)
        {
            try
            {
                var domainName = GetDynamicProperty(agent, "domainname") ?? GetDynamicProperty(agent, "internalemailaddress");

                // Verificar se o usuário já existe
                var existingUserResponse = await MakeApiCallAsync($"systemusers?$filter=domainname eq '{domainName}'&$select=systemuserid");
                var existingUserData = JsonDocument.Parse(existingUserResponse);

                if (existingUserData.RootElement.TryGetProperty("value", out var users) && users.GetArrayLength() > 0)
                {
                    var existingUserId = GetSafeString(users[0].GetProperty("systemuserid"));
                    _logger.LogInformation($"Usuário '{domainName}' já existe com ID: {existingUserId}. Pulando criação.");
                    
                    return new Dictionary<string, object>
                    {
                        ["id"] = existingUserId,
                        ["name"] = GetDynamicProperty(agent, "fullname"),
                        ["email"] = GetDynamicProperty(agent, "internalemailaddress"),
                        ["username"] = domainName,
                        ["status"] = "existing",
                        ["createdAt"] = DateTime.UtcNow
                    };
                }

                // Obter business unit padrão (obrigatório para systemuser)
                var businessUnitsResponse = await MakeApiCallAsync("businessunits?$select=businessunitid,name&$top=1");
                var businessUnitsData = JsonDocument.Parse(businessUnitsResponse);
                
                string? businessUnitId = null;
                if (businessUnitsData.RootElement.TryGetProperty("value", out var businessUnits) && businessUnits.GetArrayLength() > 0)
                {
                    var firstBusinessUnit = businessUnits[0];
                    if (firstBusinessUnit.TryGetProperty("businessunitid", out var buId))
                    {
                        businessUnitId = GetSafeString(buId);
                    }
                }
                
                if (string.IsNullOrEmpty(businessUnitId))
                {
                    throw new Exception("Não foi possível obter uma business unit válida para criar o systemuser");
                }

                var fullname = (string)GetDynamicProperty(agent, "fullname") ?? "";
                var email = GetDynamicProperty(agent, "internalemailaddress") ?? "";
                var username = GetDynamicProperty(agent, "domainname") ?? "";
                
                // Derivação robusta de firstname/lastname sem usar placeholder fixo
                string firstname = string.Empty;
                string lastname = string.Empty;

                var rawFullName = fullname?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(rawFullName))
                {
                    var tokens = rawFullName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 2)
                    {
                        // Considera o último token como sobrenome e o restante como nome
                        firstname = string.Join(" ", tokens.Take(tokens.Length - 1));
                        lastname = tokens.Last();
                    }
                    else
                    {
                        // Nome único: usar como lastname para cumprir possíveis exigências do Dynamics
                        lastname = tokens[0];
                    }
                }
                else
                {
                    // Fallback: tentar derivar do email (parte antes do @)
                    var emailLocal = (email ?? string.Empty).Split('@')[0];
                    if (!string.IsNullOrWhiteSpace(emailLocal))
                    {
                        var parts = emailLocal.Replace('.', ' ').Replace('_', ' ')
                                               .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            firstname = string.Join(" ", parts.Take(parts.Length - 1));
                            lastname = parts.Last();
                        }
                        else if (parts.Length == 1)
                        {
                            lastname = parts[0];
                        }
                    }
                }

                // Fallback final: usar o username/domainname se ainda não houver lastname
                if (string.IsNullOrWhiteSpace(lastname))
                {
                    var userLocal = (username ?? string.Empty).Split('@')[0];
                    if (!string.IsNullOrWhiteSpace(userLocal))
                    {
                        var parts = userLocal.Replace('.', ' ').Replace('_', ' ')
                                              .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            firstname = string.Join(" ", parts.Take(parts.Length - 1));
                            lastname = parts.Last();
                        }
                        else if (parts.Length == 1)
                        {
                            lastname = parts[0];
                        }
                    }
                }

                // Garantir que lastname nunca fique vazio para evitar placeholders visíveis no CRM
                if (string.IsNullOrWhiteSpace(lastname))
                {
                    lastname = "User";
                }


                // Montar o payload para a criacao do system user
                var systemUserData = new Dictionary<string, object>
                {
                    ["domainname"] = username,
                    ["firstname"] = firstname,
                    ["lastname"] = lastname,
                    ["internalemailaddress"] = email,
                    ["businessunitid@odata.bind"] = $"/businessunits({businessUnitId})"
                };

                var response = await MakeApiCallAsync("systemusers", HttpMethod.Post, systemUserData);
                var responseData = JsonDocument.Parse(response);

                var systemUserId = responseData.RootElement.TryGetProperty("systemuserid", out var id) 
                    ? GetSafeString(id) 
                    : Guid.NewGuid().ToString();

                _logger.LogInformation($"System User criado com sucesso - ID: {systemUserId}");

                // Aguardar criação automática do usersettings (solução para erro de timing)
                _logger.LogInformation("Aguardando criação automática do usersettings...");
                await Task.Delay(3000); // 3 segundos de delay
                
                // Verificar se usersettings foi criado com retry logic
                await WaitForUserSettingsCreationAsync(systemUserId);
                
                return new Dictionary<string, object>
                {
                    ["id"] = systemUserId,
                    ["name"] = GetDynamicProperty(agent, "fullname"),
                    ["email"] = GetDynamicProperty(agent, "internalemailaddress"),
                    ["username"] = GetDynamicProperty(agent, "domainname"),
                    ["status"] = "created",
                    ["createdAt"] = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                var agentInfoForError = $"Dados: Email={GetDynamicProperty(agent, "email")}, Department={GetDynamicProperty(agent, "department")}, Title={GetDynamicProperty(agent, "title")}";
                _logger.LogError(ex, $"Erro ao criar agente. {agentInfoForError}");
                throw new Exception($"Falha ao criar agente: {ex.Message}", ex);
            }
        }


        private async Task WaitForUserSettingsCreationAsync(string systemUserId)
        {
            const int maxRetries = 5;
            const int delayBetweenRetries = 2000; // 2 segundos
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogInformation($"Verificando existência do usersettings - Tentativa {attempt}/{maxRetries}");
                    
                    // Tentar acessar o usersettings do usuário criado
                    var userSettingsResponse = await MakeApiCallAsync($"usersettingscollection?$filter=systemuserid eq {systemUserId}&$select=usersettingsid");
                    var userSettingsData = JsonDocument.Parse(userSettingsResponse);
                    
                    if (userSettingsData.RootElement.TryGetProperty("value", out var userSettings) && 
                        userSettings.GetArrayLength() > 0)
                    {
                        _logger.LogInformation("UserSettings encontrado com sucesso!");
                        return;
                    }
                    
                    if (attempt < maxRetries)
                    {
                        _logger.LogWarning($"UserSettings ainda não existe. Aguardando {delayBetweenRetries}ms antes da próxima tentativa...");
                        await Task.Delay(delayBetweenRetries);
                    }
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("usersettings") && ex.Message.Contains("does not exist"))
                    {
                        if (attempt < maxRetries)
                        {
                            _logger.LogWarning($"UserSettings ainda não criado automaticamente. Tentativa {attempt}/{maxRetries}. Aguardando...");
                            await Task.Delay(delayBetweenRetries);
                            continue;
                        }
                        else
                        {
                            _logger.LogWarning("UserSettings não foi criado automaticamente após todas as tentativas. Continuando sem verificação.");
                            return;
                        }
                    }
                    else
                    {
                        _logger.LogError(ex, $"Erro inesperado ao verificar usersettings: {ex.Message}");
                        throw;
                    }
                }
            }
            
            _logger.LogWarning("UserSettings não foi encontrado após todas as tentativas, mas continuando com a criação do usuário.");
        }

        public async Task<bool> CreateCharacteristicAsync(DynamicsCharacteristic characteristic)
        {
            try
            {
                _logger.LogInformation($"Criando characteristic: {characteristic.Name}");
                
                var characteristicData = new
                {
                    name = characteristic.Name,
                    description = characteristic.Description,
                    characteristictype = characteristic.CharacteristicType, // 1 = Skill, 2 = Certification
                    statecode = 0 // Ativo
                };
                
                await MakeApiCallAsync("characteristics", HttpMethod.Post, characteristicData);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao criar characteristic '{characteristic.Name}'");
                return false;
            }
        }
        
        private async Task<bool> ImportCharacteristicsAsync(List<DynamicsCharacteristic> characteristics)
        {
            try
            {
                _logger.LogInformation($"Importando {characteristics.Count} characteristics REAIS...");
                
                var successCount = 0;
                
                foreach (var characteristic in characteristics)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(characteristic.Name))
                        {
                            _logger.LogWarning("Characteristic com nome vazio ignorado");
                            continue;
                        }
                        
                        var result = await CreateCharacteristicAsync(characteristic);
                        if (result)
                        {
                            successCount++;
                            _logger.LogInformation($"Characteristic '{characteristic.Name}' criado com sucesso");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Erro ao criar characteristic '{characteristic.Name}'");
                    }
                }
                
                _logger.LogInformation($"Characteristics importados: {successCount}/{characteristics.Count}");
                
                return successCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao importar characteristics");
                return false;
            }
        }

        private static string? GetSafeString(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetDecimal().ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }
        
        private string? GetDynamicProperty(dynamic obj, string propertyName)
        {
            try
            {
                var objType = obj.GetType();
                var property = objType.GetProperty(propertyName);
                return property?.GetValue(obj)?.ToString();
            }
            catch
            {
                return null;
            }
        }
        
        private int GetChannelTypeCode(string channelType)
        {
            return channelType?.ToLower() switch
            {
                "voice" => 192350001,
                "chat" => 192350000,
                "email" => 192350002,
                "sms" => 192350003,
                "messaging" => 192350000,
                _ => 192350000 // Default para messaging/chat
            };
        }
    }
    
    // Classes de modelo necessárias para a migração
    public class DynamicsMigrationResult
    {
        public List<DynamicsWorkstream>? Workstreams { get; set; }
        public List<DynamicsBotConfiguration>? BotConfigurations { get; set; }
        public List<DynamicsRoutingRule>? RoutingRules { get; set; }
        public List<DynamicsContextVariable>? ContextVariables { get; set; }
    }
    
    public class DynamicsAgent
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? Department { get; set; }
        public string? Title { get; set; }
        public string? State { get; set; }
        public string? GenesysUserId { get; set; }
        public string? GenesysSourceId { get; set; }
        public DateTime? MigrationDate { get; set; }
        public List<string>? WorkstreamIds { get; set; }
        public List<DynamicsSkill>? Skills { get; set; }
    }
    
    public class DynamicsSkill
    {
        public string? Name { get; set; }
        public int Proficiency { get; set; }
    }
    
    public class DynamicsWorkstream
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public int StreamSource { get; set; }
        public int Mode { get; set; }
        public int Direction { get; set; }
        public int StateCode { get; set; }
        public int StatusCode { get; set; }
        public List<DynamicsRoutingRule>? RoutingRules { get; set; }
        public List<DynamicsContextVariable>? ContextVariables { get; set; }
        public DynamicsBotConfiguration? BotConfiguration { get; set; }
    }
    
    public class DynamicsBotConfiguration
    {
        public string? Name { get; set; }
        public string? MsAppId { get; set; }
        public int BotType { get; set; }
        public string? TenantId { get; set; }
        public int StateCode { get; set; }
        public int StatusCode { get; set; }
    }
    
    public class DynamicsRoutingRule
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int RuleSetType { get; set; }
        public string? RuleSetDefinition { get; set; }
        public int StateCode { get; set; }
        public int StatusCode { get; set; }
    }
    
    public class DynamicsContextVariable
    {
        public string? Name { get; set; }
        public string? DisplayName { get; set; }
        public int DataType { get; set; }
        public string? DefaultValue { get; set; }
        public int StateCode { get; set; }
        public int StatusCode { get; set; }
    }
}
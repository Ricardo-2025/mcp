using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace GenesysMigrationMCP.Services
{
    public class GenesysTokenResponse
    {
        [JsonProperty("access_token")]
        public string? AccessToken { get; set; }

        [JsonProperty("token_type")]
        public string? TokenType { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }

    public class GenesysCloudClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GenesysCloudClient> _logger;
        private string? _accessToken;
        private DateTime _tokenExpiration;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _environment;
        private readonly string _authUrl;
        private readonly int _tokenExpirationBuffer;

        public GenesysCloudClient(HttpClient httpClient, IConfiguration configuration, ILogger<GenesysCloudClient> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;

            try
            {
                // Tentar diferentes formatos de configuração
                _clientId = _configuration["GenesysCloud__ClientId"] 
                           ?? _configuration["GenesysCloud:ClientId"]
                           ?? _configuration["GENESYSCLOUD__CLIENTID"]
                           ?? "557cefc3-b118-40ef-8a78-ba4ec837b2fd"; // Valor padrão para desenvolvimento
                
                _clientSecret = _configuration["GenesysCloud__ClientSecret"] 
                               ?? _configuration["GenesysCloud:ClientSecret"]
                               ?? _configuration["GENESYSCLOUD__CLIENTSECRET"]
                               ?? "NWUpd2PXrb3mA3MRTv6pBQ6zjm0r0GNnYw3bmSBp1po"; // Valor padrão para desenvolvimento
                
                _environment = _configuration["GenesysCloud__ApiUrl"] 
                              ?? _configuration["GenesysCloud:ApiUrl"]
                              ?? "https://api.usw2.pure.cloud";
                
                _authUrl = _configuration["GenesysCloud__AuthUrl"] 
                          ?? _configuration["GenesysCloud:AuthUrl"]
                          ?? "https://login.usw2.pure.cloud/oauth/token";
                
                _logger.LogInformation($"GenesysCloudClient configurado - ClientId: {(!string.IsNullOrEmpty(_clientId) ? "CONFIGURADO" : "VAZIO")}, Environment: {_environment}");
                
                if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
                {
                    _logger.LogWarning("GenesysCloud ClientId ou ClientSecret não configurados. Usando valores padrão para desenvolvimento.");
                }
                _tokenExpirationBuffer = 300;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao inicializar GenesysCloudClient. Cliente será criado mas não funcionará.");
                _clientId = "";
                _clientSecret = "";
                _environment = "https://api.usw2.pure.cloud";
                _authUrl = "https://login.usw2.pure.cloud/oauth/token";
                _tokenExpirationBuffer = 300;
            }
        }

        private async Task<string> GetAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiration.AddSeconds(-_tokenExpirationBuffer))
            {
                return _accessToken;
            }

            try
            {
                var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
                var request = new HttpRequestMessage(HttpMethod.Post, _authUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);
                request.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                });

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonConvert.DeserializeObject<GenesysTokenResponse>(responseContent);

                if (tokenResponse?.AccessToken == null)
                {
                    throw new Exception("Token de acesso não recebido");
                }

                _accessToken = tokenResponse.AccessToken;
                _tokenExpiration = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

                _logger.LogInformation("Token OAuth2 obtido com sucesso. Expira em: {ExpirationTime}", _tokenExpiration);

                return _accessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter token OAuth2");
                throw;
            }
        }

        public async Task<string> MakeApiCallAsync(string endpoint, HttpMethod method = null, object? data = null)
        {
            method ??= HttpMethod.Get;
            var token = await GetAccessTokenAsync();
            var url = $"{_environment.TrimEnd('/')}/{endpoint.TrimStart('/')}";

            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            if (data != null && method != HttpMethod.Get)
            {
                request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            }

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Erro na API do Genesys Cloud. Status: {StatusCode}, Conteúdo: {Content}", response.StatusCode, errorContent);
                throw new Exception($"Erro na API do Genesys Cloud: {response.StatusCode} - {errorContent}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<object> GetFlowsAsync(string? organizationId = null, string? filterType = "all")
        {
            try
            {
                _logger.LogInformation("Obtendo flows do Genesys Cloud...");
                var response = await MakeApiCallAsync("api/v2/flows?pageSize=100");
                var flowsData = JsonDocument.Parse(response);
                
                var flows = new List<object>();
                if (flowsData.RootElement.TryGetProperty("entities", out var entitiesElement))
                {
                    foreach (var flow in entitiesElement.EnumerateArray())
                    {
                        flows.Add(new
                        {
                            id = flow.TryGetProperty("id", out var id) ? id.GetString() : null,
                            name = flow.TryGetProperty("name", out var name) ? name.GetString() : null,
                            type = flow.TryGetProperty("type", out var type) ? type.GetString() : "inbound",
                            status = "active"
                        });
                    }
                }

                return new Dictionary<string, object>
                {
                    ["organizationId"] = organizationId,
                    ["filterType"] = filterType,
                    ["flows"] = flows,
                    ["totalCount"] = flows.Count,
                    ["timestamp"] = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter flows do Genesys Cloud");
                throw;
            }
        }

        /// <summary>
        /// Método principal para obter todos os dados de flows do Genesys - COPIADO DO PROJETO DE EXEMPLO
        /// </summary>
        public async Task<object> GetFlowsAndEntitiesAsync()
        {
            try
            {
                _logger.LogInformation("*** GENESYS CLIENT MCP: Iniciando extração REAL de dados de flows do Genesys... ***");

                // Lista de tarefas paralelas para buscar dados
                var tasks = new List<Task<(string key, string data)>>
                {
                    // Flows principais
                    Task.Run(async () => {
                        try {
                            _logger.LogInformation("*** GENESYS CLIENT MCP: Chamando API: api/v2/flows?pageSize=100 ***");
                            return ("flows", await MakeApiCallAsync("api/v2/flows?pageSize=100"));
                        } catch (Exception ex) {
                            _logger.LogWarning("Erro ao chamar api/v2/flows: {Error}", ex.Message);
                            return ("flows", "");
                        }
                    }),
                    Task.Run(async () => {
                        try {
                            _logger.LogInformation("*** GENESYS CLIENT MCP: Chamando API: api/v2/flows/versions?pageSize=100 ***");
                            return ("flowVersions", await MakeApiCallAsync("api/v2/flows/versions?pageSize=100"));
                        } catch (Exception ex) {
                            _logger.LogWarning("Erro ao chamar api/v2/flows/versions: {Error}", ex.Message);
                            return ("flowVersions", "");
                        }
                    }),
                    
                    // Bot flows - usando endpoint correto com filtros por tipo
                    Task.Run(async () => {
                        try {
                            _logger.LogInformation("*** GENESYS CLIENT MCP: Chamando API: api/v2/flows?pageSize=100&type=digitalbot ***");
                            var result = await MakeApiCallAsync("api/v2/flows?pageSize=100&type=digitalbot");
                            _logger.LogInformation("Resposta da API digitalbot: {ResponseLength} caracteres", result?.Length ?? 0);
                            if (!string.IsNullOrEmpty(result))
                            {
                                var parsed = ParseJsonSafely(result);
                                var entities = GetEntitiesArray(parsed);
                                _logger.LogInformation("Digital bot flows encontrados: {Count}", entities?.Length ?? 0);
                            }
                            return ("digitalBotFlows", result);
                        } catch (Exception ex) {
                            _logger.LogWarning("Erro ao chamar api/v2/flows com type=digitalbot: {Error}", ex.Message);
                            return ("digitalBotFlows", "");
                        }
                    }),
                    Task.Run(async () => {
                        try {
                            _logger.LogInformation("*** GENESYS CLIENT MCP: Chamando API: api/v2/flows?pageSize=100&type=bot ***");
                            var result = await MakeApiCallAsync("api/v2/flows?pageSize=100&type=bot");
                            _logger.LogInformation("Resposta da API bot flows: {ResponseLength} caracteres", result?.Length ?? 0);
                            if (!string.IsNullOrEmpty(result))
                            {
                                var parsed = ParseJsonSafely(result);
                                var entities = GetEntitiesArray(parsed);
                                _logger.LogInformation("Bot flows encontrados: {Count}", entities?.Length ?? 0);
                            }
                            return ("dialogEngineBotFlows", result);
                        } catch (Exception ex) {
                            _logger.LogWarning("Erro ao chamar api/v2/flows com type=bot: {Error}", ex.Message);
                            return ("dialogEngineBotFlows", "");
                        }
                    })
                };

                // Aguardar todas as tarefas
                var results = await Task.WhenAll(tasks);
                
                // Construir resultado final
                var finalResult = new Dictionary<string, object>
                {
                    ["timestamp"] = DateTime.UtcNow,
                    ["extractionSource"] = "GenesysCloudMCP_RealData"
                };

                foreach (var (key, data) in results)
                {
                    if (!string.IsNullOrEmpty(data))
                    {
                        try
                        {
                            var parsed = ParseJsonSafely(data);
                            finalResult[key] = parsed.HasValue ? (object)parsed.Value : new { error = "Failed to parse JSON" };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Erro ao processar dados de {Key}: {Error}", key, ex.Message);
                            finalResult[key] = new { error = ex.Message };
                        }
                    }
                    else
                    {
                        finalResult[key] = new { error = "No data received" };
                    }
                }

                _logger.LogInformation("*** GENESYS CLIENT MCP: Extração REAL de flows concluída com sucesso! ***");
                return finalResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "*** GENESYS CLIENT MCP: Erro na extração REAL de flows ***");
                throw;
            }
        }

        private JsonElement? ParseJsonSafely(string? json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<JsonElement>(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao fazer parse do JSON: {Json}", json?.Substring(0, Math.Min(100, json.Length)));
                return null;
            }
        }

        private JsonElement[]? GetEntitiesArray(JsonElement? data)
        {
            if (!data.HasValue)
                return null;

            try
            {
                if (data.Value.TryGetProperty("entities", out var entities) && entities.ValueKind == JsonValueKind.Array)
                {
                    return entities.EnumerateArray().ToArray();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao extrair array de entidades");
            }

            return null;
        }

        public async Task<object> GetUsersAsync(string? organizationId = null, string? state = "all")
        {
            try
            {
                _logger.LogInformation("Obtendo usuários do Genesys Cloud...");
                var response = await MakeApiCallAsync("api/v2/users?pageSize=100");
                var usersData = JsonDocument.Parse(response);
                
                var users = new List<object>();
                if (usersData.RootElement.TryGetProperty("entities", out var entitiesElement))
                {
                    foreach (var user in entitiesElement.EnumerateArray())
                    {
                        users.Add(new Dictionary<string, object?>
                        {
                            ["id"] = user.TryGetProperty("id", out var id) ? id.GetString() : null,
                            ["name"] = user.TryGetProperty("name", out var name) ? name.GetString() : null,
                            ["email"] = user.TryGetProperty("email", out var email) ? email.GetString() : null,
                            ["username"] = user.TryGetProperty("username", out var username) ? username.GetString() : null,
                            ["state"] = user.TryGetProperty("state", out var stateEl) ? stateEl.GetString() : "active"
                        });
                    }
                }

                return new Dictionary<string, object>
                {
                    ["organizationId"] = organizationId,
                    ["state"] = state,
                    ["users"] = users,
                    ["totalCount"] = users.Count,
                    ["timestamp"] = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter usuários do Genesys Cloud");
                throw;
            }
        }

        public async Task<object> GetQueuesAsync(string? organizationId = null)
        {
            try
            {
                _logger.LogInformation("Obtendo filas do Genesys Cloud...");
                var response = await MakeApiCallAsync("api/v2/routing/queues?pageSize=100");
                var queuesData = JsonDocument.Parse(response);
                
                var queues = new List<object>();
                if (queuesData.RootElement.TryGetProperty("entities", out var entitiesElement))
                {
                    foreach (var queue in entitiesElement.EnumerateArray())
                    {
                        queues.Add(new
                        {
                            id = queue.TryGetProperty("id", out var id) ? id.GetString() : null,
                            name = queue.TryGetProperty("name", out var name) ? name.GetString() : null,
                            description = queue.TryGetProperty("description", out var desc) ? desc.GetString() : null
                        });
                    }
                }

                return new Dictionary<string, object>
                {
                    ["organizationId"] = organizationId,
                    ["queues"] = queues,
                    ["totalCount"] = queues.Count,
                    ["timestamp"] = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter filas do Genesys Cloud");
                throw;
            }
        }

        public async Task<object> GetQueueRoutingRulesAsync(string queueId)
        {
            try
            {
                _logger.LogInformation($"Obtendo regras de roteamento para a fila {queueId}...");
                var response = await MakeApiCallAsync($"api/v2/routing/queues/{queueId}/routing-rules");
                var rulesData = JsonDocument.Parse(response);
                
                var rules = new List<object>();
                if (rulesData.RootElement.TryGetProperty("entities", out var entitiesElement))
                {
                    foreach (var rule in entitiesElement.EnumerateArray())
                    {
                        rules.Add(new
                        {
                            id = rule.TryGetProperty("id", out var id) ? id.GetString() : null,
                            name = rule.TryGetProperty("name", out var name) ? name.GetString() : null,
                            priority = rule.TryGetProperty("priority", out var priority) ? priority.GetInt32() : 0,
                            enabled = rule.TryGetProperty("enabled", out var enabled) ? enabled.GetBoolean() : false
                        });
                    }
                }

                return rules;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao obter regras de roteamento para a fila {queueId}");
                return new List<object>();
            }
        }

        public async Task<object> GetQueueMembersAsync(string queueId)
        {
            try
            {
                _logger.LogInformation($"Obtendo membros da fila {queueId}...");
                var response = await MakeApiCallAsync($"api/v2/routing/queues/{queueId}/members");
                var membersData = JsonDocument.Parse(response);
                
                var members = new List<object>();
                if (membersData.RootElement.TryGetProperty("entities", out var entitiesElement))
                {
                    foreach (var member in entitiesElement.EnumerateArray())
                    {
                        members.Add(new
                        {
                            userId = member.TryGetProperty("id", out var id) ? id.GetString() : null,
                            name = member.TryGetProperty("name", out var name) ? name.GetString() : null,
                            email = member.TryGetProperty("email", out var email) ? email.GetString() : null,
                            skills = new List<object>()
                        });
                    }
                }

                return members;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao obter membros da fila {queueId}");
                return new List<object>();
            }
        }

        public async Task<object> GetSkillsAsync(int pageSize = 25, int pageNumber = 1, string? name = null)
        {
            try
            {
                pageSize = Math.Max(1, Math.Min(100, pageSize));
                pageNumber = Math.Max(1, pageNumber);

                var endpoint = $"api/v2/routing/skills?pageSize={pageSize}&pageNumber={pageNumber}";
                if (!string.IsNullOrWhiteSpace(name))
                {
                    endpoint += $"&name={Uri.EscapeDataString(name)}";
                }

                _logger.LogInformation("Obtendo skills do Genesys Cloud... endpoint: {Endpoint}", endpoint);
                var response = await MakeApiCallAsync(endpoint);
                var skillsData = JsonDocument.Parse(response);

                var skills = new List<object>();
                if (skillsData.RootElement.TryGetProperty("entities", out var entitiesElement))
                {
                    foreach (var skill in entitiesElement.EnumerateArray())
                    {
                        skills.Add(new Dictionary<string, object?>
                        {
                            ["id"] = skill.TryGetProperty("id", out var id) ? id.GetString() : null,
                            ["name"] = skill.TryGetProperty("name", out var n) ? n.GetString() : null,
                            ["dateCreated"] = skill.TryGetProperty("dateCreated", out var dc) ? dc.GetDateTime().ToString("o") : null,
                            ["dateModified"] = skill.TryGetProperty("dateModified", out var dm) ? dm.GetDateTime().ToString("o") : null,
                            ["state"] = skill.TryGetProperty("state", out var st) ? st.GetString() : null
                        });
                    }
                }

                return new Dictionary<string, object>
                {
                    ["skills"] = skills,
                    ["totalCount"] = skills.Count,
                    ["pageSize"] = pageSize,
                    ["pageNumber"] = pageNumber,
                    ["timestamp"] = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter skills do Genesys Cloud");
                throw;
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                await GetAccessTokenAsync();
                await MakeApiCallAsync("api/v2/organizations/me");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao testar conexão com Genesys Cloud");
                return false;
            }
        }
    }
}
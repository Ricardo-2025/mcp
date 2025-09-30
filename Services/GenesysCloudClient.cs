using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;


namespace GenesysMigrationMCP.Services
{
    public class GenesysTokenResponse
    {
        [Newtonsoft.Json.JsonProperty("access_token")]
        public string? AccessToken { get; set; }

        [Newtonsoft.Json.JsonProperty("token_type")]
        public string? TokenType { get; set; }

        [Newtonsoft.Json.JsonProperty("expires_in")]
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
        private readonly string _baseUrl;
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

                _baseUrl = _environment; // Set _baseUrl to the same value as _environment

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
                var tokenResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<GenesysTokenResponse>(responseContent);

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

        /// <summary>
        /// Obtém os steps/ações detalhados de um bot específico do Genesys Cloud
        /// </summary>
        /// <param name="botId">ID do bot</param>
        /// <param name="includeDefinition">Se deve incluir a definição completa do flow</param>
        /// <returns>Objeto contendo os steps do bot</returns>
        public async Task<object> GetBotStepsAsync(string botId, bool includeDefinition = true)
        {
            try
            {
                _logger.LogInformation($"Obtendo steps do bot {botId} do Genesys Cloud...");

                // Primeiro, obter os dados básicos do bot/flow
                var response = await MakeApiCallAsync($"api/v2/flows/{Uri.EscapeDataString(botId)}");
                var botData = JsonDocument.Parse(response);

                var result = new Dictionary<string, object>
                {
                    ["botId"] = botId,
                    ["basicInfo"] = ExtractJsonObject(botData.RootElement),
                    ["timestamp"] = DateTime.UtcNow
                };

                if (includeDefinition)
                {
                    try
                    {
                        // Obter a configuração completa do flow/bot
                        var configResponse = await MakeApiCallAsync($"api/v2/flows/{Uri.EscapeDataString(botId)}/configuration");
                        var configData = JsonDocument.Parse(configResponse);

                        result["configuration"] = ExtractJsonObject(configData.RootElement);

                        // Extrair steps da definição
                        var steps = ExtractBotStepsFromDefinition(configData.RootElement);
                        result["steps"] = steps;
                        result["stepsCount"] = steps.Count;

                        // Tentar obter versões do flow
                        try
                        {
                            var versionsResponse = await MakeApiCallAsync($"api/v2/flows/{Uri.EscapeDataString(botId)}/versions");
                            var versionsData = JsonDocument.Parse(versionsResponse);
                            result["versions"] = ExtractJsonArray(versionsData.RootElement);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Não foi possível obter versões do bot {botId}");
                            result["versions"] = new List<object>();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Não foi possível obter configuração completa do bot {botId}");
                        result["configuration"] = null;
                        result["steps"] = new List<object>();
                        result["stepsCount"] = 0;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao obter steps do bot {botId}");
                throw new Exception($"Falha ao obter steps do bot: {ex.Message}");
            }
        }

        // Método auxiliar para extrair objetos JSON aninhados
        private Dictionary<string, object> ExtractJsonObject(JsonElement element)
        {
            var result = new Dictionary<string, object>();
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? "",
                    JsonValueKind.Number => property.Value.TryGetInt32(out int intValue) ? intValue : property.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    JsonValueKind.Object => ExtractJsonObject(property.Value),
                    JsonValueKind.Array => ExtractJsonArray(property.Value),
                    _ => property.Value.ToString()
                };
            }
            return result;
        }

        // Método auxiliar para extrair arrays JSON
        private List<object> ExtractJsonArray(JsonElement element)
        {
            var result = new List<object>();
            foreach (var item in element.EnumerateArray())
            {
                result.Add(item.ValueKind switch
                {
                    JsonValueKind.String => item.GetString() ?? "",
                    JsonValueKind.Number => item.TryGetInt32(out int intValue) ? intValue : item.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    JsonValueKind.Object => ExtractJsonObject(item),
                    JsonValueKind.Array => ExtractJsonArray(item),
                    _ => item.ToString()
                });
            }
            return result;
        }


        /// <summary>
        /// Extrai steps/ações da definição de um bot de forma recursiva
        /// </summary>
        /// <param name="element">Elemento JSON para analisar</param>
        /// <returns>Lista de steps encontrados</returns>
        private List<object> ExtractBotStepsFromDefinition(JsonElement element)
        {
            var steps = new List<object>();

            try
            {
                // Procurar por diferentes tipos de steps/ações em bots
                var stepTypes = new[] { "steps", "actions", "tasks", "states", "nodes", "activities", "intents", "entities" };

                foreach (var stepType in stepTypes)
                {
                    if (element.TryGetProperty(stepType, out var stepsElement))
                    {
                        if (stepsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var step in stepsElement.EnumerateArray())
                            {
                                var stepObj = ExtractJsonObject(step);
                                stepObj["stepType"] = stepType;
                                steps.Add(stepObj);
                            }
                        }
                        else if (stepsElement.ValueKind == JsonValueKind.Object)
                        {
                            var stepObj = ExtractJsonObject(stepsElement);
                            stepObj["stepType"] = stepType;
                            steps.Add(stepObj);
                        }
                    }
                }

                // Busca recursiva em objetos aninhados
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Object ||
                            property.Value.ValueKind == JsonValueKind.Array)
                        {
                            var nestedSteps = ExtractBotStepsFromDefinition(property.Value);
                            steps.AddRange(nestedSteps);
                        }
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            var nestedSteps = ExtractBotStepsFromDefinition(item);
                            steps.AddRange(nestedSteps);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao extrair steps da definição do bot");
            }

            return steps;
        }

        /// <summary>
        /// Obtém todas as regras de roteamento do Genesys Cloud com extração dinâmica de propriedades
        /// </summary>
        /// <param name="includeDetails">Se deve incluir detalhes completos das regras</param>
        /// <returns>Objeto contendo todas as regras de roteamento encontradas</returns>
        public async Task<object> GetRoutingRulesAsync(bool includeDetails = true)
        {
            try
            {
                _logger.LogInformation("Obtendo regras de roteamento do Genesys Cloud...");

                var routingRules = new List<object>();
                var routingRuleDetails = new List<object>();
                var errors = new List<string>();

                // 1. Obter regras de roteamento gerais
                try
                {
                    var (success, response) = await MakeApiCallWithOptionalResultAsync("api/v2/routing/rules");
                    if (success)
                    {
                        var rulesData = JsonDocument.Parse(response);

                        if (rulesData.RootElement.TryGetProperty("entities", out var entitiesElement))
                        {
                            foreach (var rule in entitiesElement.EnumerateArray())
                            {
                                var ruleProperties = new Dictionary<string, object>();

                                // Extrair todas as propriedades dinamicamente
                                foreach (var property in rule.EnumerateObject())
                                {
                                    string propertyName = property.Name;
                                    JsonElement propertyValue = property.Value;

                                    object convertedValue = propertyValue.ValueKind switch
                                    {
                                        JsonValueKind.String => propertyValue.GetString() ?? "",
                                        JsonValueKind.Number => propertyValue.TryGetInt32(out int intValue) ? intValue : propertyValue.GetDouble(),
                                        JsonValueKind.True => true,
                                        JsonValueKind.False => false,
                                        JsonValueKind.Null => null,
                                        JsonValueKind.Object => ExtractJsonObject(propertyValue),
                                        JsonValueKind.Array => ExtractJsonArray(propertyValue),
                                        _ => propertyValue.ToString()
                                    };

                                    ruleProperties.Add(propertyName, convertedValue);
                                }

                                routingRules.Add(ruleProperties);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogDebug("Nenhuma regra de roteamento geral encontrada (404 - comportamento esperado)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro ao obter regras de roteamento gerais: {ex.Message}");
                    errors.Add($"routing_rules_general: {ex.Message}");
                }

                // 2. Se incluir detalhes, obter informações adicionais de cada regra
                if (includeDetails && routingRules.Any())
                {
                    foreach (var rule in routingRules)
                    {
                        if (rule is Dictionary<string, object> ruleDict && ruleDict.ContainsKey("id"))
                        {
                            var ruleId = ruleDict["id"]?.ToString();
                            if (!string.IsNullOrEmpty(ruleId))
                            {
                                try
                                {
                                    var detailResponse = await MakeApiCallAsync($"api/v2/routing/rules/{ruleId}");
                                    var detailData = JsonDocument.Parse(detailResponse);

                                    var detailProperties = new Dictionary<string, object>();
                                    foreach (var property in detailData.RootElement.EnumerateObject())
                                    {
                                        string propertyName = property.Name;
                                        JsonElement propertyValue = property.Value;

                                        object convertedValue = propertyValue.ValueKind switch
                                        {
                                            JsonValueKind.String => propertyValue.GetString() ?? "",
                                            JsonValueKind.Number => propertyValue.TryGetInt32(out int intValue) ? intValue : propertyValue.GetDouble(),
                                            JsonValueKind.True => true,
                                            JsonValueKind.False => false,
                                            JsonValueKind.Null => null,
                                            JsonValueKind.Object => ExtractJsonObject(propertyValue),
                                            JsonValueKind.Array => ExtractJsonArray(propertyValue),
                                            _ => propertyValue.ToString()
                                        };

                                        detailProperties.Add(propertyName, convertedValue);
                                    }

                                    routingRuleDetails.Add(detailProperties);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"Erro ao obter detalhes da regra {ruleId}: {ex.Message}");
                                    errors.Add($"routing_rule_detail_{ruleId}: {ex.Message}");
                                }
                            }
                        }
                    }
                }

                // 3. Tentar obter regras de roteamento por filas também
                var queueRoutingRules = new List<object>();
                try
                {
                    var queuesResponse = await MakeApiCallAsync("api/v2/routing/queues?pageSize=25");
                    var queuesData = JsonDocument.Parse(queuesResponse);

                    if (queuesData.RootElement.TryGetProperty("entities", out var queueEntities))
                    {
                        foreach (var queue in queueEntities.EnumerateArray())
                        {
                            if (queue.TryGetProperty("id", out var queueIdElement))
                            {
                                var queueId = queueIdElement.GetString();
                                if (!string.IsNullOrEmpty(queueId))
                                {
                                    try
                                    {
                                        var queueRules = await GetQueueRoutingRulesAsync(queueId);
                                        if (queueRules is List<object> rules && rules.Any())
                                        {
                                            queueRoutingRules.AddRange(rules.Select(r => new
                                            {
                                                queueId = queueId,
                                                queueName = queue.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : "Unknown",
                                                rule = r
                                            }));
                                        }
                                    }
                                    catch (Exception ex) when (ex.Message.Contains("NotFound") || ex.Message.Contains("404"))
                                    {
                                        // 404 errors são esperados quando a fila não tem regras de roteamento configuradas
                                        _logger.LogDebug($"Nenhuma regra de roteamento encontrada para a fila {queueId} (404 - comportamento esperado)");
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning($"Erro ao obter regras da fila {queueId}: {ex.Message}");
                                        errors.Add($"queue_routing_rules_{queueId}: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro ao obter regras de roteamento por filas: {ex.Message}");
                    errors.Add($"queue_routing_rules: {ex.Message}");
                }

                var result = new Dictionary<string, object>
                {
                    ["routingRules"] = routingRules,
                    ["routingRuleDetails"] = routingRuleDetails,
                    ["queueRoutingRules"] = queueRoutingRules,
                    ["totalRoutingRules"] = routingRules.Count,
                    ["totalRoutingRuleDetails"] = routingRuleDetails.Count,
                    ["totalQueueRoutingRules"] = queueRoutingRules.Count,
                    ["includeDetails"] = includeDetails,
                    ["extractionTimestamp"] = DateTime.UtcNow,
                    ["extractionSource"] = "GenesysCloudMCP_RealData",
                    ["errors"] = errors
                };

                _logger.LogInformation($"Extração de regras de roteamento concluída. Total: {routingRules.Count} regras gerais, {routingRuleDetails.Count} detalhes, {queueRoutingRules.Count} regras de filas");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao extrair regras de roteamento do Genesys Cloud");
                return new
                {
                    routingRules = new List<object>(),
                    routingRuleDetails = new List<object>(),
                    queueRoutingRules = new List<object>(),
                    totalRoutingRules = 0,
                    totalRoutingRuleDetails = 0,
                    totalQueueRoutingRules = 0,
                    includeDetails = includeDetails,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "GenesysCloudMCP_RealData",
                    error = ex.Message,
                    errors = new List<string> { ex.Message }
                };
            }
        }

        /// <summary>
        /// Faz uma chamada de API onde 404 é considerado um comportamento esperado (não um erro)
        /// </summary>
        public async Task<(bool Success, string Content)> MakeApiCallWithOptionalResultAsync(string endpoint, HttpMethod method = null, object? data = null)
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

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return (true, content);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // 404 é esperado - não é um erro
                _logger.LogDebug("Recurso não encontrado (404) para endpoint {Endpoint} - comportamento esperado", endpoint);
                return (false, string.Empty);
            }
            else
            {
                // Outros erros são reais
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Erro na API do Genesys Cloud. Status: {StatusCode}, Conteúdo: {Content}", response.StatusCode, errorContent);
                throw new Exception($"Erro na API do Genesys Cloud: {response.StatusCode} - {errorContent}");
            }
        }

        public async Task<object> GetWorkspacesAsync(int pageSize = 25, int pageNumber = 1, string? name = null)
        {
            try
            {
                pageSize = Math.Max(1, Math.Min(100, pageSize));
                pageNumber = Math.Max(1, pageNumber);

                var endpoint = $"api/v2/workspaces?pageSize={pageSize}&pageNumber={pageNumber}";
                if (!string.IsNullOrWhiteSpace(name))
                {
                    endpoint += $"&name={Uri.EscapeDataString(name)}";
                }

                _logger.LogInformation("Obtendo workspaces do Genesys Cloud... endpoint: {Endpoint}", endpoint);
                var (success, response) = await MakeApiCallWithOptionalResultAsync(endpoint);

                if (!success)
                {
                    _logger.LogDebug("Nenhum workspace encontrado ou API retornou 404");
                    return new
                    {
                        workspaces = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "GenesysCloudMCP_RealData"
                    };
                }

                var workspacesData = JsonDocument.Parse(response);
                var workspaces = new List<object>();

                if (workspacesData.RootElement.TryGetProperty("entities", out var entitiesElement))
                {
                    foreach (var workspace in entitiesElement.EnumerateArray())
                    {
                        var workspaceProperties = new Dictionary<string, object>();

                        foreach (var property in workspace.EnumerateObject())
                        {
                            string propertyName = property.Name;
                            object? propertyValue = null;

                            switch (property.Value.ValueKind)
                            {
                                case JsonValueKind.String:
                                    propertyValue = property.Value.GetString();
                                    break;
                                case JsonValueKind.Number:
                                    if (property.Value.TryGetInt32(out int intValue))
                                        propertyValue = intValue;
                                    else if (property.Value.TryGetDouble(out double doubleValue))
                                        propertyValue = doubleValue;
                                    break;
                                case JsonValueKind.True:
                                case JsonValueKind.False:
                                    propertyValue = property.Value.GetBoolean();
                                    break;
                                case JsonValueKind.Array:
                                    var arrayItems = new List<object>();
                                    foreach (var item in property.Value.EnumerateArray())
                                    {
                                        if (item.ValueKind == JsonValueKind.String)
                                            arrayItems.Add(item.GetString() ?? "");
                                        else if (item.ValueKind == JsonValueKind.Object)
                                        {
                                            var objDict = new Dictionary<string, object>();
                                            foreach (var objProp in item.EnumerateObject())
                                            {
                                                objDict[objProp.Name] = objProp.Value.ValueKind == JsonValueKind.String
                                                    ? objProp.Value.GetString() ?? ""
                                                    : objProp.Value.ToString();
                                            }
                                            arrayItems.Add(objDict);
                                        }
                                    }
                                    propertyValue = arrayItems;
                                    break;
                                case JsonValueKind.Object:
                                    var nestedDict = new Dictionary<string, object>();
                                    foreach (var nestedProp in property.Value.EnumerateObject())
                                    {
                                        nestedDict[nestedProp.Name] = nestedProp.Value.ValueKind == JsonValueKind.String
                                            ? nestedProp.Value.GetString() ?? ""
                                            : nestedProp.Value.ToString();
                                    }
                                    propertyValue = nestedDict;
                                    break;
                                default:
                                    propertyValue = property.Value.ToString();
                                    break;
                            }

                            if (propertyValue != null)
                            {
                                workspaceProperties[propertyName] = propertyValue;
                            }
                        }

                        workspaces.Add(workspaceProperties);
                    }
                }

                // Obter informações de paginação
                int totalCount = 0;
                if (workspacesData.RootElement.TryGetProperty("total", out var totalElement))
                {
                    totalCount = totalElement.GetInt32();
                }

                _logger.LogInformation("Workspaces obtidos com sucesso: {Count} de {Total}", workspaces.Count, totalCount);

                return new
                {
                    workspaces = workspaces,
                    totalCount = totalCount,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "GenesysCloudMCP_RealData"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter workspaces do Genesys Cloud");
                return new
                {
                    workspaces = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "GenesysCloudMCP_RealData",
                    error = ex.Message,
                    errors = new List<string> { ex.Message }
                };
            }
        }

        public async Task<object> GetDivisionsAsync(int pageSize = 25, int pageNumber = 1, string? name = null)
        {
            try
            {
                var url = $"/api/v2/authorization/divisions";
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(name))
                {
                    queryParams.Add($"name={Uri.EscapeDataString(name)}");
                }

                if (queryParams.Any())
                {
                    url += "?" + string.Join("&", queryParams);
                }

                var (success, response) = await MakeApiCallWithOptionalResultAsync(url);

                if (!success)
                {
                    return new
                    {
                        divisions = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "GenesysCloudMCP_RealData",
                        error = "Failed to retrieve divisions from Genesys Cloud API",
                        errors = new List<string> { "API call failed or returned no data" }
                    };
                }

                var divisionsData = JsonDocument.Parse(response);
                var divisions = new List<object>();
                int totalCount = 0;

                if (divisionsData.RootElement.TryGetProperty("total", out var totalElement))
                {
                    totalCount = totalElement.GetInt32();
                }

                if (divisionsData.RootElement.TryGetProperty("entities", out var entitiesElement))
                {
                    foreach (var division in entitiesElement.EnumerateArray())
                    {
                        var divisionProperties = new Dictionary<string, object>();

                        foreach (var property in division.EnumerateObject())
                        {
                            string propertyName = property.Name;
                            object? propertyValue = null;

                            switch (property.Value.ValueKind)
                            {
                                case JsonValueKind.String:
                                    propertyValue = property.Value.GetString();
                                    break;
                                case JsonValueKind.Number:
                                    if (property.Value.TryGetInt32(out int intValue))
                                        propertyValue = intValue;
                                    else if (property.Value.TryGetDouble(out double doubleValue))
                                        propertyValue = doubleValue;
                                    break;
                                case JsonValueKind.True:
                                case JsonValueKind.False:
                                    propertyValue = property.Value.GetBoolean();
                                    break;
                                case JsonValueKind.Array:
                                    var arrayItems = new List<object>();
                                    foreach (var item in property.Value.EnumerateArray())
                                    {
                                        if (item.ValueKind == JsonValueKind.String)
                                            arrayItems.Add(item.GetString() ?? "");
                                        else if (item.ValueKind == JsonValueKind.Object)
                                        {
                                            var objDict = new Dictionary<string, object>();
                                            foreach (var objProp in item.EnumerateObject())
                                            {
                                                objDict[objProp.Name] = objProp.Value.ValueKind == JsonValueKind.String
                                                    ? objProp.Value.GetString() ?? ""
                                                    : objProp.Value.ToString();
                                            }
                                            arrayItems.Add(objDict);
                                        }
                                    }
                                    propertyValue = arrayItems;
                                    break;
                                case JsonValueKind.Object:
                                    var nestedDict = new Dictionary<string, object>();
                                    foreach (var nestedProp in property.Value.EnumerateObject())
                                    {
                                        nestedDict[nestedProp.Name] = nestedProp.Value.ValueKind == JsonValueKind.String
                                            ? nestedProp.Value.GetString() ?? ""
                                            : nestedProp.Value.ToString();
                                    }
                                    propertyValue = nestedDict;
                                    break;
                                case JsonValueKind.Null:
                                    propertyValue = null;
                                    break;
                            }

                            if (propertyValue != null)
                            {
                                divisionProperties[propertyName] = propertyValue;
                            }
                        }

                        divisions.Add(divisionProperties);
                    }
                }

                return new
                {
                    divisions = divisions,
                    totalCount = totalCount,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "GenesysCloudMCP_RealData"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter divisões do Genesys Cloud");
                return new
                {
                    divisions = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "GenesysCloudMCP_RealData",
                    error = ex.Message,
                    errors = new List<string> { ex.Message }
                };
            }
        }

        public async Task<object> GetGroupsAsync(string? name = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(name))
                {
                    queryParams.Add($"name={Uri.EscapeDataString(name)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/groups?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var groups = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var group = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        group[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        if (property.Value.TryGetInt32(out var intValue))
                                            group[property.Name] = intValue;
                                        else if (property.Value.TryGetDouble(out var doubleValue))
                                            group[property.Name] = doubleValue;
                                        break;
                                    case JsonValueKind.True:
                                    case JsonValueKind.False:
                                        group[property.Name] = property.Value.GetBoolean();
                                        break;
                                    case JsonValueKind.Object:
                                        group[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        group[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        group[property.Name] = null;
                                        break;
                                }
                            }

                            groups.Add(group);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        groups = groups,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        groups = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No groups found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve groups: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving groups from Genesys Cloud");
                throw;
            }
        }

        public async Task<object> GetRolesAsync(string? name = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(name))
                {
                    queryParams.Add($"name={Uri.EscapeDataString(name)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/authorization/roles?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var roles = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var role = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        role[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        if (property.Value.TryGetInt32(out var intValue))
                                            role[property.Name] = intValue;
                                        else if (property.Value.TryGetDouble(out var doubleValue))
                                            role[property.Name] = doubleValue;
                                        break;
                                    case JsonValueKind.True:
                                    case JsonValueKind.False:
                                        role[property.Name] = property.Value.GetBoolean();
                                        break;
                                    case JsonValueKind.Object:
                                        role[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        role[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        role[property.Name] = null;
                                        break;
                                }
                            }

                            roles.Add(role);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        roles = roles,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        roles = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No roles found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve roles: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving roles from Genesys Cloud");
                throw;
            }
        }

        public async Task<object> GetLocationsAsync(string? name = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(name))
                {
                    queryParams.Add($"name={Uri.EscapeDataString(name)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/locations?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var locations = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var location = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        location[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        if (property.Value.TryGetInt32(out var intValue))
                                            location[property.Name] = intValue;
                                        else if (property.Value.TryGetDouble(out var doubleValue))
                                            location[property.Name] = doubleValue;
                                        break;
                                    case JsonValueKind.True:
                                    case JsonValueKind.False:
                                        location[property.Name] = property.Value.GetBoolean();
                                        break;
                                    case JsonValueKind.Object:
                                        location[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        location[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        location[property.Name] = null;
                                        break;
                                }
                            }

                            locations.Add(location);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        locations = locations,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        locations = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No locations found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve locations: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving locations from Genesys Cloud");
                throw;
            }
        }

        public async Task<object> GetAnalyticsAsync(string? interval = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(interval))
                {
                    queryParams.Add($"interval={Uri.EscapeDataString(interval)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/analytics/conversations/details/query?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var analytics = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("conversations", out var conversationsElement))
                    {
                        foreach (var entity in conversationsElement.EnumerateArray())
                        {
                            var analytic = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        analytic[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        if (property.Value.TryGetInt32(out var intValue))
                                            analytic[property.Name] = intValue;
                                        else if (property.Value.TryGetDouble(out var doubleValue))
                                            analytic[property.Name] = doubleValue;
                                        break;
                                    case JsonValueKind.True:
                                    case JsonValueKind.False:
                                        analytic[property.Name] = property.Value.GetBoolean();
                                        break;
                                    case JsonValueKind.Object:
                                        analytic[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        analytic[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        analytic[property.Name] = null;
                                        break;
                                }
                            }

                            analytics.Add(analytic);
                        }
                    }

                    if (root.TryGetProperty("totalHits", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        analytics = analytics,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        analytics = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No analytics found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve analytics: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving analytics from Genesys Cloud");
                throw;
            }
        }

        public async Task<object> GetConversationsAsync(string? mediaType = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(mediaType))
                {
                    queryParams.Add($"mediaType={Uri.EscapeDataString(mediaType)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/conversations?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var conversations = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var conversation = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        conversation[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        if (property.Value.TryGetInt32(out var intValue))
                                            conversation[property.Name] = intValue;
                                        else if (property.Value.TryGetDouble(out var doubleValue))
                                            conversation[property.Name] = doubleValue;
                                        break;
                                    case JsonValueKind.True:
                                    case JsonValueKind.False:
                                        conversation[property.Name] = property.Value.GetBoolean();
                                        break;
                                    case JsonValueKind.Object:
                                        conversation[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        conversation[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        conversation[property.Name] = null;
                                        break;
                                }
                            }

                            conversations.Add(conversation);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        conversations = conversations,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        conversations = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No conversations found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve conversations: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving conversations from Genesys Cloud");
                throw;
            }
        }

        public async Task<object> GetPresenceAsync(string? sourceId = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(sourceId))
                {
                    queryParams.Add($"sourceId={Uri.EscapeDataString(sourceId)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/presence/definitions?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var presences = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var presence = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        presence[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        if (property.Value.TryGetInt32(out var intValue))
                                            presence[property.Name] = intValue;
                                        else if (property.Value.TryGetDouble(out var doubleValue))
                                            presence[property.Name] = doubleValue;
                                        break;
                                    case JsonValueKind.True:
                                    case JsonValueKind.False:
                                        presence[property.Name] = property.Value.GetBoolean();
                                        break;
                                    case JsonValueKind.Object:
                                        presence[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        presence[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        presence[property.Name] = null;
                                        break;
                                }
                            }

                            presences.Add(presence);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        presences = presences,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        presences = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No presence definitions found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve presence definitions: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving presence definitions from Genesys Cloud");
                throw;
            }
        }

        public async Task<object> GetIntegrationsAsync(string? integrationType = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(integrationType))
                {
                    queryParams.Add($"integrationType={Uri.EscapeDataString(integrationType)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/integrations?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var integrations = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var integration = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        integration[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        if (property.Value.TryGetInt32(out var intValue))
                                            integration[property.Name] = intValue;
                                        else if (property.Value.TryGetDouble(out var doubleValue))
                                            integration[property.Name] = doubleValue;
                                        break;
                                    case JsonValueKind.True:
                                    case JsonValueKind.False:
                                        integration[property.Name] = property.Value.GetBoolean();
                                        break;
                                    case JsonValueKind.Object:
                                        integration[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        integration[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        integration[property.Name] = null;
                                        break;
                                }
                            }

                            integrations.Add(integration);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        integrations = integrations,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        integrations = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No integrations found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve integrations: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving integrations from Genesys Cloud");
                throw;
            }
        }

        public async Task<object> GetExternalContactsAsync(string? name = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(name))
                {
                    queryParams.Add($"q={Uri.EscapeDataString(name)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/externalcontacts/contacts?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var contacts = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var contact = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        contact[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        if (property.Value.TryGetInt32(out var intValue))
                                            contact[property.Name] = intValue;
                                        else if (property.Value.TryGetDouble(out var doubleValue))
                                            contact[property.Name] = doubleValue;
                                        break;
                                    case JsonValueKind.True:
                                    case JsonValueKind.False:
                                        contact[property.Name] = property.Value.GetBoolean();
                                        break;
                                    case JsonValueKind.Object:
                                        contact[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        contact[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        contact[property.Name] = null;
                                        break;
                                }
                            }

                            contacts.Add(contact);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        externalContacts = contacts,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        externalContacts = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No external contacts found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve external contacts: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving external contacts from Genesys Cloud");
                throw;
            }
        }

        public async Task<object> GetScriptsAsync(string? name = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(name))
                {
                    queryParams.Add($"name={Uri.EscapeDataString(name)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/scripts?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var scripts = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var script = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        script[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        if (property.Value.TryGetInt32(out var intValue))
                                            script[property.Name] = intValue;
                                        else if (property.Value.TryGetDouble(out var doubleValue))
                                            script[property.Name] = doubleValue;
                                        break;
                                    case JsonValueKind.True:
                                    case JsonValueKind.False:
                                        script[property.Name] = property.Value.GetBoolean();
                                        break;
                                    case JsonValueKind.Object:
                                        script[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        script[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        script[property.Name] = null;
                                        break;
                                }
                            }

                            scripts.Add(script);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        scripts = scripts,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        scripts = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No scripts found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve scripts: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving scripts from Genesys Cloud");
                throw;
            }
        }

        /// <summary>
        /// Obtém gravações de conversas do Genesys Cloud
        /// </summary>
        /// <param name="conversationId">ID da conversa (opcional)</param>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as gravações</returns>
        public async Task<object> GetRecordingsAsync(string? conversationId = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(conversationId))
                {
                    queryParams.Add($"conversationId={Uri.EscapeDataString(conversationId)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/recordings?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var recordings = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var recording = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        recording[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        recording[property.Name] = property.Value.TryGetInt32(out int intValue) ? intValue : property.Value.GetDouble();
                                        break;
                                    case JsonValueKind.True:
                                        recording[property.Name] = true;
                                        break;
                                    case JsonValueKind.False:
                                        recording[property.Name] = false;
                                        break;
                                    case JsonValueKind.Object:
                                        recording[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        recording[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        recording[property.Name] = null;
                                        break;
                                }
                            }

                            recordings.Add(recording);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        recordings = recordings,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        recordings = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No recordings found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve recordings: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving recordings from Genesys Cloud");
                throw;
            }
        }

        /// <summary>
        /// Obtém cronogramas de agentes (Workforce Management) do Genesys Cloud
        /// </summary>
        /// <param name="managementUnitId">ID da unidade de gerenciamento (opcional)</param>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo os cronogramas</returns>
        public async Task<object> GetSchedulesAsync(string? managementUnitId = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                // Primeiro, tentar obter as unidades de gerenciamento se não foi fornecido um ID específico
                if (string.IsNullOrEmpty(managementUnitId))
                {
                    try
                    {
                        var managementUnitsResult = await MakeApiCallWithOptionalResultAsync("api/v2/workforcemanagement/managementunits");
                        if (managementUnitsResult.Success)
                        {
                            var unitsDoc = JsonDocument.Parse(managementUnitsResult.Content);
                            var unitsRoot = unitsDoc.RootElement;

                            if (unitsRoot.TryGetProperty("entities", out var unitsElement) && unitsElement.GetArrayLength() > 0)
                            {
                                var firstUnit = unitsElement[0];
                                if (firstUnit.TryGetProperty("id", out var idElement))
                                {
                                    managementUnitId = idElement.GetString();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Erro ao obter unidades de gerenciamento: {Error}", ex.Message);
                    }
                }

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                string url;
                if (!string.IsNullOrEmpty(managementUnitId))
                {
                    // Usar endpoint específico da unidade de gerenciamento
                    url = $"api/v2/workforcemanagement/managementunits/{Uri.EscapeDataString(managementUnitId)}/schedules";
                }
                else
                {
                    // Fallback para endpoint de usuários com informações de schedule
                    url = "api/v2/users";
                    queryParams.Add("expand=presence");
                }

                if (queryParams.Any())
                {
                    url += "?" + string.Join("&", queryParams);
                }

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var schedules = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var schedule = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        schedule[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        schedule[property.Name] = property.Value.TryGetInt32(out int intValue) ? intValue : property.Value.GetDouble();
                                        break;
                                    case JsonValueKind.True:
                                        schedule[property.Name] = true;
                                        break;
                                    case JsonValueKind.False:
                                        schedule[property.Name] = false;
                                        break;
                                    case JsonValueKind.Object:
                                        schedule[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        schedule[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        schedule[property.Name] = null;
                                        break;
                                }
                            }

                            schedules.Add(schedule);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        schedules = schedules,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        managementUnitId = managementUnitId,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        schedules = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        managementUnitId = managementUnitId,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No schedules found or access denied"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve schedules: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving schedules from Genesys Cloud");
                return new
                {
                    schedules = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    managementUnitId = managementUnitId,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving schedules - may require specific permissions"
                };
            }
        }

        /// <summary>
        /// Obtém avaliações de qualidade do Genesys Cloud
        /// </summary>
        /// <param name="evaluatorUserId">ID do usuário avaliador (opcional)</param>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as avaliações</returns>
        public async Task<object> GetEvaluationsAsync(string? evaluatorUserId = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(evaluatorUserId))
                {
                    queryParams.Add($"evaluatorUserId={Uri.EscapeDataString(evaluatorUserId)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/quality/evaluations/query?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url, HttpMethod.Post, new { });

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var evaluations = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var evaluation = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        evaluation[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        evaluation[property.Name] = property.Value.TryGetInt32(out int intValue) ? intValue : property.Value.GetDouble();
                                        break;
                                    case JsonValueKind.True:
                                        evaluation[property.Name] = true;
                                        break;
                                    case JsonValueKind.False:
                                        evaluation[property.Name] = false;
                                        break;
                                    case JsonValueKind.Object:
                                        evaluation[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        evaluation[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        evaluation[property.Name] = null;
                                        break;
                                }
                            }

                            evaluations.Add(evaluation);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        evaluations = evaluations,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        evaluatorUserId = evaluatorUserId,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        evaluations = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        evaluatorUserId = evaluatorUserId,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No evaluations found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve evaluations: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving evaluations from Genesys Cloud");
                throw;
            }
        }

        /// <summary>
        /// Obtém campanhas de outbound do Genesys Cloud
        /// </summary>
        /// <param name="name">Nome da campanha (opcional)</param>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as campanhas</returns>
        public async Task<object> GetCampaignsAsync(string? name = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(name))
                {
                    queryParams.Add($"name={Uri.EscapeDataString(name)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/outbound/campaigns?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var campaigns = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var campaign = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        campaign[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        campaign[property.Name] = property.Value.TryGetInt32(out int intValue) ? intValue : property.Value.GetDouble();
                                        break;
                                    case JsonValueKind.True:
                                        campaign[property.Name] = true;
                                        break;
                                    case JsonValueKind.False:
                                        campaign[property.Name] = false;
                                        break;
                                    case JsonValueKind.Object:
                                        campaign[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        campaign[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        campaign[property.Name] = null;
                                        break;
                                }
                            }

                            campaigns.Add(campaign);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        campaigns = campaigns,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        campaigns = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No campaigns found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve campaigns: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving campaigns from Genesys Cloud");
                throw;
            }
        }

        /// <summary>
        /// Obtém estações telefônicas do Genesys Cloud
        /// </summary>
        /// <param name="name">Nome da estação (opcional)</param>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as estações</returns>
        public async Task<object> GetStationsAsync(string? name = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(name))
                {
                    queryParams.Add($"name={Uri.EscapeDataString(name)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/stations?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var stations = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var station = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        station[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        station[property.Name] = property.Value.TryGetInt32(out int intValue) ? intValue : property.Value.GetDouble();
                                        break;
                                    case JsonValueKind.True:
                                        station[property.Name] = true;
                                        break;
                                    case JsonValueKind.False:
                                        station[property.Name] = false;
                                        break;
                                    case JsonValueKind.Object:
                                        station[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        station[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        station[property.Name] = null;
                                        break;
                                }
                            }

                            stations.Add(station);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        stations = stations,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        stations = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No stations found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve stations: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving stations from Genesys Cloud");
                throw;
            }
        }

        /// <summary>
        /// Obtém base de conhecimento do Genesys Cloud
        /// </summary>
        /// <param name="knowledgeBaseId">ID da base de conhecimento (opcional)</param>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo a base de conhecimento</returns>
        public async Task<object> GetKnowledgeAsync(string? knowledgeBaseId = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                string url;
                if (!string.IsNullOrEmpty(knowledgeBaseId))
                {
                    url = $"api/v2/knowledge/knowledgebases/{Uri.EscapeDataString(knowledgeBaseId)}/documents";
                }
                else
                {
                    url = "api/v2/knowledge/knowledgebases";
                }

                if (queryParams.Any())
                {
                    url += "?" + string.Join("&", queryParams);
                }

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var knowledgeItems = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var knowledgeItem = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        knowledgeItem[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        knowledgeItem[property.Name] = property.Value.TryGetInt32(out int intValue) ? intValue : property.Value.GetDouble();
                                        break;
                                    case JsonValueKind.True:
                                        knowledgeItem[property.Name] = true;
                                        break;
                                    case JsonValueKind.False:
                                        knowledgeItem[property.Name] = false;
                                        break;
                                    case JsonValueKind.Object:
                                        knowledgeItem[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        knowledgeItem[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        knowledgeItem[property.Name] = null;
                                        break;
                                }
                            }

                            knowledgeItems.Add(knowledgeItem);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        knowledgeItems = knowledgeItems,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        knowledgeBaseId = knowledgeBaseId,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        knowledgeItems = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        knowledgeBaseId = knowledgeBaseId,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No knowledge items found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve knowledge items: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving knowledge items from Genesys Cloud");
                throw;
            }
        }

        /// <summary>
        /// Obtém correios de voz do Genesys Cloud
        /// </summary>
        /// <param name="userId">ID do usuário (opcional)</param>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo os correios de voz</returns>
        public async Task<object> GetVoicemailAsync(string? userId = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                string url;
                if (!string.IsNullOrEmpty(userId))
                {
                    // Endpoint específico do usuário
                    url = $"api/v2/users/{Uri.EscapeDataString(userId)}/voicemail/messages";
                }
                else
                {
                    // Tentar diferentes endpoints para voicemail
                    // Primeiro, tentar obter o usuário atual
                    try
                    {
                        var userResult = await MakeApiCallWithOptionalResultAsync("api/v2/users/me");
                        if (userResult.Success)
                        {
                            var userDoc = JsonDocument.Parse(userResult.Content);
                            var userRoot = userDoc.RootElement;

                            if (userRoot.TryGetProperty("id", out var userIdElement))
                            {
                                userId = userIdElement.GetString();
                                url = $"api/v2/users/{Uri.EscapeDataString(userId)}/voicemail/messages";
                            }
                            else
                            {
                                // Fallback para endpoint geral de voicemail
                                url = "api/v2/voicemail/messages";
                            }
                        }
                        else
                        {
                            // Fallback para endpoint geral de voicemail
                            url = "api/v2/voicemail/messages";
                        }
                    }
                    catch
                    {
                        // Fallback para endpoint geral de voicemail
                        url = "api/v2/voicemail/messages";
                    }
                }

                if (queryParams.Any())
                {
                    url += "?" + string.Join("&", queryParams);
                }

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var voicemails = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var voicemail = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        voicemail[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        voicemail[property.Name] = property.Value.TryGetInt32(out int intValue) ? intValue : property.Value.GetDouble();
                                        break;
                                    case JsonValueKind.True:
                                        voicemail[property.Name] = true;
                                        break;
                                    case JsonValueKind.False:
                                        voicemail[property.Name] = false;
                                        break;
                                    case JsonValueKind.Object:
                                        voicemail[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        voicemail[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        voicemail[property.Name] = null;
                                        break;
                                }
                            }

                            voicemails.Add(voicemail);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        voicemails = voicemails,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        userId = userId,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        voicemails = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        userId = userId,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No voicemails found or access denied"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve voicemails: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving voicemails from Genesys Cloud");
                return new
                {
                    voicemails = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    userId = userId,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving voicemails - may require specific permissions or user context"
                };
            }
        }

        /// <summary>
        /// Obtém permissões detalhadas do Genesys Cloud
        /// </summary>
        /// <param name="domain">Domínio das permissões (opcional)</param>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as permissões</returns>
        public async Task<object> GetPermissionsAsync(string? domain = null, int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(domain))
                {
                    queryParams.Add($"domain={Uri.EscapeDataString(domain)}");
                }

                var queryString = string.Join("&", queryParams);
                var url = $"api/v2/authorization/permissions?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var permissions = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var permission = new Dictionary<string, object?>();

                            foreach (var property in entity.EnumerateObject())
                            {
                                switch (property.Value.ValueKind)
                                {
                                    case JsonValueKind.String:
                                        permission[property.Name] = property.Value.GetString();
                                        break;
                                    case JsonValueKind.Number:
                                        permission[property.Name] = property.Value.TryGetInt32(out int intValue) ? intValue : property.Value.GetDouble();
                                        break;
                                    case JsonValueKind.True:
                                        permission[property.Name] = true;
                                        break;
                                    case JsonValueKind.False:
                                        permission[property.Name] = false;
                                        break;
                                    case JsonValueKind.Object:
                                        permission[property.Name] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Array:
                                        permission[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object[]>(property.Value.GetRawText());
                                        break;
                                    case JsonValueKind.Null:
                                        permission[property.Name] = null;
                                        break;
                                }
                            }

                            permissions.Add(permission);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        permissions = permissions,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        domain = domain,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else if (!result.Success)
                {
                    return new
                    {
                        permissions = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        domain = domain,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No permissions found"
                    };
                }
                else
                {
                    throw new Exception($"Failed to retrieve permissions: Unknown error occurred");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving permissions from Genesys Cloud");
                throw;
            }
        }

        // HIGH PRIORITY METHODS

        /// <summary>
        /// Obtém alertas do Genesys Cloud
        /// </summary>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo os alertas</returns>
        public async Task<object> GetAlertingAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/alerting/alerts?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var alerts = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var alert = ExtractJsonObject(entity);
                            alerts.Add(alert);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        alerts = alerts,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        alerts = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No alerts found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving alerts from Genesys Cloud");
                return new
                {
                    alerts = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving alerts"
                };
            }
        }

        /// <summary>
        /// Obtém configurações de WebChat do Genesys Cloud
        /// </summary>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as configurações de WebChat</returns>
        public async Task<object> GetWebChatAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/webchat/deployments?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var webchats = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var webchat = ExtractJsonObject(entity);
                            webchats.Add(webchat);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        webchats = webchats,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        webchats = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No webchat deployments found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving webchat deployments from Genesys Cloud");
                return new
                {
                    webchats = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving webchat deployments"
                };
            }
        }

        /// <summary>
        /// Obtém campanhas outbound do Genesys Cloud
        /// </summary>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as campanhas outbound</returns>
        public async Task<object> GetOutboundCampaignsAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/outbound/campaigns?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var campaigns = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var campaign = ExtractJsonObject(entity);
                            campaigns.Add(campaign);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        campaigns = campaigns,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        campaigns = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No outbound campaigns found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving outbound campaigns from Genesys Cloud");
                return new
                {
                    campaigns = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving outbound campaigns"
                };
            }
        }

        /// <summary>
        /// Obtém listas de contatos do Genesys Cloud
        /// </summary>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as listas de contatos</returns>
        public async Task<object> GetContactListsAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/outbound/contactlists?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var contactLists = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var contactList = ExtractJsonObject(entity);
                            contactLists.Add(contactList);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        contactLists = contactLists,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        contactLists = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No contact lists found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving contact lists from Genesys Cloud");
                return new
                {
                    contactLists = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving contact lists"
                };
            }
        }

        /// <summary>
        /// Obtém gerenciamento de conteúdo do Genesys Cloud
        /// </summary>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo o gerenciamento de conteúdo</returns>
        public async Task<object> GetContentManagementAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/contentmanagement/documents?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var documents = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var document = ExtractJsonObject(entity);
                            documents.Add(document);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        documents = documents,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        documents = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No content management documents found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving content management from Genesys Cloud");
                return new
                {
                    documents = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving content management documents"
                };
            }
        }

        /// <summary>
        /// Obtém notificações do Genesys Cloud
        /// </summary>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as notificações</returns>
        public async Task<object> GetNotificationAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/notifications/channels?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var notifications = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var notification = ExtractJsonObject(entity);
                            notifications.Add(notification);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        notifications = notifications,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        notifications = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No notification channels found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving notifications from Genesys Cloud");
                return new
                {
                    notifications = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving notification channels"
                };
            }
        }

        /// <summary>
        /// Obtém configurações de telefonia do Genesys Cloud
        /// </summary>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as configurações de telefonia</returns>
        public async Task<object> GetTelephonyAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/telephony/providers/edges?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var telephony = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var edge = ExtractJsonObject(entity);
                            telephony.Add(edge);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        telephony = telephony,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        telephony = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No telephony edges found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving telephony from Genesys Cloud");
                return new
                {
                    telephony = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving telephony edges"
                };
            }
        }

        /// <summary>
        /// Obtém configurações do Architect do Genesys Cloud
        /// </summary>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as configurações do Architect</returns>
        public async Task<object> GetArchitectAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/architect/flows?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var architectFlows = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var flow = ExtractJsonObject(entity);
                            architectFlows.Add(flow);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        architectFlows = architectFlows,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        architectFlows = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No architect flows found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving architect flows from Genesys Cloud");
                return new
                {
                    architectFlows = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving architect flows"
                };
            }
        }

        /// <summary>
        /// Obtém gerenciamento de qualidade do Genesys Cloud
        /// </summary>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo o gerenciamento de qualidade</returns>
        public async Task<object> GetQualityManagementAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/quality/forms?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var qualityForms = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var form = ExtractJsonObject(entity);
                            qualityForms.Add(form);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        qualityForms = qualityForms,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        qualityForms = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No quality forms found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving quality management from Genesys Cloud");
                return new
                {
                    qualityForms = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving quality forms"
                };
            }
        }

        /// <summary>
        /// Obtém gerenciamento de força de trabalho do Genesys Cloud
        /// </summary>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo o gerenciamento de força de trabalho</returns>
        public async Task<object> GetWorkforceManagementAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/workforcemanagement/managementunits?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var managementUnits = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var unit = ExtractJsonObject(entity);
                            managementUnits.Add(unit);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        managementUnits = managementUnits,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        managementUnits = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No workforce management units found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving workforce management from Genesys Cloud");
                return new
                {
                    managementUnits = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving workforce management units"
                };
            }
        }

        /// <summary>
        /// Obtém configurações de autorização do Genesys Cloud
        /// </summary>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as configurações de autorização</returns>
        public async Task<object> GetAuthorizationAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/authorization/roles?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var authRoles = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var role = ExtractJsonObject(entity);
                            authRoles.Add(role);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        authRoles = authRoles,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        authRoles = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No authorization roles found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving authorization from Genesys Cloud");
                return new
                {
                    authRoles = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving authorization roles"
                };
            }
        }

        /// <summary>
        /// Obtém informações de cobrança do Genesys Cloud
        /// </summary>
        /// <param name="pageSize">Tamanho da página</param>
        /// <param name="pageNumber">Número da página</param>
        /// <returns>Objeto contendo as informações de cobrança</returns>
        public async Task<object> GetBillingAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/billing/reports?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var billingReports = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var report = ExtractJsonObject(entity);
                            billingReports.Add(report);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        billingReports = billingReports,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        billingReports = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No billing reports found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving billing from Genesys Cloud");
                return new
                {
                    billingReports = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving billing reports - may require specific permissions"
                };
            }
        }

        // ===== MEDIUM PRIORITY GENESYS CLOUD API METHODS =====

        /// <summary>
        /// Retrieves Journey data from Genesys Cloud
        /// </summary>
        public async Task<object> GetJourneyAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/journey/segments?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var segments = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var segment = ExtractJsonObject(entity);
                            segments.Add(segment);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        organizationId = organizationId,
                        journey = segments,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        journey = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No journey segments found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving journey data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    journey = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving journey data"
                };
            }
        }

        /// <summary>
        /// Retrieves Social Media data from Genesys Cloud
        /// </summary>
        public async Task<object> GetSocialMediaAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/socialmedia/topics?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var topics = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var topic = ExtractJsonObject(entity);
                            topics.Add(topic);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        organizationId = organizationId,
                        socialMedia = topics,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        socialMedia = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No social media topics found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving social media data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    socialMedia = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving social media data"
                };
            }
        }

        /// <summary>
        /// Retrieves Callback data from Genesys Cloud
        /// </summary>
        public async Task<object> GetCallbackAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/conversations/callbacks?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var callbacks = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var callback = ExtractJsonObject(entity);
                            callbacks.Add(callback);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        organizationId = organizationId,
                        callbacks = callbacks,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        callbacks = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No callbacks found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving callback data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    callbacks = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving callback data"
                };
            }
        }

        /// <summary>
        /// Retrieves Gamification data from Genesys Cloud
        /// </summary>
        public async Task<object> GetGamificationAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/gamification/leaderboard?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var leaderboard = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var entry = ExtractJsonObject(entity);
                            leaderboard.Add(entry);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        organizationId = organizationId,
                        gamification = leaderboard,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        gamification = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No gamification data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving gamification data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    gamification = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving gamification data"
                };
            }
        }

        /// <summary>
        /// Retrieves Learning data from Genesys Cloud
        /// </summary>
        public async Task<object> GetLearningAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/learning/modules?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var modules = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var module = ExtractJsonObject(entity);
                            modules.Add(module);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        organizationId = organizationId,
                        learning = modules,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        learning = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No learning modules found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving learning data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    learning = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving learning data"
                };
            }
        }

        /// <summary>
        /// Retrieves Coaching data from Genesys Cloud
        /// </summary>
        public async Task<object> GetCoachingAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/coaching/appointments?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var appointments = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var appointment = ExtractJsonObject(entity);
                            appointments.Add(appointment);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        organizationId = organizationId,
                        coaching = appointments,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        coaching = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No coaching appointments found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving coaching data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    coaching = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving coaching data"
                };
            }
        }

        /// <summary>
        /// Retrieves Forecasting data from Genesys Cloud
        /// </summary>
        public async Task<object> GetForecastingAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/workforcemanagement/businessunits?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var businessUnits = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var businessUnit = ExtractJsonObject(entity);
                            businessUnits.Add(businessUnit);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        organizationId = organizationId,
                        forecasting = businessUnits,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        forecasting = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No forecasting data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving forecasting data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    forecasting = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving forecasting data"
                };
            }
        }

        /// <summary>
        /// Retrieves Scheduling data from Genesys Cloud
        /// </summary>
        public async Task<object> GetSchedulingAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/workforcemanagement/managementunits?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var managementUnits = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var managementUnit = ExtractJsonObject(entity);
                            managementUnits.Add(managementUnit);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        organizationId = organizationId,
                        scheduling = managementUnits,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        scheduling = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No scheduling data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving scheduling data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    scheduling = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving scheduling data"
                };
            }
        }

        /// <summary>
        /// Retrieves Audit data from Genesys Cloud
        /// </summary>
        public async Task<object> GetAuditAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/audits/query?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var auditMessages = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("auditMessages", out var messagesElement))
                    {
                        foreach (var message in messagesElement.EnumerateArray())
                        {
                            var auditMessage = ExtractJsonObject(message);
                            auditMessages.Add(auditMessage);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        organizationId = organizationId,
                        audit = auditMessages,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        audit = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No audit data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving audit data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    audit = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving audit data"
                };
            }
        }

        /// <summary>
        /// Retrieves Compliance data from Genesys Cloud
        /// </summary>
        public async Task<object> GetComplianceAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = "api/v2/recording/settings";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var compliance = ExtractJsonObject(root);

                    return new
                    {
                        organizationId = organizationId,
                        compliance = compliance,
                        totalCount = 1,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        compliance = new object(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No compliance data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving compliance data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    compliance = new object(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving compliance data"
                };
            }
        }

        /// <summary>
        /// Retrieves GDPR data from Genesys Cloud
        /// </summary>
        public async Task<object> GetGDPRAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryString = $"pageSize={pageSize}&pageNumber={pageNumber}";
                var url = $"api/v2/gdpr/requests?{queryString}";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var requests = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var entity in entitiesElement.EnumerateArray())
                        {
                            var request = ExtractJsonObject(entity);
                            requests.Add(request);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        organizationId = organizationId,
                        gdpr = requests,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        gdpr = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No GDPR requests found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving GDPR data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    gdpr = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving GDPR data"
                };
            }
        }

        /// <summary>
        /// Retrieves Utilities data from Genesys Cloud
        /// </summary>
        public async Task<object> GetUtilitiesAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = "api/v2/utilities/certificate/details";

                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var utilities = ExtractJsonObject(root);

                    return new
                    {
                        organizationId = organizationId,
                        utilities = utilities,
                        totalCount = 1,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        utilities = new object(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No utilities data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving utilities data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    utilities = new object(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving utilities data"
                };
            }
        }

        // ===== LOW PRIORITY GENESYS CLOUD API METHODS =====

        /// <summary>
        /// Obtém dados de Fax do Genesys Cloud
        /// </summary>
        public async Task<object> GetFaxAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/fax/documents?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        fax = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        fax = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No fax data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving fax data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    fax = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving fax data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Greetings do Genesys Cloud
        /// </summary>
        public async Task<object> GetGreetingsAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/greetings?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        greetings = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        greetings = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No greetings data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving greetings data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    greetings = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving greetings data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Command-line Interface do Genesys Cloud
        /// </summary>
        public async Task<object> GetCommandLineInterfaceAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/configuration/schemas?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        cli = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        cli = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No CLI data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving CLI data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    cli = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving CLI data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Messaging do Genesys Cloud
        /// </summary>
        public async Task<object> GetMessagingAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/conversations/messaging/integrations?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        messaging = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        messaging = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No messaging data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving messaging data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    messaging = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving messaging data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Widgets do Genesys Cloud
        /// </summary>
        public async Task<object> GetWidgetsAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/widgets/deployments?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        widgets = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        widgets = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No widgets data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving widgets data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    widgets = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving widgets data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Workspaces do Genesys Cloud
        /// </summary>
        public async Task<object> GetWorkspacesAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/workspaces?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        workspaces = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        workspaces = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No workspaces data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving workspaces data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    workspaces = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving workspaces data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Tokens do Genesys Cloud
        /// </summary>
        public async Task<object> GetTokensAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/tokens/me?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        tokens = new[] { data },
                        totalCount = 1,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        tokens = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No tokens data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tokens data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    tokens = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving tokens data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Usage do Genesys Cloud
        /// </summary>
        public async Task<object> GetUsageAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/usage/query/executionresults?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        usage = data.TryGetProperty("results", out var results) ? results.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("totalHits", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        usage = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No usage data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving usage data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    usage = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving usage data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Uploads do Genesys Cloud
        /// </summary>
        public async Task<object> GetUploadsAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/uploads?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        uploads = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        uploads = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No uploads data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving uploads data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    uploads = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving uploads data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Textbots do Genesys Cloud
        /// </summary>
        public async Task<object> GetTextbotsAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/textbots/bots?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        textbots = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        textbots = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No textbots data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving textbots data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    textbots = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving textbots data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Search do Genesys Cloud
        /// </summary>
        public async Task<object> GetSearchAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/search/suggest?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        search = data.TryGetProperty("results", out var results) ? results.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        search = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No search data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving search data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    search = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving search data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Response Management do Genesys Cloud
        /// </summary>
        public async Task<object> GetResponseManagementAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/responsemanagement/libraries?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        responseManagement = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        responseManagement = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No response management data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving response management data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    responseManagement = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving response management data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Process Automation do Genesys Cloud
        /// </summary>
        public async Task<object> GetProcessAutomationAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/processautomation/triggers?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        processAutomation = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        processAutomation = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No process automation data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving process automation data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    processAutomation = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving process automation data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Notifications do Genesys Cloud (Low Priority)
        /// </summary>
        public async Task<object> GetNotificationsAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/notifications/channels?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        notifications = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        notifications = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No notifications data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving notifications data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    notifications = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving notifications data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Marketplace do Genesys Cloud
        /// </summary>
        public async Task<object> GetMarketplaceAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/marketplace/listings?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        marketplace = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        marketplace = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No marketplace data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving marketplace data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    marketplace = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving marketplace data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Language Understanding do Genesys Cloud
        /// </summary>
        public async Task<object> GetLanguageUnderstandingAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/languageunderstanding/domains?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        languageUnderstanding = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        languageUnderstanding = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No language understanding data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving language understanding data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    languageUnderstanding = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving language understanding data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Identity Providers do Genesys Cloud
        /// </summary>
        public async Task<object> GetIdentityProvidersAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/identityproviders?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        identityProviders = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        identityProviders = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No identity providers data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving identity providers data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    identityProviders = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving identity providers data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Events do Genesys Cloud
        /// </summary>
        public async Task<object> GetEventsAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/events/definitions?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        events = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        events = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No events data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving events data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    events = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving events data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Email do Genesys Cloud
        /// </summary>
        public async Task<object> GetEmailAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/conversations/emails?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        email = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        email = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No email data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving email data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    email = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving email data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Data Tables do Genesys Cloud
        /// </summary>
        public async Task<object> GetDataTablesAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/flows/datatables?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        dataTables = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        dataTables = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No data tables found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving data tables from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    dataTables = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving data tables"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Certificates do Genesys Cloud
        /// </summary>
        public async Task<object> GetCertificatesAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/certificates?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        certificates = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        certificates = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No certificates data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving certificates data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    certificates = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving certificates data"
                };
            }
        }

        /// <summary>
        /// Obtém dados de Attributes do Genesys Cloud
        /// </summary>
        public async Task<object> GetAttributesAsync(string organizationId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/attributes?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        organizationId = organizationId,
                        attributes = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API"
                    };
                }
                else
                {
                    return new
                    {
                        organizationId = organizationId,
                        attributes = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud API",
                        message = "No attributes data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving attributes data from Genesys Cloud");
                return new
                {
                    organizationId = organizationId,
                    attributes = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud API",
                    error = ex.Message,
                    message = "Error retrieving attributes data"
                };
            }
        }

        #region Interaction Analytics API

        /// <summary>
        /// Obtém dados de análise de interações do Genesys Cloud
        /// </summary>
        public async Task<object> GetInteractionAnalyticsAsync(string? conversationId = null, DateTime? startDate = null, DateTime? endDate = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(conversationId))
                    queryParams.Add($"conversationId={conversationId}");

                if (startDate.HasValue)
                    queryParams.Add($"startDate={startDate.Value:yyyy-MM-ddTHH:mm:ssZ}");

                if (endDate.HasValue)
                    queryParams.Add($"endDate={endDate.Value:yyyy-MM-ddTHH:mm:ssZ}");

                var url = $"{_baseUrl}/api/v2/analytics/conversations/details/query?{string.Join("&", queryParams)}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        conversationAnalytics = data.TryGetProperty("conversations", out var conversations) ? conversations.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("totalHits", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        startDate = startDate,
                        endDate = endDate,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Analytics API"
                    };
                }
                else
                {
                    return new
                    {
                        conversationAnalytics = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Analytics API",
                        message = "No interaction analytics data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving interaction analytics data from Genesys Cloud");
                return new
                {
                    conversationAnalytics = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Analytics API",
                    error = ex.Message,
                    message = "Error retrieving interaction analytics data"
                };
            }
        }

        /// <summary>
        /// Obtém métricas de performance de agentes
        /// </summary>
        public async Task<object> GetAgentPerformanceMetricsAsync(string? agentId = null, DateTime? startDate = null, DateTime? endDate = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(agentId))
                    queryParams.Add($"userId={agentId}");

                if (startDate.HasValue)
                    queryParams.Add($"startDate={startDate.Value:yyyy-MM-ddTHH:mm:ssZ}");

                if (endDate.HasValue)
                    queryParams.Add($"endDate={endDate.Value:yyyy-MM-ddTHH:mm:ssZ}");

                var url = $"{_baseUrl}/api/v2/analytics/users/details/query?{string.Join("&", queryParams)}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        agentMetrics = data.TryGetProperty("userDetails", out var userDetails) ? userDetails.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("totalHits", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        agentId = agentId,
                        startDate = startDate,
                        endDate = endDate,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Analytics API"
                    };
                }
                else
                {
                    return new
                    {
                        agentMetrics = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Analytics API",
                        message = "No agent performance metrics found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving agent performance metrics from Genesys Cloud");
                return new
                {
                    agentMetrics = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Analytics API",
                    error = ex.Message,
                    message = "Error retrieving agent performance metrics"
                };
            }
        }

        /// <summary>
        /// Obtém dados de análise de sentimento das interações
        /// </summary>
        public async Task<object> GetSentimentAnalysisAsync(string? conversationId = null, DateTime? startDate = null, DateTime? endDate = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(conversationId))
                    queryParams.Add($"conversationId={conversationId}");

                if (startDate.HasValue)
                    queryParams.Add($"startDate={startDate.Value:yyyy-MM-ddTHH:mm:ssZ}");

                if (endDate.HasValue)
                    queryParams.Add($"endDate={endDate.Value:yyyy-MM-ddTHH:mm:ssZ}");

                var url = $"{_baseUrl}/api/v2/analytics/conversations/details/query?{string.Join("&", queryParams)}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        sentimentData = data.TryGetProperty("conversations", out var conversations) ?
                            conversations.EnumerateArray()
                                .Where(c => c.TryGetProperty("participants", out var _))
                                .SelectMany(c => c.GetProperty("participants").EnumerateArray())
                                .Where(p => p.TryGetProperty("sessions", out var _))
                                .SelectMany(p => p.GetProperty("sessions").EnumerateArray())
                                .Where(s => s.TryGetProperty("segments", out var _))
                                .SelectMany(s => s.GetProperty("segments").EnumerateArray())
                                .Where(seg => seg.TryGetProperty("sentiment", out var _))
                                .Cast<object>()
                                .ToArray() : new object[0],
                        totalCount = data.TryGetProperty("totalHits", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        startDate = startDate,
                        endDate = endDate,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Analytics API"
                    };
                }
                else
                {
                    return new
                    {
                        sentimentData = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Analytics API",
                        message = "No sentiment analysis data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sentiment analysis data from Genesys Cloud");
                return new
                {
                    sentimentData = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Analytics API",
                    error = ex.Message,
                    message = "Error retrieving sentiment analysis data"
                };
            }
        }

        #endregion

        #region Medium Priority API Methods

        public async Task<object> GetJourneyAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/journey/segments?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return data;
                }

                _logger.LogWarning("Journey API request failed with status: {StatusCode}", response.StatusCode);
                return new { entities = new object[0], pageCount = 0, pageSize, pageNumber };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Journey data");
                return new { error = ex.Message, message = "Error retrieving Journey data" };
            }
        }

        public async Task<object> GetSocialMediaAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/socialmedia/topics?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return data;
                }

                _logger.LogWarning("Social Media API request failed with status: {StatusCode}", response.StatusCode);
                return new { entities = new object[0], pageCount = 0, pageSize, pageNumber };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Social Media data");
                return new { error = ex.Message, message = "Error retrieving Social Media data" };
            }
        }

        public async Task<object> GetCallbackAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/routing/queues?pageSize={pageSize}&pageNumber={pageNumber}&divisionId=*";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return data;
                }

                _logger.LogWarning("Callback API request failed with status: {StatusCode}", response.StatusCode);
                return new { entities = new object[0], pageCount = 0, pageSize, pageNumber };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Callback data");
                return new { error = ex.Message, message = "Error retrieving Callback data" };
            }
        }

        public async Task<object> GetGamificationAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/gamification/leaderboard?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return data;
                }

                _logger.LogWarning("Gamification API request failed with status: {StatusCode}", response.StatusCode);
                return new { entities = new object[0], pageCount = 0, pageSize, pageNumber };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Gamification data");
                return new { error = ex.Message, message = "Error retrieving Gamification data" };
            }
        }

        public async Task<object> GetLearningAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/learning/modules?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return data;
                }

                _logger.LogWarning("Learning API request failed with status: {StatusCode}", response.StatusCode);
                return new { entities = new object[0], pageCount = 0, pageSize, pageNumber };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Learning data");
                return new { error = ex.Message, message = "Error retrieving Learning data" };
            }
        }

        public async Task<object> GetCoachingAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/coaching/appointments?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return data;
                }

                _logger.LogWarning("Coaching API request failed with status: {StatusCode}", response.StatusCode);
                return new { entities = new object[0], pageCount = 0, pageSize, pageNumber };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Coaching data");
                return new { error = ex.Message, message = "Error retrieving Coaching data" };
            }
        }

        public async Task<object> GetForecastingAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/workforcemanagement/businessunits?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return data;
                }

                _logger.LogWarning("Forecasting API request failed with status: {StatusCode}", response.StatusCode);
                return new { entities = new object[0], pageCount = 0, pageSize, pageNumber };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Forecasting data");
                return new { error = ex.Message, message = "Error retrieving Forecasting data" };
            }
        }

        public async Task<object> GetSchedulingAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/workforcemanagement/managementunits?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return data;
                }

                _logger.LogWarning("Scheduling API request failed with status: {StatusCode}", response.StatusCode);
                return new { entities = new object[0], pageCount = 0, pageSize, pageNumber };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Scheduling data");
                return new { error = ex.Message, message = "Error retrieving Scheduling data" };
            }
        }

        public async Task<object> GetAuditAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/audits/query?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return data;
                }

                _logger.LogWarning("Audit API request failed with status: {StatusCode}", response.StatusCode);
                return new { entities = new object[0], pageCount = 0, pageSize, pageNumber };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Audit data");
                return new { error = ex.Message, message = "Error retrieving Audit data" };
            }
        }

        public async Task<object> GetComplianceAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/recording/recordingkeys?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return data;
                }

                _logger.LogWarning("Compliance API request failed with status: {StatusCode}", response.StatusCode);
                return new { entities = new object[0], pageCount = 0, pageSize, pageNumber };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Compliance data");
                return new { error = ex.Message, message = "Error retrieving Compliance data" };
            }
        }

        public async Task<object> GetGDPRAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/gdpr/requests?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return data;
                }

                _logger.LogWarning("GDPR API request failed with status: {StatusCode}", response.StatusCode);
                return new { entities = new object[0], pageCount = 0, pageSize, pageNumber };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving GDPR data");
                return new { error = ex.Message, message = "Error retrieving GDPR data" };
            }
        }

        public async Task<object> GetUtilitiesAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/utilities/certificate/details?pageSize={pageSize}&pageNumber={pageNumber}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return data;
                }

                _logger.LogWarning("Utilities API request failed with status: {StatusCode}", response.StatusCode);
                return new { entities = new object[0], pageCount = 0, pageSize, pageNumber };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Utilities data");
                return new { error = ex.Message, message = "Error retrieving Utilities data" };
            }
        }

        #endregion

        #region Skills Management API

        /// <summary>
        /// Obtém configurações avançadas de habilidades
        /// </summary>
        public async Task<object> GetAdvancedSkillsAsync(string? skillId = null, bool includeAssignments = true, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (includeAssignments)
                    queryParams.Add("expand=assignments");

                var url = !string.IsNullOrEmpty(skillId)
                    ? $"{_baseUrl}/api/v2/routing/skills/{skillId}?{string.Join("&", queryParams)}"
                    : $"{_baseUrl}/api/v2/routing/skills?{string.Join("&", queryParams)}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        skills = !string.IsNullOrEmpty(skillId) ? new[] { data } :
                            (data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0]),
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : (!string.IsNullOrEmpty(skillId) ? 1 : 0),
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        includeAssignments = includeAssignments,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Skills Management API"
                    };
                }
                else
                {
                    return new
                    {
                        skills = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Skills Management API",
                        message = "No advanced skills data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving advanced skills data from Genesys Cloud");
                return new
                {
                    skills = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Skills Management API",
                    error = ex.Message,
                    message = "Error retrieving advanced skills data"
                };
            }
        }

        /// <summary>
        /// Obtém grupos de habilidades
        /// </summary>
        public async Task<object> GetSkillGroupsAsync(string? groupId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                var url = !string.IsNullOrEmpty(groupId)
                    ? $"{_baseUrl}/api/v2/routing/skillgroups/{groupId}?{string.Join("&", queryParams)}"
                    : $"{_baseUrl}/api/v2/routing/skillgroups?{string.Join("&", queryParams)}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        skillGroups = !string.IsNullOrEmpty(groupId) ? new[] { data } :
                            (data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0]),
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : (!string.IsNullOrEmpty(groupId) ? 1 : 0),
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Skills Management API"
                    };
                }
                else
                {
                    return new
                    {
                        skillGroups = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Skills Management API",
                        message = "No skill groups data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving skill groups data from Genesys Cloud");
                return new
                {
                    skillGroups = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Skills Management API",
                    error = ex.Message,
                    message = "Error retrieving skill groups data"
                };
            }
        }

        /// <summary>
        /// Obtém atribuições de habilidades para usuários
        /// </summary>
        public async Task<object> GetUserSkillAssignmentsAsync(string userId, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                var url = $"{_baseUrl}/api/v2/users/{userId}/routingskills?{string.Join("&", queryParams)}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        userId = userId,
                        skillAssignments = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Skills Management API"
                    };
                }
                else
                {
                    return new
                    {
                        userId = userId,
                        skillAssignments = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Skills Management API",
                        message = "No user skill assignments found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user skill assignments from Genesys Cloud");
                return new
                {
                    userId = userId,
                    skillAssignments = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Skills Management API",
                    error = ex.Message,
                    message = "Error retrieving user skill assignments"
                };
            }
        }

        #endregion

        #region Enhanced OAuth API

        /// <summary>
        /// Obtém informações detalhadas do token OAuth
        /// </summary>
        public async Task<object> GetOAuthTokenInfoAsync()
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/oauth/clients";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        oauthClients = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        tokenExpiration = _tokenExpiration,
                        isTokenValid = _tokenExpiration > DateTime.UtcNow,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud OAuth API"
                    };
                }
                else
                {
                    return new
                    {
                        oauthClients = new object[0],
                        totalCount = 0,
                        tokenExpiration = _tokenExpiration,
                        isTokenValid = _tokenExpiration > DateTime.UtcNow,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud OAuth API",
                        message = "No OAuth client information found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving OAuth token information from Genesys Cloud");
                return new
                {
                    oauthClients = new object[0],
                    totalCount = 0,
                    tokenExpiration = _tokenExpiration,
                    isTokenValid = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud OAuth API",
                    error = ex.Message,
                    message = "Error retrieving OAuth token information"
                };
            }
        }

        /// <summary>
        /// Obtém escopos disponíveis para OAuth
        /// </summary>
        public async Task<object> GetOAuthScopesAsync()
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/oauth/scopes";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        scopes = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud OAuth API"
                    };
                }
                else
                {
                    return new
                    {
                        scopes = new object[0],
                        totalCount = 0,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud OAuth API",
                        message = "No OAuth scopes found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving OAuth scopes from Genesys Cloud");
                return new
                {
                    scopes = new object[0],
                    totalCount = 0,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud OAuth API",
                    error = ex.Message,
                    message = "Error retrieving OAuth scopes"
                };
            }
        }

        /// <summary>
        /// Renova o token OAuth automaticamente
        /// </summary>
        public async Task<object> RefreshOAuthTokenAsync()
        {
            try
            {
                var tokenData = new
                {
                    grant_type = "client_credentials",
                    client_id = _clientId,
                    client_secret = _clientSecret
                };

                var json = System.Text.Json.JsonSerializer.Serialize(tokenData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_authUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var tokenResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<GenesysTokenResponse>(responseContent);

                    if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.AccessToken))
                    {
                        _accessToken = tokenResponse.AccessToken;
                        _tokenExpiration = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - _tokenExpirationBuffer);

                        _httpClient.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

                        return new
                        {
                            success = true,
                            tokenExpiration = _tokenExpiration,
                            expiresIn = tokenResponse.ExpiresIn,
                            tokenType = tokenResponse.TokenType,
                            extractionTimestamp = DateTime.UtcNow,
                            extractionSource = "Genesys Cloud OAuth API",
                            message = "Token refreshed successfully"
                        };
                    }
                }

                return new
                {
                    success = false,
                    tokenExpiration = _tokenExpiration,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud OAuth API",
                    message = "Failed to refresh OAuth token"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing OAuth token");
                return new
                {
                    success = false,
                    tokenExpiration = _tokenExpiration,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud OAuth API",
                    error = ex.Message,
                    message = "Error refreshing OAuth token"
                };
            }
        }

        #endregion

        #region Co-browse API

        /// <summary>
        /// Obtém sessões de co-browse ativas
        /// </summary>
        public async Task<object> GetCobrowseSessionsAsync(string? sessionId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                var url = !string.IsNullOrEmpty(sessionId)
                    ? $"{_baseUrl}/api/v2/cobrowse/sessions/{sessionId}?{string.Join("&", queryParams)}"
                    : $"{_baseUrl}/api/v2/cobrowse/sessions?{string.Join("&", queryParams)}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        cobrowseSessions = !string.IsNullOrEmpty(sessionId) ? new[] { data } :
                            (data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0]),
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : (!string.IsNullOrEmpty(sessionId) ? 1 : 0),
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Co-browse API"
                    };
                }
                else
                {
                    return new
                    {
                        cobrowseSessions = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Co-browse API",
                        message = "No co-browse sessions found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving co-browse sessions from Genesys Cloud");
                return new
                {
                    cobrowseSessions = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Co-browse API",
                    error = ex.Message,
                    message = "Error retrieving co-browse sessions"
                };
            }
        }

        /// <summary>
        /// Obtém configurações de co-browse
        /// </summary>
        public async Task<object> GetCobrowseConfigurationAsync()
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/cobrowse/configuration";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        configuration = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Co-browse API"
                    };
                }
                else
                {
                    return new
                    {
                        configuration = new object(),
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Co-browse API",
                        message = "No co-browse configuration found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving co-browse configuration from Genesys Cloud");
                return new
                {
                    configuration = new object(),
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Co-browse API",
                    error = ex.Message,
                    message = "Error retrieving co-browse configuration"
                };
            }
        }

        #endregion

        #region Predictive Engagement API

        /// <summary>
        /// Obtém dados de engajamento preditivo
        /// </summary>
        public async Task<object> GetPredictiveEngagementAsync(string? engagementId = null, DateTime? startDate = null, DateTime? endDate = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (startDate.HasValue)
                    queryParams.Add($"startDate={startDate.Value:yyyy-MM-ddTHH:mm:ssZ}");

                if (endDate.HasValue)
                    queryParams.Add($"endDate={endDate.Value:yyyy-MM-ddTHH:mm:ssZ}");

                var url = !string.IsNullOrEmpty(engagementId)
                    ? $"{_baseUrl}/api/v2/predictiveengagement/engagements/{engagementId}?{string.Join("&", queryParams)}"
                    : $"{_baseUrl}/api/v2/predictiveengagement/engagements?{string.Join("&", queryParams)}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        engagements = !string.IsNullOrEmpty(engagementId) ? new[] { data } :
                            (data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0]),
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : (!string.IsNullOrEmpty(engagementId) ? 1 : 0),
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        startDate = startDate,
                        endDate = endDate,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Predictive Engagement API"
                    };
                }
                else
                {
                    return new
                    {
                        engagements = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Predictive Engagement API",
                        message = "No predictive engagement data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving predictive engagement data from Genesys Cloud");
                return new
                {
                    engagements = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Predictive Engagement API",
                    error = ex.Message,
                    message = "Error retrieving predictive engagement data"
                };
            }
        }

        /// <summary>
        /// Obtém modelos de engajamento preditivo
        /// </summary>
        public async Task<object> GetPredictiveEngagementModelsAsync(string? modelId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                var url = !string.IsNullOrEmpty(modelId)
                    ? $"{_baseUrl}/api/v2/predictiveengagement/models/{modelId}?{string.Join("&", queryParams)}"
                    : $"{_baseUrl}/api/v2/predictiveengagement/models?{string.Join("&", queryParams)}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        models = !string.IsNullOrEmpty(modelId) ? new[] { data } :
                            (data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0]),
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : (!string.IsNullOrEmpty(modelId) ? 1 : 0),
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Predictive Engagement API"
                    };
                }
                else
                {
                    return new
                    {
                        models = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Predictive Engagement API",
                        message = "No predictive engagement models found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving predictive engagement models from Genesys Cloud");
                return new
                {
                    models = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Predictive Engagement API",
                    error = ex.Message,
                    message = "Error retrieving predictive engagement models"
                };
            }
        }

        #endregion

        #region Open Messaging API

        /// <summary>
        /// Obtém canais de mensagens abertas
        /// </summary>
        public async Task<object> GetOpenMessagingChannelsAsync(string? channelId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                var url = !string.IsNullOrEmpty(channelId)
                    ? $"{_baseUrl}/api/v2/conversations/messaging/integrations/open/{channelId}?{string.Join("&", queryParams)}"
                    : $"{_baseUrl}/api/v2/conversations/messaging/integrations/open?{string.Join("&", queryParams)}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        channels = !string.IsNullOrEmpty(channelId) ? new[] { data } :
                            (data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0]),
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : (!string.IsNullOrEmpty(channelId) ? 1 : 0),
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Open Messaging API"
                    };
                }
                else
                {
                    return new
                    {
                        channels = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Open Messaging API",
                        message = "No open messaging channels found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving open messaging channels from Genesys Cloud");
                return new
                {
                    channels = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Open Messaging API",
                    error = ex.Message,
                    message = "Error retrieving open messaging channels"
                };
            }
        }

        /// <summary>
        /// Obtém mensagens de canais abertos
        /// </summary>
        public async Task<object> GetOpenMessagingMessagesAsync(string channelId, DateTime? startDate = null, DateTime? endDate = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (startDate.HasValue)
                    queryParams.Add($"startDate={startDate.Value:yyyy-MM-ddTHH:mm:ssZ}");

                if (endDate.HasValue)
                    queryParams.Add($"endDate={endDate.Value:yyyy-MM-ddTHH:mm:ssZ}");

                var url = $"{_baseUrl}/api/v2/conversations/messaging/integrations/open/{channelId}/messages?{string.Join("&", queryParams)}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        channelId = channelId,
                        messages = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        startDate = startDate,
                        endDate = endDate,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Open Messaging API"
                    };
                }
                else
                {
                    return new
                    {
                        channelId = channelId,
                        messages = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Open Messaging API",
                        message = "No open messaging messages found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving open messaging messages from Genesys Cloud");
                return new
                {
                    channelId = channelId,
                    messages = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Open Messaging API",
                    error = ex.Message,
                    message = "Error retrieving open messaging messages"
                };
            }
        }

        #endregion

        #region SCIM API

        /// <summary>
        /// Obtém usuários via SCIM
        /// </summary>
        public async Task<object> GetScimUsersAsync(string? userId = null, string? filter = null, int startIndex = 1, int count = 100)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"startIndex={startIndex}",
                    $"count={count}"
                };

                if (!string.IsNullOrEmpty(filter))
                    queryParams.Add($"filter={Uri.EscapeDataString(filter)}");

                var url = !string.IsNullOrEmpty(userId)
                    ? $"{_baseUrl}/api/v2/scim/users/{userId}?{string.Join("&", queryParams)}"
                    : $"{_baseUrl}/api/v2/scim/users?{string.Join("&", queryParams)}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        users = !string.IsNullOrEmpty(userId) ? new[] { data } :
                            (data.TryGetProperty("Resources", out var resources) ? resources.EnumerateArray().ToArray() : new JsonElement[0]),
                        totalResults = data.TryGetProperty("totalResults", out var total) ? total.GetInt32() : (!string.IsNullOrEmpty(userId) ? 1 : 0),
                        startIndex = startIndex,
                        count = count,
                        filter = filter,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud SCIM API"
                    };
                }
                else
                {
                    return new
                    {
                        users = new object[0],
                        totalResults = 0,
                        startIndex = startIndex,
                        count = count,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud SCIM API",
                        message = "No SCIM users found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving SCIM users from Genesys Cloud");
                return new
                {
                    users = new object[0],
                    totalResults = 0,
                    startIndex = startIndex,
                    count = count,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud SCIM API",
                    error = ex.Message,
                    message = "Error retrieving SCIM users"
                };
            }
        }

        /// <summary>
        /// Obtém grupos via SCIM
        /// </summary>
        public async Task<object> GetScimGroupsAsync(string? groupId = null, string? filter = null, int startIndex = 1, int count = 100)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"startIndex={startIndex}",
                    $"count={count}"
                };

                if (!string.IsNullOrEmpty(filter))
                    queryParams.Add($"filter={Uri.EscapeDataString(filter)}");

                var url = !string.IsNullOrEmpty(groupId)
                    ? $"{_baseUrl}/api/v2/scim/groups/{groupId}?{string.Join("&", queryParams)}"
                    : $"{_baseUrl}/api/v2/scim/groups?{string.Join("&", queryParams)}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        groups = !string.IsNullOrEmpty(groupId) ? new[] { data } :
                            (data.TryGetProperty("Resources", out var resources) ? resources.EnumerateArray().ToArray() : new JsonElement[0]),
                        totalResults = data.TryGetProperty("totalResults", out var total) ? total.GetInt32() : (!string.IsNullOrEmpty(groupId) ? 1 : 0),
                        startIndex = startIndex,
                        count = count,
                        filter = filter,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud SCIM API"
                    };
                }
                else
                {
                    return new
                    {
                        groups = new object[0],
                        totalResults = 0,
                        startIndex = startIndex,
                        count = count,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud SCIM API",
                        message = "No SCIM groups found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving SCIM groups from Genesys Cloud");
                return new
                {
                    groups = new object[0],
                    totalResults = 0,
                    startIndex = startIndex,
                    count = count,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud SCIM API",
                    error = ex.Message,
                    message = "Error retrieving SCIM groups"
                };
            }
        }

        #endregion

        #region WebRTC API

        /// <summary>
        /// Obtém configurações WebRTC
        /// </summary>
        public async Task<object> GetWebRtcConfigurationAsync()
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/webrtc/configuration";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        configuration = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud WebRTC API"
                    };
                }
                else
                {
                    return new
                    {
                        configuration = new object(),
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud WebRTC API",
                        message = "No WebRTC configuration found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving WebRTC configuration from Genesys Cloud");
                return new
                {
                    configuration = new object(),
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud WebRTC API",
                    error = ex.Message,
                    message = "Error retrieving WebRTC configuration"
                };
            }
        }

        /// <summary>
        /// Obtém sessões WebRTC ativas
        /// </summary>
        public async Task<object> GetWebRtcSessionsAsync(string? sessionId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                var url = !string.IsNullOrEmpty(sessionId)
                    ? $"{_baseUrl}/api/v2/webrtc/sessions/{sessionId}?{string.Join("&", queryParams)}"
                    : $"{_baseUrl}/api/v2/webrtc/sessions?{string.Join("&", queryParams)}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        sessions = !string.IsNullOrEmpty(sessionId) ? new[] { data } :
                            (data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0]),
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : (!string.IsNullOrEmpty(sessionId) ? 1 : 0),
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud WebRTC API"
                    };
                }
                else
                {
                    return new
                    {
                        sessions = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud WebRTC API",
                        message = "No WebRTC sessions found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving WebRTC sessions from Genesys Cloud");
                return new
                {
                    sessions = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud WebRTC API",
                    error = ex.Message,
                    message = "Error retrieving WebRTC sessions"
                };
            }
        }

        #endregion

        #region SIP Endpoint SDK API

        /// <summary>
        /// Obtém endpoints SIP
        /// </summary>
        public async Task<object> GetSipEndpointsAsync(string? endpointId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                var url = !string.IsNullOrEmpty(endpointId)
                    ? $"{_baseUrl}/api/v2/telephony/providers/edges/endpoints/{endpointId}?{string.Join("&", queryParams)}"
                    : $"{_baseUrl}/api/v2/telephony/providers/edges/endpoints?{string.Join("&", queryParams)}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        endpoints = !string.IsNullOrEmpty(endpointId) ? new[] { data } :
                            (data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0]),
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : (!string.IsNullOrEmpty(endpointId) ? 1 : 0),
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud SIP Endpoint API"
                    };
                }
                else
                {
                    return new
                    {
                        endpoints = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud SIP Endpoint API",
                        message = "No SIP endpoints found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving SIP endpoints from Genesys Cloud");
                return new
                {
                    endpoints = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud SIP Endpoint API",
                    error = ex.Message,
                    message = "Error retrieving SIP endpoints"
                };
            }
        }

        /// <summary>
        /// Obtém configurações de telefonia
        /// </summary>
        public async Task<object> GetTelephonyConfigurationAsync()
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/telephony/providers/edges/configuration";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        configuration = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud SIP Endpoint API"
                    };
                }
                else
                {
                    return new
                    {
                        configuration = new object(),
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud SIP Endpoint API",
                        message = "No telephony configuration found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving telephony configuration from Genesys Cloud");
                return new
                {
                    configuration = new object(),
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud SIP Endpoint API",
                    error = ex.Message,
                    message = "Error retrieving telephony configuration"
                };
            }
        }

        #endregion

        #region Geolocation API

        /// <summary>
        /// Obtém dados de geolocalização
        /// </summary>
        public async Task<object> GetGeolocationDataAsync(string? locationId = null, double? latitude = null, double? longitude = null, double? radius = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (latitude.HasValue)
                    queryParams.Add($"latitude={latitude.Value}");

                if (longitude.HasValue)
                    queryParams.Add($"longitude={longitude.Value}");

                if (radius.HasValue)
                    queryParams.Add($"radius={radius.Value}");

                var url = !string.IsNullOrEmpty(locationId)
                    ? $"{_baseUrl}/api/v2/locations/{locationId}?{string.Join("&", queryParams)}"
                    : $"{_baseUrl}/api/v2/locations?{string.Join("&", queryParams)}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        locations = !string.IsNullOrEmpty(locationId) ? new[] { data } :
                            (data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0]),
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : (!string.IsNullOrEmpty(locationId) ? 1 : 0),
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        latitude = latitude,
                        longitude = longitude,
                        radius = radius,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Geolocation API"
                    };
                }
                else
                {
                    return new
                    {
                        locations = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Geolocation API",
                        message = "No geolocation data found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving geolocation data from Genesys Cloud");
                return new
                {
                    locations = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Geolocation API",
                    error = ex.Message,
                    message = "Error retrieving geolocation data"
                };
            }
        }

        /// <summary>
        /// Obtém configurações de geolocalização
        /// </summary>
        public async Task<object> GetGeolocationSettingsAsync()
        {
            try
            {
                var url = $"{_baseUrl}/api/v2/locations/settings";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(content);

                    return new
                    {
                        settings = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Geolocation API"
                    };
                }
                else
                {
                    return new
                    {
                        settings = new object(),
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Geolocation API",
                        message = "No geolocation settings found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving geolocation settings from Genesys Cloud");
                return new
                {
                    settings = new object(),
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Geolocation API",
                    error = ex.Message,
                    message = "Error retrieving geolocation settings"
                };
            }
        }

        #endregion

        #region Apple Messages for Business APIs

        /// <summary>
        /// Obtém configurações do Apple Messages for Business
        /// </summary>
        public async Task<object> GetAppleMessagesConfigurationAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/v2/conversations/messaging/integrations/apple");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return new
                    {
                        configuration = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Apple Messages for Business API"
                    };
                }
                else
                {
                    return new
                    {
                        configuration = new object(),
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Apple Messages for Business API",
                        message = "No Apple Messages configuration found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Apple Messages configuration from Genesys Cloud");
                return new
                {
                    configuration = new object(),
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Apple Messages for Business API",
                    error = ex.Message,
                    message = "Error retrieving Apple Messages configuration"
                };
            }
        }

        /// <summary>
        /// Obtém mensagens do Apple Messages for Business
        /// </summary>
        public async Task<object> GetAppleMessagesAsync(string? conversationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(conversationId))
                    queryParams.Add($"conversationId={conversationId}");

                var response = await _httpClient.GetAsync($"/api/v2/conversations/messaging/apple/messages?{string.Join("&", queryParams)}");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return new
                    {
                        messages = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Apple Messages for Business API"
                    };
                }
                else
                {
                    return new
                    {
                        messages = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Apple Messages for Business API",
                        message = "No Apple Messages found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Apple Messages from Genesys Cloud");
                return new
                {
                    messages = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Apple Messages for Business API",
                    error = ex.Message,
                    message = "Error retrieving Apple Messages"
                };
            }
        }

        #endregion

        #region Campaign Rules APIs

        /// <summary>
        /// Obtém regras de campanha
        /// </summary>
        public async Task<object> GetCampaignRulesAsync(string? campaignId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(campaignId))
                    queryParams.Add($"campaignId={campaignId}");

                var response = await _httpClient.GetAsync($"/api/v2/outbound/campaignrules?{string.Join("&", queryParams)}");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return new
                    {
                        campaignRules = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Campaign Rules API"
                    };
                }
                else
                {
                    return new
                    {
                        campaignRules = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Campaign Rules API",
                        message = "No campaign rules found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving campaign rules from Genesys Cloud");
                return new
                {
                    campaignRules = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Campaign Rules API",
                    error = ex.Message,
                    message = "Error retrieving campaign rules"
                };
            }
        }

        /// <summary>
        /// Obtém configuração de regras de campanha
        /// </summary>
        public async Task<object> GetCampaignRuleConfigurationAsync(string ruleId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/api/v2/outbound/campaignrules/{ruleId}");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return new
                    {
                        ruleConfiguration = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Campaign Rules API"
                    };
                }
                else
                {
                    return new
                    {
                        ruleConfiguration = new object(),
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Campaign Rules API",
                        message = "Campaign rule configuration not found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving campaign rule configuration from Genesys Cloud");
                return new
                {
                    ruleConfiguration = new object(),
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Campaign Rules API",
                    error = ex.Message,
                    message = "Error retrieving campaign rule configuration"
                };
            }
        }

        #endregion

        #region Journey Management APIs

        /// <summary>
        /// Obtém jornadas do cliente
        /// </summary>
        public async Task<object> GetJourneysAsync(string? journeyId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(journeyId))
                    queryParams.Add($"journeyId={journeyId}");

                var response = await _httpClient.GetAsync($"/api/v2/journey/actionmaps?{string.Join("&", queryParams)}");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return new
                    {
                        journeys = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Journey Management API"
                    };
                }
                else
                {
                    return new
                    {
                        journeys = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Journey Management API",
                        message = "No journeys found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving journeys from Genesys Cloud");
                return new
                {
                    journeys = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Journey Management API",
                    error = ex.Message,
                    message = "Error retrieving journeys"
                };
            }
        }

        /// <summary>
        /// Obtém segmentos de jornada
        /// </summary>
        public async Task<object> GetJourneySegmentsAsync(string? segmentId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(segmentId))
                    queryParams.Add($"segmentId={segmentId}");

                var response = await _httpClient.GetAsync($"/api/v2/journey/segments?{string.Join("&", queryParams)}");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return new
                    {
                        segments = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Journey Management API"
                    };
                }
                else
                {
                    return new
                    {
                        segments = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Journey Management API",
                        message = "No journey segments found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving journey segments from Genesys Cloud");
                return new
                {
                    segments = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Journey Management API",
                    error = ex.Message,
                    message = "Error retrieving journey segments"
                };
            }
        }

        #endregion

        #region Voicemail Supervision APIs

        /// <summary>
        /// Obtém configurações de supervisão de correio de voz
        /// </summary>
        public async Task<object> GetVoicemailSupervisionConfigurationAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/v2/voicemail/policy");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return new
                    {
                        supervisionConfiguration = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Voicemail Supervision API"
                    };
                }
                else
                {
                    return new
                    {
                        supervisionConfiguration = new object(),
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Voicemail Supervision API",
                        message = "No voicemail supervision configuration found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving voicemail supervision configuration from Genesys Cloud");
                return new
                {
                    supervisionConfiguration = new object(),
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Voicemail Supervision API",
                    error = ex.Message,
                    message = "Error retrieving voicemail supervision configuration"
                };
            }
        }

        /// <summary>
        /// Obtém mensagens de correio de voz supervisionadas
        /// </summary>
        public async Task<object> GetSupervisedVoicemailsAsync(string? userId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(userId))
                    queryParams.Add($"userId={userId}");

                var response = await _httpClient.GetAsync($"/api/v2/voicemail/messages?{string.Join("&", queryParams)}");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return new
                    {
                        voicemails = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Voicemail Supervision API"
                    };
                }
                else
                {
                    return new
                    {
                        voicemails = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Voicemail Supervision API",
                        message = "No supervised voicemails found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving supervised voicemails from Genesys Cloud");
                return new
                {
                    voicemails = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Voicemail Supervision API",
                    error = ex.Message,
                    message = "Error retrieving supervised voicemails"
                };
            }
        }

        #endregion

        #region External Contacts APIs

        /// <summary>
        /// Obtém organizações de contatos externos
        /// </summary>
        public async Task<object> GetExternalOrganizationsAsync(string? organizationId = null, int pageSize = 100, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(organizationId))
                    queryParams.Add($"organizationId={organizationId}");

                var response = await _httpClient.GetAsync($"/api/v2/externalcontacts/organizations?{string.Join("&", queryParams)}");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return new
                    {
                        externalOrganizations = data.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : new JsonElement[0],
                        totalCount = data.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud External Contacts API"
                    };
                }
                else
                {
                    return new
                    {
                        externalOrganizations = new object[0],
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud External Contacts API",
                        message = "No external organizations found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving external organizations from Genesys Cloud");
                return new
                {
                    externalOrganizations = new object[0],
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud External Contacts API",
                    error = ex.Message,
                    message = "Error retrieving external organizations"
                };
            }
        }

        #endregion

        #region Process Automation Configuration APIs

        /// <summary>
        /// Obtém configuração de automação de processos
        /// </summary>
        public async Task<object> GetProcessAutomationConfigurationAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/v2/processautomation/configuration");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonSerializer.Deserialize<JsonElement>(content);
                    return new
                    {
                        automationConfiguration = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Process Automation API"
                    };
                }
                else
                {
                    return new
                    {
                        automationConfiguration = new object(),
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Process Automation API",
                        message = "No process automation configuration found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving process automation configuration from Genesys Cloud");
                return new
                {
                    automationConfiguration = new object(),
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Process Automation API",
                    error = ex.Message,
                    message = "Error retrieving process automation configuration"
                };
            }
        }

        #endregion

        #region SCIM APIs

        /// <summary>
        /// Obtém usuários via SCIM API
        /// </summary>
        public async Task<object> GetScimUsersAsync(int pageSize = 25, int pageNumber = 1, string? filter = null)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"count={pageSize}",
                    $"startIndex={((pageNumber - 1) * pageSize) + 1}"
                };

                if (!string.IsNullOrEmpty(filter))
                {
                    queryParams.Add($"filter={Uri.EscapeDataString(filter)}");
                }

                var url = $"api/v2/scim/users?{string.Join("&", queryParams)}";
                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var users = new List<object>();
                    var totalResults = 0;

                    if (root.TryGetProperty("Resources", out var resourcesElement))
                    {
                        foreach (var user in resourcesElement.EnumerateArray())
                        {
                            // Converter diretamente para objeto simples para evitar referências circulares
                            var userJson = user.GetRawText();
                            var userObject = Newtonsoft.Json.JsonConvert.DeserializeObject(userJson);
                            users.Add(userObject);
                        }
                    }

                    if (root.TryGetProperty("totalResults", out var totalElement))
                    {
                        totalResults = totalElement.GetInt32();
                    }

                    return new
                    {
                        users = users,
                        totalResults = totalResults,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud SCIM API"
                    };
                }
                else
                {
                    return new
                    {
                        users = new List<object>(),
                        totalResults = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud SCIM API",
                        message = "No SCIM users found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving SCIM users from Genesys Cloud");
                return new
                {
                    users = new List<object>(),
                    totalResults = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud SCIM API",
                    error = ex.Message,
                    message = "Error retrieving SCIM users"
                };
            }
        }

        /// <summary>
        /// Cria um usuário via SCIM API
        /// </summary>
        public async Task<object> CreateScimUserAsync(object userData)
        {
            try
            {
                var result = await MakeApiCallAsync("api/v2/scim/users", HttpMethod.Post, userData);

                if (!string.IsNullOrEmpty(result))
                {
                    return JsonSerializer.Deserialize<object>(result);
                }
                else
                {
                    throw new Exception("Failed to create SCIM user");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating SCIM user in Genesys Cloud");
                throw;
            }
        }

        /// <summary>
        /// Atualiza um usuário via SCIM API
        /// </summary>
        public async Task<object> UpdateScimUserAsync(string userId, object userData)
        {
            try
            {
                var result = await MakeApiCallAsync($"api/v2/scim/users/{Uri.EscapeDataString(userId)}", HttpMethod.Put, userData);

                if (!string.IsNullOrEmpty(result))
                {
                    return JsonSerializer.Deserialize<object>(result);
                }
                else
                {
                    throw new Exception($"Failed to update SCIM user {userId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating SCIM user {UserId} in Genesys Cloud", userId);
                throw;
            }
        }

        #endregion

        #region Workitems APIs

        /// <summary>
        /// Obtém workitems do Genesys Cloud
        /// </summary>
        public async Task<object> GetWorkitemsAsync(int pageSize = 25, int pageNumber = 1, string? workbinId = null)
        {
            try
            {
                // Construir o corpo da requisição para a API de query de workitems
                var requestBody = new
                {
                    pageSize = pageSize,
                    pageNumber = pageNumber
                };

                // Adicionar workbinId se fornecido
                object finalRequestBody;
                if (!string.IsNullOrEmpty(workbinId))
                {
                    finalRequestBody = new
                    {
                        pageSize = requestBody.pageSize,
                        pageNumber = requestBody.pageNumber,
                        filters = new[]
                        {
                            new
                            {
                                name = "workbinId",
                                type = "String",
                                @operator = "EQ",
                                values = new[] { workbinId }
                            }
                        }
                    };
                }
                else
                {
                    finalRequestBody = requestBody;
                }

                var url = "api/v2/taskmanagement/workitems/query";
                var result = await MakeApiCallWithOptionalResultAsync(url, HttpMethod.Post, finalRequestBody);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var workitems = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var workitem in entitiesElement.EnumerateArray())
                        {
                            // Converter diretamente para objeto simples para evitar referências circulares
                            var workitemJson = workitem.GetRawText();
                            var workitemObject = Newtonsoft.Json.JsonConvert.DeserializeObject(workitemJson);
                            workitems.Add(workitemObject);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        workitems = workitems,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Task Management API"
                    };
                }
                else
                {
                    return new
                    {
                        workitems = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Task Management API",
                        message = "No workitems found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving workitems from Genesys Cloud");
                return new
                {
                    workitems = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Task Management API",
                    error = ex.Message,
                    message = "Error retrieving workitems"
                };
            }
        }

        /// <summary>
        /// Cria um workitem no Genesys Cloud
        /// </summary>
        public async Task<object> CreateWorkitemAsync(object workitemData)
        {
            try
            {
                var result = await MakeApiCallAsync("api/v2/taskmanagement/workitems", HttpMethod.Post, workitemData);

                if (!string.IsNullOrEmpty(result))
                {
                    return JsonSerializer.Deserialize<object>(result);
                }
                else
                {
                    throw new Exception("Failed to create workitem");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating workitem in Genesys Cloud");
                throw;
            }
        }

        /// <summary>
        /// Atualiza um workitem no Genesys Cloud
        /// </summary>
        public async Task<object> UpdateWorkitemAsync(string workitemId, object workitemData)
        {
            try
            {
                var result = await MakeApiCallAsync($"api/v2/taskmanagement/workitems/{Uri.EscapeDataString(workitemId)}", HttpMethod.Patch, workitemData);

                if (!string.IsNullOrEmpty(result))
                {
                    return JsonSerializer.Deserialize<object>(result);
                }
                else
                {
                    throw new Exception($"Failed to update workitem {workitemId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating workitem {WorkitemId} in Genesys Cloud", workitemId);
                throw;
            }
        }

        #endregion

        #region Agent Copilot and Virtual Supervisor APIs

        /// <summary>
        /// Obtém configuração do Agent Copilot
        /// </summary>
        public async Task<object> GetCopilotConfigurationAsync()
        {
            try
            {
                var result = await MakeApiCallWithOptionalResultAsync("api/v2/copilot/configuration");

                if (result.Success)
                {
                    return JsonSerializer.Deserialize<object>(result.Content);
                }
                else
                {
                    return new
                    {
                        configuration = new object(),
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Copilot API",
                        message = "No copilot configuration found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving copilot configuration from Genesys Cloud");
                return new
                {
                    configuration = new object(),
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Copilot API",
                    error = ex.Message,
                    message = "Error retrieving copilot configuration"
                };
            }
        }

        /// <summary>
        /// Obtém configuração do Virtual Supervisor
        /// </summary>
        public async Task<object> GetVirtualSupervisorConfigurationAsync()
        {
            try
            {
                var result = await MakeApiCallWithOptionalResultAsync("api/v2/quality/evaluations/virtualsupervisor");

                if (result.Success)
                {
                    return JsonSerializer.Deserialize<object>(result.Content);
                }
                else
                {
                    return new
                    {
                        configuration = new object(),
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Virtual Supervisor API",
                        message = "No virtual supervisor configuration found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving virtual supervisor configuration from Genesys Cloud");
                return new
                {
                    configuration = new object(),
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Virtual Supervisor API",
                    error = ex.Message,
                    message = "Error retrieving virtual supervisor configuration"
                };
            }
        }

        /// <summary>
        /// Obtém insights do Agent Copilot
        /// </summary>
        public async Task<object> GetCopilotInsightsAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                var url = $"api/v2/copilot/insights?{string.Join("&", queryParams)}";
                var result = await MakeApiCallWithOptionalResultAsync(url);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var insights = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("entities", out var entitiesElement))
                    {
                        foreach (var insight in entitiesElement.EnumerateArray())
                        {
                            // Converter diretamente para objeto simples para evitar referências circulares
                            var insightJson = insight.GetRawText();
                            var insightObject = Newtonsoft.Json.JsonConvert.DeserializeObject(insightJson);
                            insights.Add(insightObject);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        insights = insights,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Copilot API"
                    };
                }
                else
                {
                    return new
                    {
                        insights = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Copilot API",
                        message = "No copilot insights found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving copilot insights from Genesys Cloud");
                return new
                {
                    insights = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Copilot API",
                    error = ex.Message,
                    message = "Error retrieving copilot insights"
                };
            }
        }

        #endregion

        #region Audit APIs

        /// <summary>
        /// Obtém eventos de auditoria
        /// </summary>
        public async Task<object> GetAuditEventsAsync(int pageSize = 25, int pageNumber = 1, string? serviceName = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                // Configurar intervalo de datas (padrão: últimas 24 horas)
                var start = startDate ?? DateTime.UtcNow.AddDays(-1);
                var end = endDate ?? DateTime.UtcNow;

                var requestBody = new
                {
                    interval = $"{start:yyyy-MM-ddTHH:mm:ss}/{end:yyyy-MM-ddTHH:mm:ss}",
                    pageSize = pageSize,
                    pageNumber = pageNumber
                };

                // Adicionar serviceName se fornecido
                object finalRequestBody;
                if (!string.IsNullOrEmpty(serviceName))
                {
                    finalRequestBody = new
                    {
                        interval = requestBody.interval,
                        pageSize = requestBody.pageSize,
                        pageNumber = requestBody.pageNumber,
                        serviceName = serviceName
                    };
                }
                else
                {
                    finalRequestBody = requestBody;
                }

                var url = "api/v2/audits/query/realtime";
                var result = await MakeApiCallWithOptionalResultAsync(url, HttpMethod.Post, finalRequestBody);

                if (result.Success)
                {
                    var jsonDoc = JsonDocument.Parse(result.Content);
                    var root = jsonDoc.RootElement;

                    var auditEvents = new List<object>();
                    var totalCount = 0;

                    if (root.TryGetProperty("auditMessages", out var messagesElement))
                    {
                        foreach (var auditEvent in messagesElement.EnumerateArray())
                        {
                            // Converter diretamente para objeto simples para evitar referências circulares
                            var auditEventJson = auditEvent.GetRawText();
                            var auditEventObject = Newtonsoft.Json.JsonConvert.DeserializeObject(auditEventJson);
                            auditEvents.Add(auditEventObject);
                        }
                    }

                    if (root.TryGetProperty("total", out var totalElement))
                    {
                        totalCount = totalElement.GetInt32();
                    }

                    return new
                    {
                        auditEvents = auditEvents,
                        totalCount = totalCount,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Audit API"
                    };
                }
                else
                {
                    return new
                    {
                        auditEvents = new List<object>(),
                        totalCount = 0,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Audit API",
                        message = "No audit events found"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving audit events from Genesys Cloud");
                return new
                {
                    auditEvents = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Audit API",
                    error = ex.Message,
                    message = "Error retrieving audit events"
                };
            }
        }

        /// <summary>
        /// Obtém eventos de auditoria para contatos externos
        /// </summary>
        public async Task<object> GetExternalContactsAuditEventsAsync(int pageSize = 25, int pageNumber = 1)
        {
            try
            {
                return await GetAuditEventsAsync(pageSize, pageNumber, "ExternalContacts");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving external contacts audit events from Genesys Cloud");
                return new
                {
                    auditEvents = new List<object>(),
                    totalCount = 0,
                    pageSize = pageSize,
                    pageNumber = pageNumber,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Audit API",
                    error = ex.Message,
                    message = "Error retrieving external contacts audit events"
                };
            }
        }

        #endregion

        #region Low Priority API Methods

        public async Task<object> GetFaxAsync(int pageSize = 25, int pageNumber = 1, string? documentType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys fax documents - Page: {PageNumber}, Size: {PageSize}, Type: {DocumentType}",
                    pageNumber, pageSize, documentType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(documentType))
                {
                    queryParams.Add($"documentType={documentType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/fax/documents?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Fax API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Fax API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving fax documents"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys fax documents");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Fax API",
                    error = ex.Message,
                    message = "Error retrieving fax documents"
                };
            }
        }

        public async Task<object> GetGreetingsAsync(int pageSize = 25, int pageNumber = 1, string? greetingType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys greetings - Page: {PageNumber}, Size: {PageSize}, Type: {GreetingType}",
                    pageNumber, pageSize, greetingType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(greetingType))
                {
                    queryParams.Add($"type={greetingType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/architect/prompts?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Greetings API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Greetings API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving greetings"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys greetings");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Greetings API",
                    error = ex.Message,
                    message = "Error retrieving greetings"
                };
            }
        }

        public async Task<object> GetCommandLineInterfaceAsync(int pageSize = 25, int pageNumber = 1, string? commandType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys CLI commands - Page: {PageNumber}, Size: {PageSize}, Type: {CommandType}",
                    pageNumber, pageSize, commandType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(commandType))
                {
                    queryParams.Add($"type={commandType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/configuration/schemas?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud CLI API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud CLI API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving CLI commands"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys CLI commands");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud CLI API",
                    error = ex.Message,
                    message = "Error retrieving CLI commands"
                };
            }
        }

        public async Task<object> GetMessagingAsync(int pageSize = 25, int pageNumber = 1, string? messageType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys messaging - Page: {PageNumber}, Size: {PageSize}, Type: {MessageType}",
                    pageNumber, pageSize, messageType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(messageType))
                {
                    queryParams.Add($"messageType={messageType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/conversations/messaging/integrations?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Messaging API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Messaging API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving messaging data"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys messaging");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Messaging API",
                    error = ex.Message,
                    message = "Error retrieving messaging data"
                };
            }
        }

        public async Task<object> GetWidgetsAsync(int pageSize = 25, int pageNumber = 1, string? widgetType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys widgets - Page: {PageNumber}, Size: {PageSize}, Type: {WidgetType}",
                    pageNumber, pageSize, widgetType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(widgetType))
                {
                    queryParams.Add($"widgetType={widgetType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/widgets/deployments?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Widgets API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Widgets API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving widgets"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys widgets");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Widgets API",
                    error = ex.Message,
                    message = "Error retrieving widgets"
                };
            }
        }

        public async Task<object> GetTokensAsync(int pageSize = 25, int pageNumber = 1, string? tokenType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys tokens - Page: {PageNumber}, Size: {PageSize}, Type: {TokenType}",
                    pageNumber, pageSize, tokenType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(tokenType))
                {
                    queryParams.Add($"type={tokenType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/tokens/me?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Tokens API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Tokens API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving tokens"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys tokens");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Tokens API",
                    error = ex.Message,
                    message = "Error retrieving tokens"
                };
            }
        }

        public async Task<object> GetUsageAsync(int pageSize = 25, int pageNumber = 1, string? usageType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys usage - Page: {PageNumber}, Size: {PageSize}, Type: {UsageType}",
                    pageNumber, pageSize, usageType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(usageType))
                {
                    queryParams.Add($"usageType={usageType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/usage/query/executionresults?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Usage API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Usage API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving usage data"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys usage");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Usage API",
                    error = ex.Message,
                    message = "Error retrieving usage data"
                };
            }
        }

        public async Task<object> GetUploadsAsync(int pageSize = 25, int pageNumber = 1, string? uploadType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys uploads - Page: {PageNumber}, Size: {PageSize}, Type: {UploadType}",
                    pageNumber, pageSize, uploadType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(uploadType))
                {
                    queryParams.Add($"uploadType={uploadType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/uploads?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Uploads API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Uploads API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving uploads"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys uploads");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Uploads API",
                    error = ex.Message,
                    message = "Error retrieving uploads"
                };
            }
        }

        public async Task<object> GetTextbotsAsync(int pageSize = 25, int pageNumber = 1, string? botType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys textbots - Page: {PageNumber}, Size: {PageSize}, Type: {BotType}",
                    pageNumber, pageSize, botType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(botType))
                {
                    queryParams.Add($"botType={botType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/textbots?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Textbots API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Textbots API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving textbots"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys textbots");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Textbots API",
                    error = ex.Message,
                    message = "Error retrieving textbots"
                };
            }
        }

        public async Task<object> GetSearchAsync(int pageSize = 25, int pageNumber = 1, string? query = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys search results - Page: {PageNumber}, Size: {PageSize}, Query: {Query}",
                    pageNumber, pageSize, query);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(query))
                {
                    queryParams.Add($"q={query}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/search?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Search API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Search API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving search results"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys search results");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Search API",
                    error = ex.Message,
                    message = "Error retrieving search results"
                };
            }
        }

        public async Task<object> GetResponseManagementAsync(int pageSize = 25, int pageNumber = 1, string? responseType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys response management - Page: {PageNumber}, Size: {PageSize}, Type: {ResponseType}",
                    pageNumber, pageSize, responseType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(responseType))
                {
                    queryParams.Add($"responseType={responseType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/responsemanagement/responses?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Response Management API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Response Management API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving response management data"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys response management");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Response Management API",
                    error = ex.Message,
                    message = "Error retrieving response management data"
                };
            }
        }

        public async Task<object> GetProcessAutomationAsync(int pageSize = 25, int pageNumber = 1, string? processType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys process automation - Page: {PageNumber}, Size: {PageSize}, Type: {ProcessType}",
                    pageNumber, pageSize, processType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(processType))
                {
                    queryParams.Add($"processType={processType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/processautomation/triggers?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Process Automation API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Process Automation API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving process automation data"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys process automation");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Process Automation API",
                    error = ex.Message,
                    message = "Error retrieving process automation data"
                };
            }
        }

        public async Task<object> GetMarketplaceAsync(int pageSize = 25, int pageNumber = 1, string? itemType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys marketplace - Page: {PageNumber}, Size: {PageSize}, Type: {ItemType}",
                    pageNumber, pageSize, itemType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(itemType))
                {
                    queryParams.Add($"itemType={itemType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/integrations/types?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Marketplace API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Marketplace API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving marketplace items"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys marketplace");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Marketplace API",
                    error = ex.Message,
                    message = "Error retrieving marketplace items"
                };
            }
        }

        public async Task<object> GetLanguageUnderstandingAsync(int pageSize = 25, int pageNumber = 1, string? language = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys language understanding - Page: {PageNumber}, Size: {PageSize}, Language: {Language}",
                    pageNumber, pageSize, language);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(language))
                {
                    queryParams.Add($"language={language}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/languageunderstanding/domains?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Language Understanding API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Language Understanding API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving language understanding data"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys language understanding");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Language Understanding API",
                    error = ex.Message,
                    message = "Error retrieving language understanding data"
                };
            }
        }

        public async Task<object> GetIdentityProvidersAsync(int pageSize = 25, int pageNumber = 1, string? providerType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys identity providers - Page: {PageNumber}, Size: {PageSize}, Type: {ProviderType}",
                    pageNumber, pageSize, providerType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(providerType))
                {
                    queryParams.Add($"type={providerType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/identityproviders?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Identity Providers API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Identity Providers API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving identity providers"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys identity providers");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Identity Providers API",
                    error = ex.Message,
                    message = "Error retrieving identity providers"
                };
            }
        }

        public async Task<object> GetEventsAsync(int pageSize = 25, int pageNumber = 1, string? eventType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys events - Page: {PageNumber}, Size: {PageSize}, Type: {EventType}",
                    pageNumber, pageSize, eventType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(eventType))
                {
                    queryParams.Add($"eventType={eventType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/audits/query/realtime?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Events API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Events API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving events"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys events");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Events API",
                    error = ex.Message,
                    message = "Error retrieving events"
                };
            }
        }

        public async Task<object> GetEmailAsync(int pageSize = 25, int pageNumber = 1, string? emailType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys email - Page: {PageNumber}, Size: {PageSize}, Type: {EmailType}",
                    pageNumber, pageSize, emailType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(emailType))
                {
                    queryParams.Add($"emailType={emailType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/routing/email/domains?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Email API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Email API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving email data"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys email");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Email API",
                    error = ex.Message,
                    message = "Error retrieving email data"
                };
            }
        }

        public async Task<object> GetDataTablesAsync(int pageSize = 25, int pageNumber = 1, string? tableType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys data tables - Page: {PageNumber}, Size: {PageSize}, Type: {TableType}",
                    pageNumber, pageSize, tableType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(tableType))
                {
                    queryParams.Add($"name={tableType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/flows/datatables?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Data Tables API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Data Tables API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving data tables"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys data tables");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Data Tables API",
                    error = ex.Message,
                    message = "Error retrieving data tables"
                };
            }
        }

        public async Task<object> GetCertificatesAsync(int pageSize = 25, int pageNumber = 1, string? certificateType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys certificates - Page: {PageNumber}, Size: {PageSize}, Type: {CertificateType}",
                    pageNumber, pageSize, certificateType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(certificateType))
                {
                    queryParams.Add($"type={certificateType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/certificates?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Certificates API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Certificates API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving certificates"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys certificates");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Certificates API",
                    error = ex.Message,
                    message = "Error retrieving certificates"
                };
            }
        }

        public async Task<object> GetAttributesAsync(int pageSize = 25, int pageNumber = 1, string? attributeType = null)
        {
            try
            {
                _logger.LogInformation("Getting Genesys attributes - Page: {PageNumber}, Size: {PageSize}, Type: {AttributeType}",
                    pageNumber, pageSize, attributeType);

                var queryParams = new List<string>
                {
                    $"pageSize={pageSize}",
                    $"pageNumber={pageNumber}"
                };

                if (!string.IsNullOrEmpty(attributeType))
                {
                    queryParams.Add($"type={attributeType}");
                }

                var response = await _httpClient.GetAsync($"/api/v2/routing/wrapupcodes?{string.Join("&", queryParams)}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<object>(content);

                    return new
                    {
                        success = true,
                        data = data,
                        extractionTimestamp = DateTime.UtcNow,
                        extractionSource = "Genesys Cloud Attributes API"
                    };
                }

                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Attributes API",
                    error = $"HTTP {response.StatusCode}",
                    message = "Error retrieving attributes"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Genesys attributes");
                return new
                {
                    success = false,
                    extractionTimestamp = DateTime.UtcNow,
                    extractionSource = "Genesys Cloud Attributes API",
                    error = ex.Message,
                    message = "Error retrieving attributes"
                };
            }
        }

        #endregion

    }
}
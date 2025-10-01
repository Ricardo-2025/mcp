using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GenesysMigrationMCP.Test
{
    public class GenesysDirectTest
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        public static async Task TestGenesysConnection()
        {
            try
            {
                Console.WriteLine("=== TESTE DIRETO DA API GENESYS ===");
                
                // Credenciais
                var clientId = "557cefc3-b118-40ef-8a78-ba4ec837b2fd";
                var clientSecret = "NWUpd2PXrb3mA3MRTv6pBQ6zjm0r0GNnYw3bmSBp1po";
                var authUrl = "https://login.usw2.pure.cloud/oauth/token";
                var apiUrl = "https://api.usw2.pure.cloud";
                
                Console.WriteLine($"ClientId: {clientId}");
                Console.WriteLine($"AuthUrl: {authUrl}");
                Console.WriteLine($"ApiUrl: {apiUrl}");
                
                // 1. Obter token
                Console.WriteLine("\n1. Obtendo token OAuth2...");
                var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                var request = new HttpRequestMessage(HttpMethod.Post, authUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);
                request.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                });

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                Console.WriteLine($"Status: {response.StatusCode}");
                Console.WriteLine($"Response: {responseContent}");
                
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("ERRO: Falha na autenticação");
                    return;
                }
                
                var tokenData = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var accessToken = tokenData.GetProperty("access_token").GetString();
                
                Console.WriteLine($"Token obtido: {accessToken?.Substring(0, 20)}...");
                
                // 2. Testar API de usuários
                Console.WriteLine("\n2. Testando API de usuários...");
                var apiRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}/api/v2/users?pageSize=5");
                apiRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                
                var apiResponse = await _httpClient.SendAsync(apiRequest);
                var apiResponseContent = await apiResponse.Content.ReadAsStringAsync();
                
                Console.WriteLine($"Status: {apiResponse.StatusCode}");
                Console.WriteLine($"Response Length: {apiResponseContent.Length}");
                
                if (apiResponse.IsSuccessStatusCode)
                {
                    var usersData = JsonSerializer.Deserialize<JsonElement>(apiResponseContent);
                    if (usersData.TryGetProperty("entities", out var entities))
                    {
                        Console.WriteLine($"Usuários encontrados: {entities.GetArrayLength()}");
                        foreach (var user in entities.EnumerateArray())
                        {
                            var name = user.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "N/A";
                            var email = user.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : "N/A";
                            Console.WriteLine($"  - {name} ({email})");
                        }
                    }
                    else
                    {
                        Console.WriteLine("ERRO: Resposta não contém 'entities'");
                    }
                }
                else
                {
                    Console.WriteLine($"ERRO: {apiResponseContent}");
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EXCEÇÃO: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");
            }
        }
    }
}
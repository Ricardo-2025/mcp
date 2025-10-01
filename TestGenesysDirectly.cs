using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GenesysMigrationMCP.Services;

namespace GenesysMigrationMCP.Test
{
    public class TestGenesysDirectly
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== TESTE DIRETO DO GENESYS CLIENT ===");
            
            // Configurar serviços
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            services.AddHttpClient();
            
            // Configuração manual
            var configData = new Dictionary<string, string>
            {
                {"GenesysCloud__ClientId", "557cefc3-b118-40ef-8a78-ba4ec837b2fd"},
                {"GenesysCloud__ClientSecret", "NWUpd2PXrb3mA3MRTv6pBQ6zjm0r0GNnYw3bmSBp1po"},
                {"GenesysCloud__ApiUrl", "https://api.usw2.pure.cloud"},
                {"GenesysCloud__AuthUrl", "https://login.usw2.pure.cloud/oauth/token"}
            };
            
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();
            
            services.AddSingleton<IConfiguration>(configuration);
            
            // Registrar GenesysCloudClient
            services.AddScoped<GenesysCloudClient>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var httpClient = provider.GetRequiredService<HttpClient>();
                var logger = provider.GetRequiredService<ILogger<GenesysCloudClient>>();
                return new GenesysCloudClient(httpClient, config, logger);
            });
            
            var serviceProvider = services.BuildServiceProvider();
            
            try
            {
                var genesysClient = serviceProvider.GetRequiredService<GenesysCloudClient>();
                
                Console.WriteLine("Cliente criado. Testando GetUsersAsync...");
                
                var result = await genesysClient.GetUsersAsync();
                
                Console.WriteLine($"Resultado: {result?.GetType().Name ?? "null"}");
                
                if (result != null)
                {
                    Console.WriteLine($"Conteúdo: {result}");
                }
                else
                {
                    Console.WriteLine("ERRO: Resultado é null");
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
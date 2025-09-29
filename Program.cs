using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GenesysMigrationMCP.Services;
using GenesysMigrationMCP.Models;
using GenesysMigrationMCP.Middleware;
using GenesysMigrationMCP.Configuration;
using System.Net.Http;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker =>
    {
        // Registrar middlewares na ordem correta
        worker.UseMiddleware<MonitoringMiddleware>();
        worker.UseMiddleware<RateLimitingMiddleware>();
        worker.UseMiddleware<AuthenticationMiddleware>();
    })
    .ConfigureAppConfiguration((context, config) =>
    {
        // 1) JSON primeiro...
        var settingsPath = Path.Combine(context.HostingEnvironment.ContentRootPath, "local.settings.json");
        if (File.Exists(settingsPath))
        {
            config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);
        }

        // ...2) variáveis de ambiente por último (têm precedência)
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // Helper para ler chaves em todos os formatos usuais
        static string Cfg(IConfiguration cfg, string section, string key)
            => cfg[$"{section}:{key}"]
                ?? cfg[$"{section}__{key}"]
                ?? cfg[$"Values:{section}:{key}"]
                ?? cfg[$"Values:{section}__{key}"]
                ?? string.Empty;

        // Debug: Genesys
        var gcClientId = Cfg(configuration, "GenesysCloud", "ClientId");
        var gcClientSecret = Cfg(configuration, "GenesysCloud", "ClientSecret");
        Console.WriteLine($"Debug - GenesysCloud:ClientId: {(string.IsNullOrEmpty(gcClientId) ? "VAZIO" : "CONFIGURADO")}");
        Console.WriteLine($"Debug - GenesysCloud:ClientSecret: {(string.IsNullOrEmpty(gcClientSecret) ? "VAZIO" : "CONFIGURADO")}");

        // Debug: Dynamics (alinha com o que o DynamicsClient vai ler)
        var dynTenantId = Cfg(configuration, "Dynamics", "TenantId");
        var dynClientId = Cfg(configuration, "Dynamics", "ClientId");
        var dynBaseUrl = Cfg(configuration, "Dynamics", "BaseUrl");
        var dynOrgUrl = Cfg(configuration, "Dynamics", "OrganizationUrl"); // fallback
        var dynCertPath = Cfg(configuration, "Dynamics", "CertificatePath");
        var dynCertThumb = Cfg(configuration, "Dynamics", "CertificateThumbprint");

        var dynUrlToShow = string.IsNullOrWhiteSpace(dynBaseUrl) ? dynOrgUrl : dynBaseUrl;
        Console.WriteLine($"Debug - Dynamics:TenantId: {(string.IsNullOrEmpty(dynTenantId) ? "VAZIO" : "CONFIGURADO")}");
        Console.WriteLine($"Debug - Dynamics:ClientId: {(string.IsNullOrEmpty(dynClientId) ? "VAZIO" : "CONFIGURADO")}");
        Console.WriteLine($"Debug - Dynamics:Base/Org Url: {(string.IsNullOrEmpty(dynUrlToShow) ? "VAZIO" : dynUrlToShow)}");
        Console.WriteLine($"Debug - Dynamics:CertificatePath: {(string.IsNullOrEmpty(dynCertPath) ? "VAZIO" : dynCertPath)}");
        Console.WriteLine($"Debug - Dynamics:CertificateThumbprint: {(string.IsNullOrEmpty(dynCertThumb) ? "VAZIO" : "CONFIGURADO")}");

        // HttpClient.
        services.AddHttpClient();

        // Logging.
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

        // Configuração de segurança.
        services.AddSecurityConfiguration(configuration);
        services.AddScoped<ISecurityService, SecurityService>();

        // Serviços de logging/monitoramento.
        services.AddScoped<ILoggingService, LoggingService>();

        // Registrar clientes com factory (remova o AddScoped<> duplicado sem factory).
        services.AddScoped<GenesysCloudClient>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var httpClient = provider.GetRequiredService<HttpClient>();
            var logger = provider.GetRequiredService<ILogger<GenesysCloudClient>>();
            return new GenesysCloudClient(httpClient, config, logger);
        });

        services.AddScoped<DynamicsClient>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var httpClient = provider.GetRequiredService<HttpClient>();
            var logger = provider.GetRequiredService<ILogger<DynamicsClient>>();
            return new DynamicsClient(httpClient, config, logger);
        });

        // Serviços MCP
        services.AddScoped<IMcpService, McpService>();
        services.AddScoped<IMigrationOrchestrator, MigrationOrchestrator>();

        // Configuração MCP
        services.Configure<McpConfiguration>(configuration.GetSection("MCP"));
    })
    .Build();

host.Run();

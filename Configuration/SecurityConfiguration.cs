using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenesysMigrationMCP.Configuration
{
    /// <summary>
    /// Configurações de segurança para a Azure Function MCP
    /// </summary>
    public class SecurityConfiguration
    {
        public string[] ValidApiKeys { get; set; } = Array.Empty<string>();
        public string[] PublicEndpoints { get; set; } = Array.Empty<string>();
        public RateLimitConfiguration RateLimit { get; set; } = new();
        public CorsConfiguration Cors { get; set; } = new();
        public EncryptionConfiguration Encryption { get; set; } = new();
    }

    public class RateLimitConfiguration
    {
        public int MaxRequestsPerMinute { get; set; } = 60;
        public int MaxRequestsPerHour { get; set; } = 1000;
        public int MaxRequestsPerDay { get; set; } = 10000;
        public bool EnableRateLimit { get; set; } = true;
    }

    public class CorsConfiguration
    {
        public string[] AllowedOrigins { get; set; } = { "*" };
        public string[] AllowedMethods { get; set; } = { "GET", "POST", "PUT", "DELETE", "OPTIONS" };
        public string[] AllowedHeaders { get; set; } = { "*" };
        public bool AllowCredentials { get; set; } = false;
        public int MaxAge { get; set; } = 86400; // 24 horas
    }

    public class EncryptionConfiguration
    {
        public string EncryptionKey { get; set; } = string.Empty;
        public string HashSalt { get; set; } = string.Empty;
        public bool EnableEncryption { get; set; } = true;
    }

    /// <summary>
    /// Extensões para configuração de segurança
    /// </summary>
    public static class SecurityConfigurationExtensions
    {
        public static IServiceCollection AddSecurityConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            var securityConfig = new SecurityConfiguration();
            configuration.GetSection("Security").Bind(securityConfig);

            // Configurar chaves de API a partir de variáveis de ambiente
            var apiKeys = new List<string>();
            
            // Chave principal
            var mainApiKey = Environment.GetEnvironmentVariable("MCP_API_KEY");
            if (!string.IsNullOrEmpty(mainApiKey))
            {
                apiKeys.Add(mainApiKey);
            }

            // Chave de administrador
            var adminApiKey = Environment.GetEnvironmentVariable("MCP_ADMIN_KEY");
            if (!string.IsNullOrEmpty(adminApiKey))
            {
                apiKeys.Add(adminApiKey);
            }

            // Chaves adicionais (separadas por vírgula)
            var additionalKeys = Environment.GetEnvironmentVariable("MCP_ADDITIONAL_KEYS");
            if (!string.IsNullOrEmpty(additionalKeys))
            {
                apiKeys.AddRange(additionalKeys.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim()));
            }

            // Se não houver chaves configuradas, usar chaves padrão para desenvolvimento
            if (apiKeys.Count == 0)
            {
                apiKeys.AddRange(new[] { "default-dev-key-123", "admin-dev-key-456" });
            }

            securityConfig.ValidApiKeys = apiKeys.ToArray();

            // Configurar endpoints públicos
            if (securityConfig.PublicEndpoints.Length == 0)
            {
                securityConfig.PublicEndpoints = new[]
                {
                    "/mcp/info",
                    "/mcp/capabilities",
                    "/mcp/endpoints",
                    "/mcp/health"
                };
            }

            // Configurar rate limiting a partir de variáveis de ambiente
            if (int.TryParse(Environment.GetEnvironmentVariable("MCP_RATE_LIMIT_PER_MINUTE"), out var perMinute))
            {
                securityConfig.RateLimit.MaxRequestsPerMinute = perMinute;
            }

            if (int.TryParse(Environment.GetEnvironmentVariable("MCP_RATE_LIMIT_PER_HOUR"), out var perHour))
            {
                securityConfig.RateLimit.MaxRequestsPerHour = perHour;
            }

            // Configurar CORS
            var allowedOrigins = Environment.GetEnvironmentVariable("MCP_CORS_ORIGINS");
            if (!string.IsNullOrEmpty(allowedOrigins))
            {
                securityConfig.Cors.AllowedOrigins = allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(o => o.Trim()).ToArray();
            }

            // Configurar criptografia
            securityConfig.Encryption.EncryptionKey = Environment.GetEnvironmentVariable("MCP_ENCRYPTION_KEY") ?? GenerateRandomKey();
            securityConfig.Encryption.HashSalt = Environment.GetEnvironmentVariable("MCP_HASH_SALT") ?? GenerateRandomSalt();

            services.AddSingleton(securityConfig);
            return services;
        }

        private static string GenerateRandomKey()
        {
            var random = new Random();
            var key = new byte[32]; // 256 bits
            random.NextBytes(key);
            return Convert.ToBase64String(key);
        }

        private static string GenerateRandomSalt()
        {
            var random = new Random();
            var salt = new byte[16]; // 128 bits
            random.NextBytes(salt);
            return Convert.ToBase64String(salt);
        }
    }

    /// <summary>
    /// Serviço para validação de segurança
    /// </summary>
    public interface ISecurityService
    {
        bool IsValidApiKey(string apiKey);
        bool IsPublicEndpoint(string path);
        string HashSensitiveData(string data);
        string EncryptSensitiveData(string data);
        string DecryptSensitiveData(string encryptedData);
    }

    public class SecurityService : ISecurityService
    {
        private readonly SecurityConfiguration _config;
        private readonly ILogger<SecurityService> _logger;

        public SecurityService(SecurityConfiguration config, ILogger<SecurityService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public bool IsValidApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return false;

            return _config.ValidApiKeys.Contains(apiKey);
        }

        public bool IsPublicEndpoint(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return _config.PublicEndpoints.Any(endpoint => 
                path.StartsWith(endpoint, StringComparison.OrdinalIgnoreCase));
        }

        public string HashSensitiveData(string data)
        {
            if (string.IsNullOrEmpty(data))
                return string.Empty;

            try
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var saltedData = data + _config.Encryption.HashSalt;
                var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(saltedData));
                return Convert.ToBase64String(hashBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao fazer hash dos dados sensíveis");
                return string.Empty;
            }
        }

        public string EncryptSensitiveData(string data)
        {
            if (string.IsNullOrEmpty(data) || !_config.Encryption.EnableEncryption)
                return data;

            try
            {
                // Implementação básica - em produção, usar Azure Key Vault
                var keyBytes = Convert.FromBase64String(_config.Encryption.EncryptionKey);
                using var aes = System.Security.Cryptography.Aes.Create();
                aes.Key = keyBytes;
                aes.GenerateIV();

                using var encryptor = aes.CreateEncryptor();
                var dataBytes = System.Text.Encoding.UTF8.GetBytes(data);
                var encryptedBytes = encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);
                
                // Combinar IV + dados criptografados
                var result = new byte[aes.IV.Length + encryptedBytes.Length];
                Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
                Array.Copy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);
                
                return Convert.ToBase64String(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criptografar dados sensíveis");
                return data; // Retornar dados originais em caso de erro
            }
        }

        public string DecryptSensitiveData(string encryptedData)
        {
            if (string.IsNullOrEmpty(encryptedData) || !_config.Encryption.EnableEncryption)
                return encryptedData;

            try
            {
                var keyBytes = Convert.FromBase64String(_config.Encryption.EncryptionKey);
                var dataBytes = Convert.FromBase64String(encryptedData);
                
                using var aes = System.Security.Cryptography.Aes.Create();
                aes.Key = keyBytes;
                
                // Extrair IV dos primeiros 16 bytes
                var iv = new byte[16];
                Array.Copy(dataBytes, 0, iv, 0, 16);
                aes.IV = iv;
                
                // Extrair dados criptografados
                var encryptedBytes = new byte[dataBytes.Length - 16];
                Array.Copy(dataBytes, 16, encryptedBytes, 0, encryptedBytes.Length);
                
                using var decryptor = aes.CreateDecryptor();
                var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
                
                return System.Text.Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao descriptografar dados sensíveis");
                return encryptedData; // Retornar dados originais em caso de erro
            }
        }
    }
}
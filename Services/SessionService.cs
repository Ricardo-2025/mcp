using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenesysMigrationMCP.Services
{
    public class SessionService : ISessionService, IDisposable
    {
        private readonly ILogger<SessionService> _logger;
        private readonly SessionConfiguration _config;
        private readonly ConcurrentDictionary<string, SessionInfo> _sessions;
        private readonly Timer _cleanupTimer;
        private readonly object _lockObject = new object();

        public SessionService(ILogger<SessionService> logger, IOptions<SessionConfiguration> config)
        {
            _logger = logger;
            _config = config.Value;
            _sessions = new ConcurrentDictionary<string, SessionInfo>();
            
            // Timer para limpeza automática de sessões expiradas
            _cleanupTimer = new Timer(async _ => await CleanExpiredSessionsAsync(), 
                null, 
                TimeSpan.FromMinutes(5), // primeira execução em 5 minutos
                TimeSpan.FromMinutes(_config.CleanupIntervalMinutes)); // depois a cada intervalo configurado
        }

        public async Task<string> CreateSessionAsync()
        {
            var sessionId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            
            var sessionInfo = new SessionInfo
            {
                SessionId = sessionId,
                CreatedAt = now,
                LastActivity = now,
                IsActive = true
            };

            _sessions.TryAdd(sessionId, sessionInfo);
            
            _logger.LogInformation("Nova sessão criada: {SessionId}", sessionId);
            
            return await Task.FromResult(sessionId);
        }

        public async Task<bool> ValidateSessionAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return await Task.FromResult(false);
            }

            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                _logger.LogWarning("Tentativa de acesso à sessão inexistente: {SessionId}", sessionId);
                return await Task.FromResult(false);
            }

            var now = DateTime.UtcNow;
            var isExpired = (now - session.LastActivity).TotalMinutes > _config.SessionTimeoutMinutes;
            
            if (isExpired)
            {
                _logger.LogInformation("Sessão expirada removida: {SessionId}", sessionId);
                _sessions.TryRemove(sessionId, out _);
                return await Task.FromResult(false);
            }

            return await Task.FromResult(session.IsActive);
        }

        public async Task UpdateSessionActivityAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.LastActivity = DateTime.UtcNow;
                _logger.LogDebug("Atividade da sessão atualizada: {SessionId}", sessionId);
            }
            
            await Task.CompletedTask;
        }

        public async Task RemoveSessionAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            if (_sessions.TryRemove(sessionId, out var session))
            {
                session.IsActive = false;
                _logger.LogInformation("Sessão removida: {SessionId}", sessionId);
            }
            
            await Task.CompletedTask;
        }

        public async Task CleanExpiredSessionsAsync()
        {
            var now = DateTime.UtcNow;
            var expiredSessions = new List<string>();

            foreach (var kvp in _sessions)
            {
                var session = kvp.Value;
                var timeSinceLastActivity = now - session.LastActivity;
                
                if (timeSinceLastActivity.TotalMinutes > _config.SessionTimeoutMinutes)
                {
                    expiredSessions.Add(kvp.Key);
                }
            }

            foreach (var sessionId in expiredSessions)
            {
                if (_sessions.TryRemove(sessionId, out var removedSession))
                {
                    removedSession.IsActive = false;
                    _logger.LogInformation("Sessão expirada removida automaticamente: {SessionId} (inativa há {Minutes} minutos)", 
                        sessionId, (now - removedSession.LastActivity).TotalMinutes);
                }
            }

            if (expiredSessions.Count > 0)
            {
                _logger.LogInformation("Limpeza de sessões concluída: {Count} sessões expiradas removidas", expiredSessions.Count);
            }

            await Task.CompletedTask;
        }

        public async Task<SessionInfo?> GetSessionInfoAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return await Task.FromResult<SessionInfo?>(null);
            }

            _sessions.TryGetValue(sessionId, out var session);
            return await Task.FromResult(session);
        }

        public async Task<IEnumerable<SessionInfo>> GetActiveSessionsAsync()
        {
            var activeSessions = _sessions.Values
                .Where(s => s.IsActive)
                .ToList();
                
            _logger.LogDebug("Sessões ativas: {Count}", activeSessions.Count);
            
            return await Task.FromResult(activeSessions);
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            
            // Log de sessões ativas no momento do dispose
            var activeSessions = _sessions.Values.Count(s => s.IsActive);
            if (activeSessions > 0)
            {
                _logger.LogInformation("Serviço de sessões sendo finalizado com {Count} sessões ativas", activeSessions);
            }
        }
    }

    public class SessionConfiguration
    {
        public int SessionTimeoutMinutes { get; set; } = 30; // 30 minutos por padrão
        public int CleanupIntervalMinutes { get; set; } = 10; // limpeza a cada 10 minutos
        public int MaxConcurrentSessions { get; set; } = 1000; // limite de sessões simultâneas
    }
}
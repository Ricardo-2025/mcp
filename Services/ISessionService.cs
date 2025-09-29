using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GenesysMigrationMCP.Services
{
    public interface ISessionService
    {
        /// <summary>
        /// Cria uma nova sessão
        /// </summary>
        Task<string> CreateSessionAsync();

        /// <summary>
        /// Valida se uma sessão existe e está ativa
        /// </summary>
        Task<bool> ValidateSessionAsync(string sessionId);

        /// <summary>
        /// Atualiza o timestamp da última atividade de uma sessão
        /// </summary>
        Task UpdateSessionActivityAsync(string sessionId);

        /// <summary>
        /// Remove uma sessão
        /// </summary>
        Task RemoveSessionAsync(string sessionId);

        /// <summary>
        /// Limpa sessões expiradas
        /// </summary>
        Task CleanExpiredSessionsAsync();

        /// <summary>
        /// Obtém informações da sessão
        /// </summary>
        Task<SessionInfo?> GetSessionInfoAsync(string sessionId);

        /// <summary>
        /// Lista todas as sessões ativas (para debug/admin)
        /// </summary>
        Task<IEnumerable<SessionInfo>> GetActiveSessionsAsync();
    }

    public class SessionInfo
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsActive { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
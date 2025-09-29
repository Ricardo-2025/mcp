using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GenesysMigrationMCP.Services
{
    public interface ISessionService
    {
        /// <summary>
        /// Cria uma nova sess�o
        /// </summary>
        Task<string> CreateSessionAsync();

        /// <summary>
        /// Valida se uma sess�o existe e est� ativa
        /// </summary>
        Task<bool> ValidateSessionAsync(string sessionId);

        /// <summary>
        /// Atualiza o timestamp da �ltima atividade de uma sess�o
        /// </summary>
        Task UpdateSessionActivityAsync(string sessionId);

        /// <summary>
        /// Remove uma sess�o
        /// </summary>
        Task RemoveSessionAsync(string sessionId);

        /// <summary>
        /// Limpa sess�es expiradas
        /// </summary>
        Task CleanExpiredSessionsAsync();

        /// <summary>
        /// Obt�m informa��es da sess�o
        /// </summary>
        Task<SessionInfo?> GetSessionInfoAsync(string sessionId);

        /// <summary>
        /// Lista todas as sess�es ativas (para debug/admin)
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
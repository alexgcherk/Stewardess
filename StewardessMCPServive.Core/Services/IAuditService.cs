using System.Threading;
using System.Threading.Tasks;
using StewardessMCPServive.Models;

namespace StewardessMCPServive.Services
{
    /// <summary>
    /// Persists an immutable audit trail for all service operations.
    /// Writes must be non-blocking and must never throw into the caller.
    /// </summary>
    public interface IAuditService
    {
        /// <summary>Appends an audit entry asynchronously.</summary>
        Task LogAsync(AuditEntry entry, CancellationToken ct = default);

        /// <summary>
        /// Convenience method: creates and appends an entry for a completed operation.
        /// </summary>
        Task LogOperationAsync(
            string requestId,
            string sessionId,
            AuditOperationType operationType,
            string operationName,
            string clientIp,
            string targetPath,
            AuditOutcome outcome,
            string errorCode,
            string description,
            long elapsedMs,
            string changeReason = null,
            string backupPath = null,
            CancellationToken ct = default);

        /// <summary>Queries the audit log with optional filters.</summary>
        Task<AuditLogQueryResponse> QueryAsync(AuditLogQueryRequest request, CancellationToken ct = default);
    }
}

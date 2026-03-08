using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Models;
using Newtonsoft.Json;

namespace StewardessMCPServive.Services
{
    /// <summary>
    /// Append-only file-based audit log.  Each entry is a single JSON line
    /// (NDJSON format) written to the configured audit log file.
    ///
    /// Thread-safety: writes are serialised through a <see cref="SemaphoreSlim"/>.
    /// </summary>
    public sealed class AuditService : IAuditService, IDisposable
    {
        private readonly string? _logPath;
        private readonly bool _enabled;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private static readonly McpLogger _log = McpLogger.For<AuditService>();

        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            NullValueHandling    = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc
        };

        /// <summary>Initialises a new instance of <see cref="AuditService"/>.</summary>
        public AuditService(McpServiceSettings settings)
        {
            _enabled = settings.EnableAuditLog;

            if (!_enabled) return;

            if (!string.IsNullOrWhiteSpace(settings.AuditLogPath))
            {
                _logPath = settings.AuditLogPath;
            }
            else if (!string.IsNullOrWhiteSpace(settings.RepositoryRoot))
            {
                _logPath = Path.Combine(settings.RepositoryRoot, ".mcp", "audit.log");
            }
            else
            {
                _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "audit.log");
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            }
            catch (Exception ex)
            {
                _log.Warn($"Could not create audit log directory: {ex.Message}");
                _enabled = false;
            }
        }

        // ── IAuditService ────────────────────────────────────────────────────────

        /// <inheritdoc />
        public async Task LogAsync(AuditEntry entry, CancellationToken ct = default)
        {
            if (!_enabled || entry == null) return;

            if (string.IsNullOrEmpty(entry.EntryId))
                entry.EntryId = Guid.NewGuid().ToString("N");

            if (entry.Timestamp == default)
                entry.Timestamp = DateTimeOffset.UtcNow;

            var line = JsonConvert.SerializeObject(entry, _jsonSettings) + Environment.NewLine;

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // File.AppendAllTextAsync with CancellationToken is .NET 6+ — use Task.Run on .NET 4.7.2.
                await Task.Run(() => File.AppendAllText(_logPath!, line, Encoding.UTF8), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Audit failures must never propagate to the caller.
                _log.Error($"Audit write failed: {ex.Message}");
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <inheritdoc />
        public Task LogOperationAsync(
            string requestId, string? sessionId, AuditOperationType operationType,
            string operationName, string clientIp, string? targetPath,
            AuditOutcome outcome, string? errorCode, string? description,
            long elapsedMs, string? changeReason = null, string? backupPath = null,
            CancellationToken ct = default)
        {
            var entry = new AuditEntry
            {
                EntryId       = Guid.NewGuid().ToString("N"),
                RequestId     = requestId,
                SessionId     = sessionId,
                Timestamp     = DateTimeOffset.UtcNow,
                OperationType = operationType,
                Source        = "REST",
                OperationName = operationName,
                ClientIp      = clientIp,
                TargetPath    = targetPath,
                Outcome       = outcome,
                ErrorCode     = errorCode,
                Description   = description,
                ChangeReason  = changeReason,
                BackupPath    = backupPath,
                ElapsedMs     = elapsedMs
            };

            return LogAsync(entry, ct);
        }

        /// <inheritdoc />
        public async Task<AuditLogQueryResponse> QueryAsync(AuditLogQueryRequest request, CancellationToken ct = default)
        {
            if (!_enabled || !File.Exists(_logPath))
                return new AuditLogQueryResponse { Entries = new List<AuditEntry>() };

            var lines = await Task.Run(() => File.ReadAllLines(_logPath!, Encoding.UTF8), ct).ConfigureAwait(false);
            var entries = new List<AuditEntry>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var e = JsonConvert.DeserializeObject<AuditEntry>(line, _jsonSettings);
                    if (e != null) entries.Add(e);
                }
                catch { /* skip malformed lines */ }
            }

            // Apply filters
            if (request.Since.HasValue)
                entries = entries.Where(e => e.Timestamp >= request.Since.Value).ToList();
            if (request.Until.HasValue)
                entries = entries.Where(e => e.Timestamp <= request.Until.Value).ToList();
            if (!string.IsNullOrEmpty(request.RequestId))
                entries = entries.Where(e => e.RequestId == request.RequestId).ToList();
            if (!string.IsNullOrEmpty(request.SessionId))
                entries = entries.Where(e => e.SessionId == request.SessionId).ToList();
            if (!string.IsNullOrEmpty(request.TargetPath))
                entries = entries.Where(e => e.TargetPath != null &&
                          e.TargetPath.IndexOf(request.TargetPath, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (request.OperationType.HasValue)
                entries = entries.Where(e => e.OperationType == request.OperationType.Value).ToList();
            if (request.Outcome.HasValue)
                entries = entries.Where(e => e.Outcome == request.Outcome.Value).ToList();

            entries = entries.OrderByDescending(e => e.Timestamp).ToList();

            var total = entries.Count;
            var page  = entries
                .Skip(request.PageIndex * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            return new AuditLogQueryResponse
            {
                Entries    = page,
                TotalCount = total,
                HasMore    = (request.PageIndex + 1) * request.PageSize < total
            };
        }

        /// <summary>Releases the write lock semaphore.</summary>
        public void Dispose() => _writeLock?.Dispose();
    }
}

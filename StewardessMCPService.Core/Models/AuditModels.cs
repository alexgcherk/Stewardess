// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.Models;
// ────────────────────────────────────────────────────────────────────────────
//  Audit log entry models
// ────────────────────────────────────────────────────────────────────────────

/// <summary>A single immutable audit log record for one service operation.</summary>
public sealed class AuditEntry
{
    /// <summary>Unique audit entry identifier (GUID).</summary>
    public string EntryId { get; set; } = null!;

    /// <summary>HTTP / MCP correlation ID propagated from the request.</summary>
    public string? RequestId { get; set; }

    /// <summary>Agent-supplied session identifier, if provided.</summary>
    public string? SessionId { get; set; }

    /// <summary>UTC timestamp of the operation.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Type of operation performed.</summary>
    public AuditOperationType OperationType { get; set; }

    /// <summary>Source of the operation: "REST" or "MCP".</summary>
    public string Source { get; set; } = null!;

    /// <summary>Tool name (MCP operations) or HTTP path (REST operations).</summary>
    public string OperationName { get; set; } = null!;

    /// <summary>Client IP address.</summary>
    public string ClientIp { get; set; } = null!;

    /// <summary>Relative file path targeted by the operation; null for non-file ops.</summary>
    public string? TargetPath { get; set; }

    /// <summary>Outcome of the operation.</summary>
    public AuditOutcome Outcome { get; set; }

    /// <summary>Error code when Outcome is Failure.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Human-readable description of what was done.</summary>
    public string Description { get; set; } = null!;

    /// <summary>Change reason supplied by the caller.</summary>
    public string? ChangeReason { get; set; }

    /// <summary>Path of the backup file created; null if no backup was taken.</summary>
    public string? BackupPath { get; set; }

    /// <summary>Duration of the operation in milliseconds.</summary>
    public long ElapsedMs { get; set; }

    /// <summary>Additional key-value metadata.</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>Broad category of the audited operation.</summary>
public enum AuditOperationType
{
    // Read operations
    /// <summary>File read.</summary>
    ReadFile,

    /// <summary>Directory listing.</summary>
    ReadDirectory,

    /// <summary>Text or file-name search.</summary>
    Search,

    /// <summary>File or directory metadata retrieval.</summary>
    GetMetadata,

    // Write operations
    /// <summary>File write (overwrite).</summary>
    WriteFile,

    /// <summary>New file creation.</summary>
    CreateFile,

    /// <summary>New directory creation.</summary>
    CreateDirectory,

    /// <summary>File deletion.</summary>
    DeleteFile,

    /// <summary>Directory deletion.</summary>
    DeleteDirectory,

    /// <summary>File or directory rename / move.</summary>
    RenameMove,

    /// <summary>Content appended to a file.</summary>
    AppendFile,

    /// <summary>Text replacement within a file.</summary>
    ReplaceText,

    /// <summary>Line-range replacement within a file.</summary>
    ReplaceLines,

    /// <summary>Unified diff patch applied to a file.</summary>
    PatchFile,

    /// <summary>Multi-file unified diff applied.</summary>
    ApplyDiff,

    /// <summary>Batch of multiple edit operations.</summary>
    BatchEdit,

    // Git
    /// <summary>Git status query.</summary>
    GitStatus,

    /// <summary>Git diff query.</summary>
    GitDiff,

    /// <summary>Git log query.</summary>
    GitLog,

    // Command execution
    /// <summary>Build command execution.</summary>
    RunBuild,

    /// <summary>Test command execution.</summary>
    RunTests,

    /// <summary>Custom allowed command execution.</summary>
    RunCommand,

    // Service / infrastructure
    /// <summary>Health-check probe.</summary>
    HealthCheck,

    /// <summary>Capabilities manifest query.</summary>
    CapabilitiesQuery,

    /// <summary>Rollback of a prior edit.</summary>
    Rollback,

    /// <summary>Operation type could not be determined.</summary>
    Unknown
}

/// <summary>Outcome of the audited operation.</summary>
public enum AuditOutcome
{
    /// <summary>Operation completed successfully.</summary>
    Success,

    /// <summary>Operation failed.</summary>
    Failure,

    /// <summary>Operation was a dry run; no changes were persisted.</summary>
    DryRun,

    /// <summary>Operation was denied by a security policy.</summary>
    Denied
}

// ── Audit log query (for the /api/audit endpoint) ────────────────────────────

/// <summary>Filter parameters for querying the audit log.</summary>
public sealed class AuditLogQueryRequest
{
    /// <summary>Return entries with timestamp &gt;= this value.</summary>
    public DateTimeOffset? Since { get; set; }

    /// <summary>Return entries with timestamp &lt;= this value.</summary>
    public DateTimeOffset? Until { get; set; }

    /// <summary>Filter by exact request ID.</summary>
    public string? RequestId { get; set; }

    /// <summary>Filter by session / correlation ID.</summary>
    public string? SessionId { get; set; }

    /// <summary>Filter by target file or directory path.</summary>
    public string? TargetPath { get; set; }

    /// <summary>Filter by operation type.</summary>
    public AuditOperationType? OperationType { get; set; }

    /// <summary>Filter by outcome.</summary>
    public AuditOutcome? Outcome { get; set; }

    /// <summary>Maximum number of entries per page.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Zero-based page index.</summary>
    public int PageIndex { get; set; } = 0;
}

/// <summary>Paginated result of an audit log query.</summary>
public sealed class AuditLogQueryResponse
{
    /// <summary>Matching audit log entries for the current page.</summary>
    public List<AuditEntry> Entries { get; set; } = new();

    /// <summary>Total number of matching entries across all pages.</summary>
    public int TotalCount { get; set; }

    /// <summary>True when more entries exist beyond this page.</summary>
    public bool HasMore { get; set; }
}
using System;
using System.Collections.Generic;

namespace StewardessMCPServive.Models
{
    // ────────────────────────────────────────────────────────────────────────────
    //  Standard API response envelope
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generic success/error envelope returned by every REST endpoint.
    /// MCP tool handlers convert the inner <typeparamref name="T"/> to MCP content.
    /// </summary>
    /// <typeparam name="T">The payload type on success.</typeparam>
    public sealed class ApiResponse<T>
    {
        /// <summary>True when the operation completed without errors.</summary>
        public bool Success { get; set; }

        /// <summary>Response payload; null on failure.</summary>
        public T Data { get; set; } = default!;

        /// <summary>Error detail; null on success.</summary>
        public ApiError? Error { get; set; }

        /// <summary>Unique identifier for this request, propagated from X-Request-Id or generated.</summary>
        public string? RequestId { get; set; }

        /// <summary>UTC timestamp of the response.</summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        // ── Factory helpers ──────────────────────────────────────────────────────

        /// <summary>Creates a successful <see cref="ApiResponse{T}"/> wrapping <paramref name="data"/>.</summary>
        public static ApiResponse<T> Ok(T data, string? requestId = null) =>
            new ApiResponse<T>
            {
                Success   = true,
                Data      = data,
                RequestId = requestId
            };

        /// <summary>Creates a failed <see cref="ApiResponse{T}"/> from an existing <see cref="ApiError"/>.</summary>
        public static ApiResponse<T> Fail(ApiError error, string? requestId = null) =>
            new ApiResponse<T>
            {
                Success   = false,
                Error     = error,
                RequestId = requestId
            };

        /// <summary>Creates a failed <see cref="ApiResponse{T}"/> with a code and message.</summary>
        public static ApiResponse<T> Fail(string code, string message, string? requestId = null) =>
            Fail(new ApiError { Code = code, Message = message }, requestId);
    }

    /// <summary>
    /// Non-generic convenience alias used when no payload is needed.
    /// </summary>
    public sealed class ApiResponse
    {
        /// <summary>True when the operation completed without errors.</summary>
        public bool Success { get; set; }
        /// <summary>Error detail; null on success.</summary>
        public ApiError? Error { get; set; }
        /// <summary>Unique identifier for this request.</summary>
        public string? RequestId { get; set; }
        /// <summary>UTC timestamp of the response.</summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Creates a successful <see cref="ApiResponse"/>.</summary>
        public static ApiResponse Ok(string? requestId = null) =>
            new ApiResponse { Success = true, RequestId = requestId };

        /// <summary>Creates a failed <see cref="ApiResponse"/> with the given error code and message.</summary>
        public static ApiResponse Fail(string code, string message, string? requestId = null) =>
            new ApiResponse
            {
                Success   = false,
                Error     = new ApiError { Code = code, Message = message },
                RequestId = requestId
            };
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Error detail
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Structured error information attached to a failed response.</summary>
    public sealed class ApiError
    {
        /// <summary>Machine-readable error code from <see cref="ErrorCodes"/>.</summary>
        public string? Code { get; set; }

        /// <summary>Human-readable error message.</summary>
        public string? Message { get; set; }

        /// <summary>Optional field-level validation errors.</summary>
        public List<FieldError>? Details { get; set; }

        /// <summary>Optional inner exception message for diagnostic purposes (dev/debug only).</summary>
        public string? InnerMessage { get; set; }
    }

    /// <summary>A single field-level validation error.</summary>
    public sealed class FieldError
    {
        /// <summary>The field or parameter name that failed validation.</summary>
        public string? Field { get; set; }

        /// <summary>Description of the validation failure.</summary>
        public string? Message { get; set; }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Error codes (machine-readable string constants)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Canonical error code strings used in <see cref="ApiError.Code"/>.</summary>
    public static class ErrorCodes
    {
        // Security
        /// <summary>Request lacks valid credentials.</summary>
        public const string Unauthorized       = "UNAUTHORIZED";
        /// <summary>Credentials are valid but access is denied.</summary>
        public const string Forbidden          = "FORBIDDEN";
        /// <summary>Operation rejected because the service is in read-only mode.</summary>
        public const string ReadOnlyMode       = "READ_ONLY_MODE";
        /// <summary>Path attempts to escape the repository root via traversal sequences.</summary>
        public const string PathTraversal      = "PATH_TRAVERSAL";
        /// <summary>Resolved path is outside the configured repository root.</summary>
        public const string OutsideRepository  = "OUTSIDE_REPOSITORY";
        /// <summary>Target path is inside a blocked folder.</summary>
        public const string BlockedFolder      = "BLOCKED_FOLDER";
        /// <summary>File extension is explicitly blocked.</summary>
        public const string BlockedExtension   = "BLOCKED_EXTENSION";
        /// <summary>File extension is not in the allowed-extensions list.</summary>
        public const string ExtensionNotAllowed= "EXTENSION_NOT_ALLOWED";
        /// <summary>Destructive operation requires a prior approval token.</summary>
        public const string ApprovalRequired   = "APPROVAL_REQUIRED";

        // Input validation
        /// <summary>Generic invalid request error.</summary>
        public const string InvalidRequest     = "INVALID_REQUEST";
        /// <summary>A required parameter was missing.</summary>
        public const string MissingParameter   = "MISSING_PARAMETER";
        /// <summary>The supplied path is syntactically invalid.</summary>
        public const string InvalidPath        = "INVALID_PATH";
        /// <summary>The supplied line range is invalid.</summary>
        public const string InvalidRange       = "INVALID_RANGE";
        /// <summary>The supplied regex or search pattern is invalid.</summary>
        public const string InvalidPattern     = "INVALID_PATTERN";

        // File system
        /// <summary>The requested file was not found.</summary>
        public const string FileNotFound       = "FILE_NOT_FOUND";
        /// <summary>The requested directory was not found.</summary>
        public const string DirectoryNotFound  = "DIRECTORY_NOT_FOUND";
        /// <summary>The requested path (file or directory) was not found.</summary>
        public const string PathNotFound       = "PATH_NOT_FOUND";
        /// <summary>The target path already exists.</summary>
        public const string AlreadyExists      = "ALREADY_EXISTS";
        /// <summary>The file exceeds the configured read-size limit.</summary>
        public const string FileTooLarge       = "FILE_TOO_LARGE";
        /// <summary>An I/O error occurred while accessing the file system.</summary>
        public const string IOError            = "IO_ERROR";

        // Limits
        /// <summary>Search results were capped at the configured maximum.</summary>
        public const string ResultsTruncated   = "RESULTS_TRUNCATED";
        /// <summary>Directory traversal exceeded the configured depth limit.</summary>
        public const string DepthExceeded      = "DEPTH_EXCEEDED";
        /// <summary>The operation exceeded the configured time limit.</summary>
        public const string TimeoutExceeded    = "TIMEOUT_EXCEEDED";

        // Operations
        /// <summary>A git command failed.</summary>
        public const string GitError           = "GIT_ERROR";
        /// <summary>The build command failed.</summary>
        public const string BuildError         = "BUILD_ERROR";
        /// <summary>The requested command is not in the allowed-commands whitelist.</summary>
        public const string CommandNotAllowed  = "COMMAND_NOT_ALLOWED";
        /// <summary>The command exited with a non-zero exit code.</summary>
        public const string CommandFailed      = "COMMAND_FAILED";
        /// <summary>A patch/diff could not be applied cleanly.</summary>
        public const string PatchFailed        = "PATCH_FAILED";
        /// <summary>Creating a pre-edit backup failed.</summary>
        public const string BackupFailed       = "BACKUP_FAILED";
        /// <summary>Rolling back to a backup failed.</summary>
        public const string RollbackFailed     = "ROLLBACK_FAILED";

        // MCP protocol
        /// <summary>The requested JSON-RPC method was not found.</summary>
        public const string McpMethodNotFound  = "MCP_METHOD_NOT_FOUND";
        /// <summary>The requested MCP tool was not found.</summary>
        public const string McpToolNotFound    = "MCP_TOOL_NOT_FOUND";
        /// <summary>Invalid parameters supplied to an MCP tool call.</summary>
        public const string McpInvalidParams   = "MCP_INVALID_PARAMS";

        // Internal
        /// <summary>An unexpected internal server error occurred.</summary>
        public const string InternalError      = "INTERNAL_ERROR";
        /// <summary>The operation is not yet implemented.</summary>
        public const string NotImplemented     = "NOT_IMPLEMENTED";
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Paginated response wrapper
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Paginated list result, embedded as the Data of an <see cref="ApiResponse{T}"/>.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    public sealed class PagedResult<T>
    {
        /// <summary>Items in the current page.</summary>
        public List<T> Items { get; set; } = new List<T>();

        /// <summary>Total number of matching items across all pages.</summary>
        public int TotalCount { get; set; }

        /// <summary>Zero-based page index.</summary>
        public int PageIndex { get; set; }

        /// <summary>Page size used for this query.</summary>
        public int PageSize { get; set; }

        /// <summary>True when more items exist beyond this page.</summary>
        public bool HasMore { get; set; }

        /// <summary>Optional informational message (e.g. "Results truncated to 200").</summary>
        public string? Note { get; set; }
    }
}

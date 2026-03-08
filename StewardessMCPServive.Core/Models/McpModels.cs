using System;
using System.Collections.Generic;

namespace StewardessMCPServive.Models
{
    // ────────────────────────────────────────────────────────────────────────────
    //  MCP (Model Context Protocol) — JSON-RPC 2.0 transport layer
    //
    //  The service implements the MCP specification (protocol version 2024-11-05).
    //    POST /mcp/v1/          — JSON-RPC 2.0 dispatch
    //    GET  /mcp/v1/manifest  — capability manifest (non-RPC)
    //
    //  Supported JSON-RPC methods:
    //    initialize           — capability negotiation (required lifecycle step)
    //    notifications/*      — silently consumed (no response per JSON-RPC spec)
    //    ping                 — liveness check
    //    tools/list           — enumerate all registered tools
    //    tools/call           — invoke a named tool
    // ────────────────────────────────────────────────────────────────────────────

    // ── JSON-RPC 2.0 envelope ────────────────────────────────────────────────────

    /// <summary>Inbound JSON-RPC 2.0 request.</summary>
    public sealed class McpRequest
    {
        /// <summary>Must be "2.0".</summary>
        public string JsonRpc { get; set; } = "2.0";

        /// <summary>
        /// Request identifier.  May be a number or a string per the JSON-RPC spec.
        /// Stored as object; serialized as-is in the response.
        /// </summary>
        public object Id { get; set; }

        /// <summary>Method name: "tools/list", "tools/call", "ping", etc.</summary>
        public string Method { get; set; }

        /// <summary>Method parameters; structure depends on Method.</summary>
        public object Params { get; set; }
    }

    /// <summary>Outbound JSON-RPC 2.0 response.</summary>
    public sealed class McpResponse
    {
        /// <summary>JSON-RPC version string; always "2.0".</summary>
        public string JsonRpc { get; set; } = "2.0";

        /// <summary>Echoed from the request Id.</summary>
        public object Id { get; set; }

        /// <summary>Populated on success; null on error.</summary>
        public object Result { get; set; }

        /// <summary>Populated on error; null on success.</summary>
        public McpError Error { get; set; }

        /// <summary>Creates a successful MCP response.</summary>
        public static McpResponse Ok(object id, object result) =>
            new McpResponse { Id = id, Result = result };

        /// <summary>Creates an error MCP response with the given JSON-RPC error code and message.</summary>
        public static McpResponse Err(object id, int code, string message, object data = null) =>
            new McpResponse { Id = id, Error = new McpError { Code = code, Message = message, Data = data } };
    }

    /// <summary>JSON-RPC 2.0 error object.</summary>
    public sealed class McpError
    {
        /// <summary>Standard JSON-RPC error codes + custom extensions.</summary>
        public int Code { get; set; }

        /// <summary>Human-readable error description.</summary>
        public string Message { get; set; }

        /// <summary>Optional additional data (e.g. validation details).</summary>
        public object Data { get; set; }
    }

    /// <summary>Standard JSON-RPC 2.0 error codes.</summary>
    public static class McpErrorCodes
    {
        /// <summary>JSON parse error (-32700).</summary>
        public const int ParseError     = -32700;
        /// <summary>Invalid JSON-RPC request (-32600).</summary>
        public const int InvalidRequest = -32600;
        /// <summary>Method not found (-32601).</summary>
        public const int MethodNotFound = -32601;
        /// <summary>Invalid method parameters (-32602).</summary>
        public const int InvalidParams  = -32602;
        /// <summary>Internal server error (-32603).</summary>
        public const int InternalError  = -32603;

        // Custom application codes (in the -32000 to -32099 range)
        /// <summary>Authentication required (-32001).</summary>
        public const int Unauthorized        = -32001;
        /// <summary>Access denied (-32002).</summary>
        public const int Forbidden           = -32002;
        /// <summary>Named tool not found (-32003).</summary>
        public const int ToolNotFound        = -32003;
        /// <summary>Path traversal attempt detected (-32004).</summary>
        public const int PathTraversal       = -32004;
        /// <summary>Service is in read-only mode (-32005).</summary>
        public const int ReadOnlyMode        = -32005;
        /// <summary>Command not in the allowed-commands whitelist (-32006).</summary>
        public const int CommandNotAllowed   = -32006;
        /// <summary>Operation timed out (-32007).</summary>
        public const int TimeoutExceeded     = -32007;
        /// <summary>File exceeds the read-size limit (-32008).</summary>
        public const int FileTooLarge        = -32008;
    }

    // ── tools/list result ────────────────────────────────────────────────────────

    /// <summary>Result of a <c>tools/list</c> request.</summary>
    public sealed class McpListToolsResult
    {
        /// <summary>Tool definitions available on this page.</summary>
        public List<McpToolDefinition> Tools { get; set; } = new List<McpToolDefinition>();

        /// <summary>
        /// Opaque cursor for the next page of tools; null when there are no more pages.
        /// Clients pass this back as <c>params.cursor</c> in the next tools/list call.
        /// </summary>
        public string NextCursor { get; set; }
    }

    // ── initialize ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sent by the client as the first JSON-RPC call.
    /// The server MUST respond before any other method is accepted.
    /// </summary>
    public sealed class McpInitializeParams
    {
        /// <summary>Protocol version the client supports, e.g. "2024-11-05".</summary>
        public string ProtocolVersion { get; set; }

        /// <summary>Capabilities the client declares (roots, sampling, etc.).</summary>
        public McpClientCapabilities Capabilities { get; set; }

        /// <summary>Identifying information about the connecting MCP client.</summary>
        public McpClientInfo ClientInfo { get; set; }
    }

    /// <summary>Capabilities declared by the MCP client during initialization.</summary>
    public sealed class McpClientCapabilities
    {
        /// <summary>True when the client supports roots list changes.</summary>
        public McpRootsCapability Roots { get; set; }

        /// <summary>True when the client supports sampling.</summary>
        public object Sampling { get; set; }
    }

    /// <summary>Indicates whether the client supports roots list-changed notifications.</summary>
    public sealed class McpRootsCapability
    {
        /// <summary>True when the client can handle <c>notifications/roots/list_changed</c>.</summary>
        public bool ListChanged { get; set; }
    }

    /// <summary>Identifying information about the connecting MCP client.</summary>
    public sealed class McpClientInfo
    {
        /// <summary>Client application name.</summary>
        public string Name { get; set; }
        /// <summary>Client application version string.</summary>
        public string Version { get; set; }
    }

    /// <summary>Server response to <c>initialize</c>.</summary>
    public sealed class McpInitializeResult
    {
        /// <summary>Protocol version this server supports.  Must match or be compatible with the client's request.</summary>
        public string ProtocolVersion { get; set; }

        /// <summary>Server capabilities declared during initialization.</summary>
        public McpInitializeServerCapabilities Capabilities { get; set; }

        /// <summary>Identifying information about this MCP server.</summary>
        public McpServerInfo ServerInfo { get; set; }

        /// <summary>Optional human-readable instructions for the LLM about how to use this server.</summary>
        public string Instructions { get; set; }
    }

    /// <summary>Server capabilities declared during MCP initialization.</summary>
    public sealed class McpInitializeServerCapabilities
    {
        /// <summary>Declared when the server supports tools.</summary>
        public McpToolsCapability Tools { get; set; }

        /// <summary>Declared when the server supports logging level control.</summary>
        public object Logging { get; set; }
    }

    /// <summary>Declares that this server exposes a <c>tools</c> capability.</summary>
    public sealed class McpToolsCapability
    {
        /// <summary>True when the server may send <c>notifications/tools/list_changed</c>.</summary>
        public bool ListChanged { get; set; } = false;
    }

    /// <summary>Identifying information about this MCP server.</summary>
    public sealed class McpServerInfo
    {
        /// <summary>Server application name.</summary>
        public string Name { get; set; }
        /// <summary>Server application version string.</summary>
        public string Version { get; set; }
    }

    // ── Tool definition (schema) ─────────────────────────────────────────────────

    /// <summary>
    /// Full MCP tool definition including the JSON Schema for input validation.
    /// Returned by tools/list and the manifest endpoint.
    /// </summary>
    public sealed class McpToolDefinition
    {
        /// <summary>Unique snake_case tool name, e.g. "read_file".</summary>
        public string Name { get; set; }

        /// <summary>Human-readable description shown in agent UI.</summary>
        public string Description { get; set; }

        /// <summary>JSON Schema object describing the tool's input parameters.</summary>
        public McpInputSchema InputSchema { get; set; }

        /// <summary>Tool category for grouping in the manifest.</summary>
        public string Category { get; set; }

        /// <summary>True when this tool performs a write/destructive operation.</summary>
        public bool IsDestructive { get; set; }

        /// <summary>True when the tool is disabled in the current configuration (e.g. read-only mode).</summary>
        public bool IsDisabled { get; set; }

        /// <summary>Reason the tool is disabled; null when enabled.</summary>
        public string DisabledReason { get; set; }

        /// <summary>Whether this tool supports dry_run execution without side effects.</summary>
        public bool SupportsDryRun { get; set; }

        /// <summary>Whether this tool supports rollback/undo of its effects.</summary>
        public bool SupportsRollback { get; set; }

        /// <summary>Whether this tool requires an approval token before execution.</summary>
        public bool RequiresApproval { get; set; }

        /// <summary>Whether this tool accepts a change_reason audit string.</summary>
        public bool SupportsAuditReason { get; set; }

        /// <summary>Risk level: "low", "medium", or "high".</summary>
        public string RiskLevel { get; set; } = "low";

        /// <summary>Side effect class: "read-only", "file-write", "process-execution", "git-mutation", "destructive", "service-state-write".</summary>
        public string SideEffectClass { get; set; } = "read-only";

        /// <summary>Additional semantic tags for agent routing (e.g. "code-intelligence", "semantic", "ci").</summary>
        public string[] Tags { get; set; } = Array.Empty<string>();

        /// <summary>Agent-oriented usage guidance for this tool.</summary>
        public McpUsageGuidance UsageGuidance { get; set; }

        /// <summary>JSON Schema describing the output structure of this tool.</summary>
        public object OutputSchema { get; set; }
    }

    /// <summary>Agent-oriented guidance for when and how to use an MCP tool.</summary>
    public sealed class McpUsageGuidance
    {
        /// <summary>Conditions under which this tool is the right choice.</summary>
        public string UseWhen { get; set; }
        /// <summary>Conditions under which this tool should NOT be used.</summary>
        public string DoNotUseWhen { get; set; }
        /// <summary>Tools that are commonly invoked after this one in an agent workflow.</summary>
        public string[] TypicalNextTools { get; set; } = Array.Empty<string>();
    }

    /// <summary>Service-level approval and safety policies.</summary>
    public sealed class McpPolicies
    {
        /// <summary>Whether tools marked IsDestructive require an approval token.</summary>
        public bool ApprovalRequiredForDestructive { get; set; }
        /// <summary>Whether command-execution tools require an approval token.</summary>
        public bool ApprovalRequiredForCommands { get; set; }
        /// <summary>Whether git-mutation tools require an approval token.</summary>
        public bool ApprovalRequiredForGitMutations { get; set; }
    }

    /// <summary>JSON Schema for a tool's input parameters.</summary>
    public sealed class McpInputSchema
    {
        /// <summary>JSON Schema type; always "object" for tool inputs.</summary>
        public string Type { get; set; } = "object";
        /// <summary>Map of parameter name to its JSON Schema definition.</summary>
        public Dictionary<string, McpPropertySchema> Properties { get; set; } = new Dictionary<string, McpPropertySchema>();
        /// <summary>Names of required parameters.</summary>
        public List<string> Required { get; set; } = new List<string>();
        /// <summary>When false, no extra properties beyond those declared are allowed.</summary>
        public bool AdditionalProperties { get; set; } = false;
    }

    /// <summary>JSON Schema for a single tool parameter.</summary>
    public sealed class McpPropertySchema
    {
        /// <summary>JSON Schema type (e.g. "string", "integer", "boolean", "array", "object").</summary>
        public string Type { get; set; }
        /// <summary>Human-readable description of what this parameter does.</summary>
        public string Description { get; set; }

        /// <summary>Default value serialized as a string.</summary>
        public object Default { get; set; }

        /// <summary>For enum-type properties.</summary>
        public List<string> Enum { get; set; }

        /// <summary>
        /// For array-type properties. May be a <see cref="McpPropertySchema"/> for simple element
        /// types or an anonymous object for complex object element schemas.
        /// </summary>
        public object Items { get; set; }

        /// <summary>Minimum value for numeric parameters.</summary>
        public int? Minimum { get; set; }
        /// <summary>Maximum value for numeric parameters.</summary>
        public int? Maximum { get; set; }
    }

    // ── tools/call parameters and result ─────────────────────────────────────────

    /// <summary>Parameters for a <c>tools/call</c> JSON-RPC request.</summary>
    public sealed class McpToolCallParams
    {
        /// <summary>Name of the tool to invoke.</summary>
        public string Name { get; set; }

        /// <summary>Tool arguments as a key-value dictionary.</summary>
        public Dictionary<string, object> Arguments { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>Result of a <c>tools/call</c> JSON-RPC request.</summary>
    public sealed class McpToolCallResult
    {
        /// <summary>Result content items.</summary>
        public List<McpContent> Content { get; set; } = new List<McpContent>();

        /// <summary>True when the tool returned an application-level error (not a JSON-RPC error).</summary>
        public bool IsError { get; set; }
    }

    /// <summary>A single content item in a tool call result.</summary>
    public sealed class McpContent
    {
        /// <summary>"text" | "image" | "resource".</summary>
        public string Type { get; set; } = "text";

        /// <summary>Text content; populated when Type is "text".</summary>
        public string Text { get; set; }

        /// <summary>Base64-encoded image data; populated when Type is "image".</summary>
        public string Data { get; set; }

        /// <summary>MIME type; used when Type is "image".</summary>
        public string MimeType { get; set; }

        /// <summary>Creates a text content item from a plain string.</summary>
        public static McpContent FromText(string text) =>
            new McpContent { Type = "text", Text = text };

        /// <summary>Creates a text content item representing an error message.</summary>
        public static McpContent FromError(string message) =>
            new McpContent { Type = "text", Text = "ERROR: " + message };
    }

    // ── Manifest (non-RPC) ───────────────────────────────────────────────────────

    /// <summary>
    /// Machine-readable capability manifest returned at GET /mcp/v1/manifest.
    /// An agent can fetch this once per session to discover all available tools
    /// without issuing a tools/list RPC call.
    /// </summary>
    public sealed class McpCapabilitiesManifest
    {
        /// <summary>Manifest schema version; currently "1.0".</summary>
        public string SchemaVersion { get; set; } = "1.0";
        /// <summary>Display name of this service.</summary>
        public string ServiceName { get; set; } = "StewardessMCPService";
        /// <summary>Version of this service.</summary>
        public string ServiceVersion { get; set; }
        /// <summary>UTC time when this manifest was generated.</summary>
        public DateTimeOffset GeneratedAt { get; set; }

        /// <summary>
        /// Describes the format of this manifest.
        /// This is an application-level service metadata document, not the native MCP wire protocol.
        /// It describes the MCP-exposed tools in a structured way for agent discovery.
        /// </summary>
        public string ManifestFormat { get; set; } = "stewardess-service-manifest/v1";

        /// <summary>Service-level approval and safety policies.</summary>
        public McpPolicies Policies { get; set; }

        /// <summary>High-level capability flags for this server.</summary>
        public McpServerCapabilities Capabilities { get; set; }
        /// <summary>Full list of available tool definitions.</summary>
        public List<McpToolDefinition> Tools { get; set; } = new List<McpToolDefinition>();
        /// <summary>Configured resource limits and allowed-command list.</summary>
        public McpServerConstraints Constraints { get; set; }
        /// <summary>Repository-specific context for the connected AI agent.</summary>
        public McpRepositoryContext RepositoryContext { get; set; }

        /// <summary>
        /// Common error schema used by all tools when an error occurs.
        /// All tools return { "error": { "Code": string, "Message": string, "Context": object? } }
        /// on failure. The Code field contains a machine-readable error code.
        /// </summary>
        public object CommonErrorSchema { get; set; }
    }

    /// <summary>High-level feature flags declared in the capability manifest.</summary>
    public sealed class McpServerCapabilities
    {
        /// <summary>True when file read operations are supported.</summary>
        public bool CanRead { get; set; } = true;
        /// <summary>True when file write/edit operations are supported.</summary>
        public bool CanWrite { get; set; }
        /// <summary>True when search operations are supported.</summary>
        public bool CanSearch { get; set; } = true;
        /// <summary>True when shell-command execution is supported.</summary>
        public bool CanExecuteCommands { get; set; }
        /// <summary>True when git operations are supported.</summary>
        public bool CanAccessGit { get; set; } = true;
        /// <summary>True when dry-run mode is supported for write operations.</summary>
        public bool SupportsDryRun { get; set; } = true;
        /// <summary>True when rollback of recent changes is supported.</summary>
        public bool SupportsRollback { get; set; } = true;
        /// <summary>True when an audit log is maintained.</summary>
        public bool SupportsAuditLog { get; set; } = true;
        /// <summary>True when multiple edits can be submitted as a single batch.</summary>
        public bool SupportsBatchEdits { get; set; } = true;
    }

    /// <summary>Configured resource limits and command whitelist.</summary>
    public sealed class McpServerConstraints
    {
        /// <summary>Maximum bytes that can be read from a single file.</summary>
        public long MaxFileReadBytes { get; set; }
        /// <summary>Maximum search results returned per query.</summary>
        public int MaxSearchResults { get; set; }
        /// <summary>Maximum recursive directory depth.</summary>
        public int MaxDirectoryDepth { get; set; }
        /// <summary>Maximum wall-clock seconds allowed for a single command execution.</summary>
        public int MaxCommandExecutionSeconds { get; set; }
        /// <summary>Shell commands allowed by the configuration whitelist.</summary>
        public IReadOnlyList<string> AllowedCommands { get; set; }
        /// <summary>Folder names that are excluded from all operations.</summary>
        public IReadOnlyCollection<string> BlockedFolders { get; set; }
        /// <summary>File extensions that are excluded from all operations.</summary>
        public IReadOnlyCollection<string> BlockedExtensions { get; set; }
    }

    /// <summary>Repository-specific context provided to the connected AI agent.</summary>
    public sealed class McpRepositoryContext
    {
        /// <summary>Friendly name of the repository (derived from the root folder name).</summary>
        public string RepositoryName { get; set; }
        /// <summary>Absolute path of the configured repository root.</summary>
        public string RepositoryRoot { get; set; }
        /// <summary>True when the root is a valid git repository.</summary>
        public bool IsGitRepository { get; set; }
        /// <summary>Currently checked-out branch name; null when not a git repository.</summary>
        public string CurrentBranch { get; set; }
    }

    // ── Ping ─────────────────────────────────────────────────────────────────────

    /// <summary>Response body for a ping / health-check RPC call.</summary>
    public sealed class McpPingResult
    {
        /// <summary>Always "ok" when the service is healthy.</summary>
        public string Status { get; set; } = "ok";
        /// <summary>UTC time at which the ping was processed.</summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        /// <summary>Running version of this service.</summary>
        public string ServiceVersion { get; set; }
    }
}

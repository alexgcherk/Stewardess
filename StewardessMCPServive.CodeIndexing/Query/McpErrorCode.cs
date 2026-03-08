namespace StewardessMCPServive.CodeIndexing.Query;

/// <summary>Machine-classifiable MCP error codes for AI agent consumption.</summary>
public static class McpErrorCode
{
    /// <summary>One or more input parameters failed validation.</summary>
    public const string ValidationError = "ValidationError";

    /// <summary>The requested snapshot was not found.</summary>
    public const string SnapshotNotFound = "SnapshotNotFound";

    /// <summary>The requested file was not found in the index.</summary>
    public const string FileNotFound = "FileNotFound";

    /// <summary>The requested symbol was not found in the index.</summary>
    public const string SymbolNotFound = "SymbolNotFound";

    /// <summary>The requested operation is not supported by this adapter or configuration.</summary>
    public const string CapabilityNotSupported = "CapabilityNotSupported";

    /// <summary>The repository has not been indexed yet.</summary>
    public const string RepositoryNotIndexed = "RepositoryNotIndexed";

    /// <summary>An unexpected internal error occurred.</summary>
    public const string InternalError = "InternalError";
}

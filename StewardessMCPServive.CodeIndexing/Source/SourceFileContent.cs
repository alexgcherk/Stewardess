namespace StewardessMCPServive.CodeIndexing.Source;

/// <summary>
/// Full content payload for a source file, including encoding and hash.
/// </summary>
public sealed class SourceFileContent
{
    /// <summary>Absolute path to the file.</summary>
    public required string FilePath { get; init; }

    /// <summary>Decoded text content of the file.</summary>
    public required string Content { get; init; }

    /// <summary>Detected encoding (e.g., "utf-8", "utf-16-le", "ascii").</summary>
    public string Encoding { get; init; } = "utf-8";

    /// <summary>SHA-256 hex hash of the raw bytes.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Raw byte content.</summary>
    public required byte[] RawBytes { get; init; }
}

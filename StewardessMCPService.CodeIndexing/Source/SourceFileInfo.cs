namespace StewardessMCPService.CodeIndexing.Source;

/// <summary>
/// Lightweight file metadata returned from file enumeration.
/// </summary>
public sealed class SourceFileInfo
{
    /// <summary>Absolute path to the file.</summary>
    public required string FilePath { get; init; }

    /// <summary>File size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Last modified UTC timestamp.</summary>
    public DateTimeOffset LastModified { get; init; }
}

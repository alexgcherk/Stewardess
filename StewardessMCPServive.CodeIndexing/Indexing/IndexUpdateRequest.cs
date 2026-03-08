using StewardessMCPServive.CodeIndexing.Model.Snapshots;

namespace StewardessMCPServive.CodeIndexing.Indexing;

/// <summary>
/// Parameters for an incremental index update operation.
/// </summary>
public sealed class IndexUpdateRequest
{
    /// <summary>Absolute path to the repository root.</summary>
    public required string RootPath { get; init; }

    /// <summary>
    /// Optional caller-supplied list of changed relative file paths.
    /// When provided, the engine skips full file enumeration and trusts this list.
    /// When null, the engine detects changes automatically by comparing content hashes.
    /// </summary>
    public IReadOnlyList<string>? ChangedFiles { get; init; }

    /// <summary>Optional progress callback invoked during the update.</summary>
    public Action<IndexProgress>? ProgressCallback { get; init; }
}

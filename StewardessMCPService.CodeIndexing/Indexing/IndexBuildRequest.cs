using StewardessMCPService.CodeIndexing.Model.Snapshots;
using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Indexing;

/// <summary>
/// Request to build a full index for a repository root.
/// </summary>
public sealed class IndexBuildRequest
{
    /// <summary>Absolute path to the repository root.</summary>
    public required string RootPath { get; init; }

    /// <summary>Glob patterns to include. <see langword="null"/> means include all eligible files.</summary>
    public IReadOnlyList<string>? IncludePatterns { get; init; }

    /// <summary>Glob patterns to exclude in addition to default eligibility exclusions.</summary>
    public IReadOnlyList<string>? ExcludePatterns { get; init; }

    /// <summary>Parse depth for this build.</summary>
    public ParseMode ParseMode { get; init; } = ParseMode.Declarations;

    /// <summary>Restrict to specific languages. <see langword="null"/> means all supported languages.</summary>
    public IReadOnlyList<string>? LanguageFilter { get; init; }

    /// <summary>When <see langword="true"/>, discards any cached index and rebuilds from scratch.</summary>
    public bool ForceRebuild { get; init; }

    /// <summary>Per-file size limit override. <see langword="null"/> uses the policy default.</summary>
    public long? MaxFileSizeBytes { get; init; }

    /// <summary>Whether to persist the snapshot to the snapshot store.</summary>
    public bool PersistSnapshot { get; init; } = true;

    /// <summary>Optional progress callback invoked during the build.</summary>
    public Action<IndexProgress>? ProgressCallback { get; init; }
}

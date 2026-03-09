using StewardessMCPService.CodeIndexing.Model.Snapshots;

namespace StewardessMCPService.CodeIndexing.Indexing;

/// <summary>
/// Current indexing status for a repository root.
/// </summary>
public sealed class IndexStatus
{
    /// <summary>Repository root path.</summary>
    public required string RootPath { get; init; }

    /// <summary>Current operational state.</summary>
    public IndexState State { get; init; }

    /// <summary>ID of the most recently published snapshot, if any.</summary>
    public string? LatestSnapshotId { get; init; }

    /// <summary>UTC time when the last successful build completed.</summary>
    public DateTimeOffset? LastCompletedAt { get; init; }

    /// <summary>Error message from the last failed build, if any.</summary>
    public string? LastError { get; init; }

    /// <summary>In-progress build progress, if a build is active.</summary>
    public IndexProgress? Progress { get; init; }

    /// <summary>Number of indexed files in the latest snapshot.</summary>
    public int FileCount { get; init; }

    /// <summary>Number of logical symbols in the latest snapshot.</summary>
    public int SymbolCount { get; init; }

    /// <summary>Number of resolved reference edges in the latest snapshot.</summary>
    public int ReferenceCount { get; init; }

    /// <summary>Delta information from the most recent incremental update, if applicable.</summary>
    public SnapshotDelta? LastDelta { get; init; }
}

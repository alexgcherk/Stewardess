// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.CodeIndexing.Model.Snapshots;

/// <summary>
///     Records the set of file-level changes captured in an incremental snapshot update.
/// </summary>
public sealed class SnapshotDelta
{
    /// <summary>Repository-relative paths of newly added files.</summary>
    public IReadOnlyList<string> AddedFilePaths { get; init; } = [];

    /// <summary>Repository-relative paths of modified files.</summary>
    public IReadOnlyList<string> ModifiedFilePaths { get; init; } = [];

    /// <summary>Repository-relative paths of deleted files.</summary>
    public IReadOnlyList<string> DeletedFilePaths { get; init; } = [];

    /// <summary>Number of files unchanged from the previous snapshot.</summary>
    public int UnchangedFileCount { get; init; }

    /// <summary>Change in symbol count relative to the previous snapshot.</summary>
    public int SymbolCountDelta { get; init; }

    /// <summary>Change in reference count relative to the previous snapshot.</summary>
    public int ReferenceCountDelta { get; init; }

    /// <summary>Total milliseconds spent on the incremental update.</summary>
    public long DurationMs { get; init; }

    /// <summary>ID of the snapshot that this delta was computed from.</summary>
    public string? PreviousSnapshotId { get; init; }
}
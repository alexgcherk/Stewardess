// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.CodeIndexing.Indexing;

/// <summary>
///     Result of a completed incremental index update operation.
/// </summary>
public sealed class IndexUpdateResult
{
    /// <summary>ID of the newly published snapshot.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>ID of the snapshot that was replaced, or null if this was a first-time build.</summary>
    public string? PreviousSnapshotId { get; init; }

    /// <summary>Absolute repository root path that was updated.</summary>
    public required string RootPath { get; init; }

    /// <summary>UTC timestamp when the update started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>UTC timestamp when the update completed.</summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>Total elapsed milliseconds for the update.</summary>
    public long DurationMs { get; init; }

    /// <summary>Number of files newly added to the repository.</summary>
    public int FilesAdded { get; init; }

    /// <summary>Number of files whose content changed since the last snapshot.</summary>
    public int FilesModified { get; init; }

    /// <summary>Number of files removed from the repository.</summary>
    public int FilesDeleted { get; init; }

    /// <summary>Number of files that were unchanged and reused from the previous snapshot.</summary>
    public int FilesUnchanged { get; init; }

    /// <summary>Error message if the update failed, otherwise null.</summary>
    public string? Error { get; init; }
}
// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.CodeIndexing.Model.Snapshots;

namespace StewardessMCPService.CodeIndexing.Snapshots;

/// <summary>
/// Stores and retrieves published index snapshots.
/// Implementations MUST be thread-safe.
/// </summary>
public interface ISnapshotStore
{
    /// <summary>
    /// Persists a new snapshot. Replaces any previous snapshot for the same root path.
    /// </summary>
    Task SaveSnapshotAsync(IndexSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Returns the latest published snapshot for the given root path,
    /// or <see langword="null"/> if no snapshot exists.
    /// </summary>
    Task<IndexSnapshot?> GetLatestSnapshotAsync(string rootPath, CancellationToken ct = default);

    /// <summary>
    /// Returns a snapshot by its unique ID, or <see langword="null"/> if not found.
    /// </summary>
    Task<IndexSnapshot?> GetSnapshotByIdAsync(string snapshotId, CancellationToken ct = default);

    /// <summary>
    /// Lists all snapshot metadata for the given root path, newest first.
    /// </summary>
    Task<IReadOnlyList<SnapshotMetadata>> ListSnapshotsAsync(string rootPath, CancellationToken ct = default);

    /// <summary>
    /// Removes a snapshot by ID.
    /// </summary>
    Task DeleteSnapshotAsync(string snapshotId, CancellationToken ct = default);

    /// <summary>
    /// Returns all known root paths that have at least one snapshot.
    /// </summary>
    Task<IReadOnlyList<string>> ListRootPathsAsync(CancellationToken ct = default);

    /// <summary>
    /// Removes all stored snapshots for the given root path, returning the number of snapshots removed.
    /// </summary>
    Task<int> ClearRepositoryAsync(string rootPath, CancellationToken ct = default);
}

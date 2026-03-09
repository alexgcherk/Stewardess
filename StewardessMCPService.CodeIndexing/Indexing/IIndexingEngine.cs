// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
namespace StewardessMCPService.CodeIndexing.Indexing;

/// <summary>
/// Orchestrates the full indexing pipeline for a repository root.
/// </summary>
public interface IIndexingEngine
{
    /// <summary>
    /// Builds or rebuilds a full index snapshot for the given root path.
    /// </summary>
    /// <param name="request">Build configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the completed build.</returns>
    Task<IndexBuildResult> BuildAsync(IndexBuildRequest request, CancellationToken ct = default);

    /// <summary>
    /// Performs an incremental update for the given root path.
    /// Only re-parses files that have been added, modified, or deleted since the last snapshot.
    /// Reference resolution is re-run for all files to ensure cross-file consistency.
    /// If no previous snapshot exists, falls back to a full build.
    /// </summary>
    Task<IndexUpdateResult> UpdateAsync(IndexUpdateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns the current indexing status for the given root path.
    /// </summary>
    Task<IndexStatus> GetStatusAsync(string rootPath, CancellationToken ct = default);

    /// <summary>
    /// Removes all stored index state for the given root path.
    /// Returns the number of snapshots removed.
    /// </summary>
    Task<int> ClearRepositoryAsync(string rootPath, CancellationToken ct = default);
}

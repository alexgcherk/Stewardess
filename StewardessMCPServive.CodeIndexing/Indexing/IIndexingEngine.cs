namespace StewardessMCPServive.CodeIndexing.Indexing;

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
    /// Returns the current indexing status for the given root path.
    /// </summary>
    Task<IndexStatus> GetStatusAsync(string rootPath, CancellationToken ct = default);
}

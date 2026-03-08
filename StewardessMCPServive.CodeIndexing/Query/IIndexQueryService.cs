using StewardessMCPServive.CodeIndexing.Model.Snapshots;

namespace StewardessMCPServive.CodeIndexing.Query;

/// <summary>
/// Provides read-only query access to published index snapshots.
/// All methods execute against a consistent, immutable snapshot.
/// </summary>
public interface IIndexQueryService
{
    /// <summary>
    /// Returns the latest snapshot metadata for the given root path,
    /// or <see langword="null"/> if no snapshot exists.
    /// </summary>
    Task<SnapshotMetadata?> GetSnapshotInfoAsync(string? snapshotId, string? rootPath, CancellationToken ct = default);

    /// <summary>
    /// Returns the full snapshot for the given ID or root path,
    /// or <see langword="null"/> if not found.
    /// </summary>
    Task<IndexSnapshot?> GetSnapshotAsync(string? snapshotId, string? rootPath, CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated list of files in the snapshot.
    /// </summary>
    Task<ListFilesResponse> ListFilesAsync(ListFilesRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns the structural outline of a single file.
    /// </summary>
    Task<FileOutlineResponse?> GetFileOutlineAsync(GetFileOutlineRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns all known root paths that have a published snapshot.
    /// </summary>
    Task<IReadOnlyList<string>> ListRootPathsAsync(CancellationToken ct = default);

    // ── Phase 2 — Symbol queries ──────────────────────────────────────────────

    /// <summary>
    /// Searches symbols by name with optional match mode, language, kind, and container filters.
    /// </summary>
    Task<FindSymbolsResponse> FindSymbolsAsync(FindSymbolsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns full details for a single logical symbol.
    /// </summary>
    Task<GetSymbolResponse> GetSymbolAsync(GetSymbolRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns all known occurrences of a logical symbol.
    /// </summary>
    Task<GetSymbolOccurrencesResponse> GetSymbolOccurrencesAsync(
        GetSymbolOccurrencesRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns direct child symbols of the specified symbol.
    /// </summary>
    Task<GetSymbolChildrenResponse> GetSymbolChildrenAsync(
        GetSymbolChildrenRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns categorized members (constructors, methods, properties, fields, events, nested types)
    /// of a type-like symbol.
    /// </summary>
    Task<GetTypeMembersResponse> GetTypeMembersAsync(
        GetTypeMembersRequest request, CancellationToken ct = default);

    /// <summary>
    /// Resolves a symbol ID or occurrence ID to a concrete source location.
    /// Exactly one of <see cref="ResolveLocationRequest.SymbolId"/> or
    /// <see cref="ResolveLocationRequest.OccurrenceId"/> must be provided.
    /// </summary>
    Task<ResolveLocationResponse> ResolveLocationAsync(
        ResolveLocationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns a hierarchical namespace/module/package container tree.
    /// </summary>
    Task<GetNamespaceTreeResponse> GetNamespaceTreeAsync(
        GetNamespaceTreeRequest request, CancellationToken ct = default);

    // ── Phase 3 — Reference and import queries ────────────────────────────────

    /// <summary>
    /// Returns all import/using directives for a single file.
    /// </summary>
    Task<GetImportsResponse> GetImportsAsync(GetImportsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns outgoing and/or incoming reference edges for a single symbol.
    /// </summary>
    Task<GetReferencesResponse> GetReferencesAsync(GetReferencesRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns all reference edges originating in a single file.
    /// </summary>
    Task<GetFileReferencesResponse> GetFileReferencesAsync(GetFileReferencesRequest request, CancellationToken ct = default);
}

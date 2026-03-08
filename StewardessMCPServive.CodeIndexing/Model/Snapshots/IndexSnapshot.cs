using StewardessMCPServive.CodeIndexing.Model.Diagnostics;
using StewardessMCPServive.CodeIndexing.Model.References;
using StewardessMCPServive.CodeIndexing.Model.Semantic;
using StewardessMCPServive.CodeIndexing.Model.Structural;
using StewardessMCPServive.CodeIndexing.Parsers.Abstractions;

namespace StewardessMCPServive.CodeIndexing.Model.Snapshots;

/// <summary>
/// An immutable, consistent snapshot of the index for a repository root.
/// All read queries MUST execute against a single published snapshot.
/// </summary>
/// <remarks>
/// Snapshots are published atomically. In-progress builds MUST NOT affect
/// queries against the previous published snapshot.
/// </remarks>
public sealed class IndexSnapshot
{
    /// <summary>Snapshot metadata including counts and versioning.</summary>
    public required SnapshotMetadata Metadata { get; init; }

    /// <summary>All indexed files keyed by FileId.</summary>
    public IReadOnlyDictionary<string, FileRecord> Files { get; init; } =
        new Dictionary<string, FileRecord>();

    /// <summary>All structural nodes keyed by NodeId.</summary>
    public IReadOnlyDictionary<string, StructuralNode> Nodes { get; init; } =
        new Dictionary<string, StructuralNode>();

    /// <summary>All logical symbols keyed by SymbolId (Phase 2+).</summary>
    public IReadOnlyDictionary<string, LogicalSymbol> Symbols { get; init; } =
        new Dictionary<string, LogicalSymbol>();

    /// <summary>All symbol occurrences keyed by OccurrenceId (Phase 2+).</summary>
    public IReadOnlyDictionary<string, SymbolOccurrence> Occurrences { get; init; } =
        new Dictionary<string, SymbolOccurrence>();

    /// <summary>All reference edges keyed by EdgeId (Phase 3+).</summary>
    public IReadOnlyDictionary<string, ReferenceEdge> References { get; init; } =
        new Dictionary<string, ReferenceEdge>();

    // --- Reference reverse indexes (Phase 3+) ---

    /// <summary>
    /// Maps FileId to a list of import entries in that file (Phase 3+).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ImportEntry>> ImportsByFileId { get; init; } =
        new Dictionary<string, IReadOnlyList<ImportEntry>>();

    /// <summary>
    /// Unresolved reference hints per file, keyed by file ID.
    /// Retained to enable efficient incremental re-resolution in subsequent updates.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ReferenceHint>> HintsByFileId { get; init; } =
        new Dictionary<string, IReadOnlyList<ReferenceHint>>();

    /// <summary>
    /// Delta information describing what changed relative to the previous snapshot.
    /// Null for full builds.
    /// </summary>
    public SnapshotDelta? Delta { get; init; }

    /// <summary>
    /// Maps SourceSymbolId to a list of EdgeIds where that symbol is the reference source (Phase 3+).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ReferencesBySourceSymbolId { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps FileId to a list of EdgeIds for reference edges originating in that file (Phase 3+).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ReferencesByFileId { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>All diagnostics keyed by DiagnosticId.</summary>
    public IReadOnlyDictionary<string, IndexDiagnostic> Diagnostics { get; init; } =
        new Dictionary<string, IndexDiagnostic>();

    // --- Reverse indexes for efficient query ---

    /// <summary>Maps relative file path (forward-slashes, lower-case) to FileId.</summary>
    public IReadOnlyDictionary<string, string> PathToFileId { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps FileId to NodeIds of top-level nodes in that file.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> FileIdToTopNodeIds { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    // --- Symbol reverse indexes (Phase 2+) ---

    /// <summary>
    /// Maps lower-case symbol simple name to a list of SymbolIds with that name.
    /// Supports case-insensitive symbol name search.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SymbolsByName { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps lower-case fully qualified name to a single SymbolId.
    /// Used for exact qualified-name lookups.
    /// </summary>
    public IReadOnlyDictionary<string, string> SymbolsByQualifiedName { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps FileId to a list of SymbolIds declared in that file.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SymbolsByFileId { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// Maps SymbolId to a list of OccurrenceIds for that symbol.
    /// Supports the get_symbol_occurrences query.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> OccurrencesBySymbolId { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// Maps parent SymbolId to a list of direct child SymbolIds.
    /// Supports the get_symbol_children and namespace-tree queries.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ChildSymbolsByParentId { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
}

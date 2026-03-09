// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.CodeIndexing.Model.References;
using StewardessMCPService.CodeIndexing.Model.Semantic;
using StewardessMCPService.CodeIndexing.Model.Snapshots;
using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Query;

// ── Request types ─────────────────────────────────────────────────────────────

/// <summary>Request parameters for <see cref="IIndexQueryService.ListFilesAsync"/>.</summary>
public sealed class ListFilesRequest
{
    /// <summary>Optional snapshot ID. If null, the latest snapshot is used.</summary>
    public string? SnapshotId { get; init; }
    /// <summary>Optional repository root path used to identify the snapshot.</summary>
    public string? RootPath { get; init; }
    /// <summary>Restricts results to files with one of these language IDs.</summary>
    public IReadOnlyList<string>? LanguageFilter { get; init; }
    /// <summary>Restricts results to files with one of these eligibility statuses.</summary>
    public IReadOnlyList<EligibilityStatus>? EligibilityFilter { get; init; }
    /// <summary>Restricts results to files with one of these parse statuses.</summary>
    public IReadOnlyList<ParseStatus>? ParseStatusFilter { get; init; }
    /// <summary>Restricts results to files whose path starts with this prefix.</summary>
    public string? PathPrefix { get; init; }
    /// <summary>Whether to include a diagnostic count summary per file.</summary>
    public bool IncludeDiagnosticsSummary { get; init; }
    /// <summary>One-based page number.</summary>
    public int Page { get; init; } = 1;
    /// <summary>Page size (default 50).</summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>Request parameters for <see cref="IIndexQueryService.GetFileOutlineAsync"/>.</summary>
public sealed class GetFileOutlineRequest
{
    /// <summary>Relative file path whose outline should be returned.</summary>
    public required string FilePath { get; init; }
    /// <summary>Optional snapshot ID. If null, the latest snapshot is used.</summary>
    public string? SnapshotId { get; init; }
    /// <summary>Maximum tree depth to traverse. Null means unlimited.</summary>
    public int? MaxDepth { get; init; }
    /// <summary>Whether to include non-semantic (structural) nodes in the outline.</summary>
    public bool IncludeNonSemanticNodes { get; init; } = true;
    /// <summary>Whether to include source span information on each node.</summary>
    public bool IncludeSourceSpans { get; init; } = true;
    /// <summary>Whether to include extraction confidence scores on each node.</summary>
    public bool IncludeConfidence { get; init; }
}

// ── Response types ────────────────────────────────────────────────────────────

/// <summary>A single item in a list-files response.</summary>
public sealed class FileListItem
{
    /// <summary>Unique file identifier within the snapshot.</summary>
    public required string FileId { get; init; }
    /// <summary>Relative file path.</summary>
    public required string Path { get; init; }
    /// <summary>Language identifier for this file.</summary>
    public required string LanguageId { get; init; }
    /// <summary>Hash of the file content at index time.</summary>
    public required string ContentHash { get; init; }
    /// <summary>Eligibility status assigned to this file.</summary>
    public EligibilityStatus EligibilityStatus { get; init; }
    /// <summary>Parse status for this file.</summary>
    public ParseStatus ParseStatus { get; init; }
    /// <summary>Number of top-level structural nodes in this file.</summary>
    public int TopLevelNodeCount { get; init; }
    /// <summary>Number of diagnostics recorded for this file.</summary>
    public int DiagnosticCount { get; init; }
}

/// <summary>Paginated list of files from a snapshot.</summary>
public sealed class ListFilesResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }
    /// <summary>File entries for this page.</summary>
    public IReadOnlyList<FileListItem> Items { get; init; } = [];
    /// <summary>Current page (one-based).</summary>
    public int Page { get; init; }
    /// <summary>Page size used.</summary>
    public int PageSize { get; init; }
    /// <summary>Total matching files across all pages.</summary>
    public int TotalItems { get; init; }
    /// <summary>Whether additional pages are available.</summary>
    public bool HasMore { get; init; }
}

/// <summary>A structural node suitable for outline display.</summary>
public sealed class OutlineNode
{
    /// <summary>Unique node identifier within the snapshot.</summary>
    public required string NodeId { get; init; }
    /// <summary>Structural kind of this node.</summary>
    public NodeKind Kind { get; init; }
    /// <summary>Language-specific sub-kind string, if available.</summary>
    public string? Subkind { get; init; }
    /// <summary>Simple unqualified name of this node.</summary>
    public required string Name { get; init; }
    /// <summary>Human-readable display name (e.g., including parameter list).</summary>
    public string DisplayName { get; init; } = string.Empty;
    /// <summary>Source span of this node, included when requested.</summary>
    public SourceSpan? SourceSpan { get; init; }
    /// <summary>Extraction confidence score in [0.0, 1.0], included when requested.</summary>
    public double? Confidence { get; init; }
    /// <summary>Direct child nodes.</summary>
    public IReadOnlyList<OutlineNode> Children { get; init; } = [];
}

/// <summary>File structural outline response.</summary>
public sealed class FileOutlineResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }
    /// <summary>Unique file identifier within the snapshot.</summary>
    public required string FileId { get; init; }
    /// <summary>Relative file path.</summary>
    public required string Path { get; init; }
    /// <summary>Language identifier for this file.</summary>
    public required string LanguageId { get; init; }
    /// <summary>Parse status for this file.</summary>
    public ParseStatus ParseStatus { get; init; }
    /// <summary>Top-level structural nodes of the file outline.</summary>
    public IReadOnlyList<OutlineNode> RootNodes { get; init; } = [];
}

// ── Phase 2 — Symbol query request/response types ────────────────────────────

/// <summary>Request parameters for <see cref="IIndexQueryService.FindSymbolsAsync"/>.</summary>
public sealed class FindSymbolsRequest
{
    /// <summary>Search text to match against symbol names or qualified names.</summary>
    public required string QueryText { get; init; }

    /// <summary>Optional snapshot ID. If null, the latest snapshot is used.</summary>
    public string? SnapshotId { get; init; }

    /// <summary>
    /// Match mode: "exact" (name equality), "prefix" (starts-with), or "contains".
    /// Defaults to "prefix".
    /// </summary>
    public string MatchMode { get; init; } = "prefix";

    /// <summary>Restricts results to these language IDs.</summary>
    public IReadOnlyList<string>? LanguageFilter { get; init; }

    /// <summary>Restricts results to these symbol kinds.</summary>
    public IReadOnlyList<SymbolKind>? KindFilter { get; init; }

    /// <summary>Restricts results to symbols whose container path starts with one of these prefixes.</summary>
    public IReadOnlyList<string>? ContainerFilter { get; init; }

    /// <summary>Whether to include occurrence count per symbol.</summary>
    public bool IncludeOccurrenceCount { get; init; } = true;

    /// <summary>Whether to include a members summary for type symbols.</summary>
    public bool IncludeMembersSummary { get; init; }

    /// <summary>One-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size (default 50).</summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>Request parameters for <see cref="IIndexQueryService.GetSymbolAsync"/>.</summary>
public sealed class GetSymbolRequest
{
    /// <summary>Symbol ID to look up.</summary>
    public required string SymbolId { get; init; }

    /// <summary>Optional snapshot ID.</summary>
    public string? SnapshotId { get; init; }

    /// <summary>Whether to include primary occurrence location in the response.</summary>
    public bool IncludePrimaryOccurrence { get; init; } = true;

    /// <summary>Whether to include a members summary for type symbols.</summary>
    public bool IncludeMembersSummary { get; init; } = true;
}

/// <summary>Request parameters for <see cref="IIndexQueryService.GetSymbolOccurrencesAsync"/>.</summary>
public sealed class GetSymbolOccurrencesRequest
{
    /// <summary>Symbol ID whose occurrences should be returned.</summary>
    public required string SymbolId { get; init; }

    /// <summary>Optional snapshot ID.</summary>
    public string? SnapshotId { get; init; }

    /// <summary>Optional filter to restrict results to specific occurrence roles.</summary>
    public IReadOnlyList<OccurrenceRole>? RoleFilter { get; init; }
}

/// <summary>Request parameters for <see cref="IIndexQueryService.GetSymbolChildrenAsync"/>.</summary>
public sealed class GetSymbolChildrenRequest
{
    /// <summary>Parent symbol ID.</summary>
    public required string SymbolId { get; init; }

    /// <summary>Optional snapshot ID.</summary>
    public string? SnapshotId { get; init; }

    /// <summary>Optional filter to restrict children to specific symbol kinds.</summary>
    public IReadOnlyList<SymbolKind>? KindFilter { get; init; }

    /// <summary>Whether to include nested type symbols (classes, interfaces, etc.).</summary>
    public bool IncludeNestedTypes { get; init; } = true;
}

/// <summary>Request parameters for <see cref="IIndexQueryService.GetTypeMembersAsync"/>.</summary>
public sealed class GetTypeMembersRequest
{
    /// <summary>Symbol ID of the type to inspect.</summary>
    public required string TypeSymbolId { get; init; }

    /// <summary>Optional snapshot ID.</summary>
    public string? SnapshotId { get; init; }

    /// <summary>Whether to include accessor symbols (getters/setters).</summary>
    public bool IncludeAccessors { get; init; } = true;

    /// <summary>Whether to include nested type symbols.</summary>
    public bool IncludeNestedTypes { get; init; } = true;
}

/// <summary>Request parameters for <see cref="IIndexQueryService.ResolveLocationAsync"/>.</summary>
public sealed class ResolveLocationRequest
{
    /// <summary>Symbol ID to resolve (exclusive with <see cref="OccurrenceId"/>).</summary>
    public string? SymbolId { get; init; }

    /// <summary>Occurrence ID to resolve (exclusive with <see cref="SymbolId"/>).</summary>
    public string? OccurrenceId { get; init; }

    /// <summary>Optional snapshot ID.</summary>
    public string? SnapshotId { get; init; }
}

/// <summary>Request parameters for <see cref="IIndexQueryService.GetNamespaceTreeAsync"/>.</summary>
public sealed class GetNamespaceTreeRequest
{
    /// <summary>Optional snapshot ID.</summary>
    public string? SnapshotId { get; init; }

    /// <summary>Restricts results to these language IDs.</summary>
    public IReadOnlyList<string>? LanguageFilter { get; init; }

    /// <summary>
    /// If set, returns only descendants of this container qualified name.
    /// </summary>
    public string? RootContainer { get; init; }

    /// <summary>Whether to include symbol and file counts per container node.</summary>
    public bool IncludeCounts { get; init; } = true;

    /// <summary>Maximum tree depth. Null means unlimited.</summary>
    public int? MaxDepth { get; init; }
}

// ── Phase 2 response types ────────────────────────────────────────────────────

/// <summary>Resolved source location for a symbol or occurrence.</summary>
public sealed class SymbolLocation
{
    /// <summary>Relative file path.</summary>
    public required string FilePath { get; init; }

    /// <summary>Source span within the file, if available.</summary>
    public SourceSpan? SourceSpan { get; init; }

    /// <summary>Role of this occurrence.</summary>
    public OccurrenceRole Role { get; init; }

    /// <summary>Whether this is the primary occurrence of the symbol.</summary>
    public bool IsPrimary { get; init; }
}

/// <summary>Summary of members grouped by kind for a type symbol.</summary>
public sealed class MembersSummary
{
    /// <summary>Number of constructor symbols.</summary>
    public int ConstructorCount { get; init; }

    /// <summary>Number of method symbols.</summary>
    public int MethodCount { get; init; }

    /// <summary>Number of property symbols.</summary>
    public int PropertyCount { get; init; }

    /// <summary>Number of field symbols.</summary>
    public int FieldCount { get; init; }

    /// <summary>Number of event symbols.</summary>
    public int EventCount { get; init; }

    /// <summary>Number of nested type symbols.</summary>
    public int NestedTypeCount { get; init; }
}

/// <summary>Summary view of a logical symbol suitable for list and search results.</summary>
public sealed class SymbolSummary
{
    /// <summary>Unique symbol identifier.</summary>
    public required string SymbolId { get; init; }

    /// <summary>Human-readable composite key.</summary>
    public required string SymbolKey { get; init; }

    /// <summary>Simple unqualified name.</summary>
    public required string Name { get; init; }

    /// <summary>Fully qualified name.</summary>
    public required string QualifiedName { get; init; }

    /// <summary>Semantic kind.</summary>
    public SymbolKind Kind { get; init; }

    /// <summary>Language-specific sub-kind string.</summary>
    public string? Subkind { get; init; }

    /// <summary>Language identifier.</summary>
    public required string LanguageId { get; init; }

    /// <summary>Container path from root to immediate parent.</summary>
    public IReadOnlyList<string> ContainerPath { get; init; } = [];

    /// <summary>Primary source location, if resolved.</summary>
    public SymbolLocation? PrimaryLocation { get; init; }

    /// <summary>Extraction confidence.</summary>
    public double Confidence { get; init; }

    /// <summary>Number of occurrences for this symbol.</summary>
    public int OccurrenceCount { get; init; }

    /// <summary>Member counts, populated when requested.</summary>
    public MembersSummary? MembersSummary { get; init; }
}

/// <summary>Paginated symbol search results.</summary>
public sealed class FindSymbolsResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>Matched symbols for this page.</summary>
    public IReadOnlyList<SymbolSummary> Items { get; init; } = [];

    /// <summary>Current page (one-based).</summary>
    public int Page { get; init; }

    /// <summary>Page size used.</summary>
    public int PageSize { get; init; }

    /// <summary>Total matching symbols across all pages.</summary>
    public int TotalItems { get; init; }

    /// <summary>Whether additional pages are available.</summary>
    public bool HasMore { get; init; }
}

/// <summary>Response for a single symbol lookup.</summary>
public sealed class GetSymbolResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>The requested logical symbol, or null if not found.</summary>
    public LogicalSymbol? Symbol { get; init; }

    /// <summary>Primary occurrence location, included when requested.</summary>
    public SymbolLocation? PrimaryOccurrence { get; init; }

    /// <summary>Member summary for type symbols, included when requested.</summary>
    public MembersSummary? MembersSummary { get; init; }
}

/// <summary>Detail for one concrete occurrence of a symbol.</summary>
public sealed class OccurrenceDetail
{
    /// <summary>Unique occurrence identifier.</summary>
    public required string OccurrenceId { get; init; }

    /// <summary>Relative file path where this occurrence appears.</summary>
    public required string FilePath { get; init; }

    /// <summary>Role this occurrence plays.</summary>
    public OccurrenceRole Role { get; init; }

    /// <summary>Source location.</summary>
    public required SourceSpan SourceSpan { get; init; }

    /// <summary>Whether this is the primary (canonical) occurrence.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Extraction confidence.</summary>
    public double Confidence { get; init; }

    /// <summary>How this occurrence was extracted.</summary>
    public ExtractionMode ExtractionMode { get; init; }
}

/// <summary>Response containing all occurrences of a symbol.</summary>
public sealed class GetSymbolOccurrencesResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>The symbol ID that was queried.</summary>
    public required string SymbolId { get; init; }

    /// <summary>Occurrences of the symbol for this page.</summary>
    public IReadOnlyList<OccurrenceDetail> Occurrences { get; init; } = [];

    /// <summary>Current page (one-based).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size used.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Total occurrences across all pages.</summary>
    public int TotalItems { get; init; }

    /// <summary>Whether additional pages are available.</summary>
    public bool HasMore { get; init; }
}

/// <summary>Response containing direct child symbols of a symbol.</summary>
public sealed class GetSymbolChildrenResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>The parent symbol ID that was queried.</summary>
    public required string SymbolId { get; init; }

    /// <summary>Direct child symbols for this page.</summary>
    public IReadOnlyList<SymbolSummary> Children { get; init; } = [];

    /// <summary>Current page (one-based).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size used.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Total children across all pages.</summary>
    public int TotalItems { get; init; }

    /// <summary>Whether additional pages are available.</summary>
    public bool HasMore { get; init; }
}

/// <summary>Response containing categorized members of a type symbol.</summary>
public sealed class GetTypeMembersResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>The type symbol ID that was queried.</summary>
    public required string TypeSymbolId { get; init; }

    /// <summary>Constructor symbols.</summary>
    public IReadOnlyList<SymbolSummary> Constructors { get; init; } = [];

    /// <summary>Method symbols.</summary>
    public IReadOnlyList<SymbolSummary> Methods { get; init; } = [];

    /// <summary>Property symbols.</summary>
    public IReadOnlyList<SymbolSummary> Properties { get; init; } = [];

    /// <summary>Field symbols.</summary>
    public IReadOnlyList<SymbolSummary> Fields { get; init; } = [];

    /// <summary>Event symbols.</summary>
    public IReadOnlyList<SymbolSummary> Events { get; init; } = [];

    /// <summary>Nested type symbols.</summary>
    public IReadOnlyList<SymbolSummary> NestedTypes { get; init; } = [];

    /// <summary>Error message if the type symbol was not found or is not a type.</summary>
    public string? Error { get; init; }
}

/// <summary>Response for resolving a symbol or occurrence to a source location.</summary>
public sealed class ResolveLocationResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>Whether the location was resolved via "symbolId" or "occurrenceId".</summary>
    public string ResolvedBy { get; init; } = string.Empty;

    /// <summary>Relative file path where the symbol or occurrence is declared.</summary>
    public string? FilePath { get; init; }

    /// <summary>Source span within the file.</summary>
    public SourceSpan? SourceSpan { get; init; }

    /// <summary>Occurrence role.</summary>
    public OccurrenceRole? Role { get; init; }

    /// <summary>Whether this is the primary occurrence.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Error message if the symbol or occurrence was not found.</summary>
    public string? Error { get; init; }
}

/// <summary>One node in the namespace/module container tree.</summary>
public sealed class ContainerNode
{
    /// <summary>Simple name of this container (e.g., "Domain").</summary>
    public required string Name { get; init; }

    /// <summary>Fully qualified name (e.g., "MyApp.Domain").</summary>
    public required string QualifiedName { get; init; }

    /// <summary>Symbol kind of this container.</summary>
    public SymbolKind Kind { get; init; }

    /// <summary>Direct child containers.</summary>
    public IReadOnlyList<ContainerNode> Children { get; init; } = [];

    /// <summary>Number of symbols directly in this container (when counts are requested).</summary>
    public int SymbolCount { get; init; }

    /// <summary>Number of files contributing symbols to this container (when counts are requested).</summary>
    public int FileCount { get; init; }
}

/// <summary>Hierarchical namespace/module/package tree response.</summary>
public sealed class GetNamespaceTreeResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>Root container nodes of the tree.</summary>
    public IReadOnlyList<ContainerNode> Roots { get; init; } = [];
}

// ── Phase 4 — Dependency projection query types ───────────────────────────────

/// <summary>Request parameters for <see cref="IIndexQueryService.GetDependenciesAsync"/>.</summary>
public sealed class GetDependenciesRequest
{
    /// <summary>Symbol ID to query outgoing dependencies for.</summary>
    public required string SymbolId { get; init; }
    /// <summary>Optional snapshot ID. If null, the latest snapshot is used.</summary>
    public string? SnapshotId { get; init; }
    /// <summary>Optional filter to restrict results to specific relationship kinds.</summary>
    public IReadOnlyList<RelationshipKind>? RelationshipKinds { get; init; }
    /// <summary>If true (default), only include hard-bound dependencies (ExactBound, ScopedBound, ImportBound, AliasBound).</summary>
    public bool HardOnly { get; init; } = true;
    /// <summary>Whether to include evidence text in results.</summary>
    public bool IncludeEvidence { get; init; } = true;
    /// <summary>Whether to include confidence scores in results.</summary>
    public bool IncludeConfidence { get; init; } = true;
}

/// <summary>A summarised outgoing dependency from a source symbol to a target.</summary>
public sealed class DependencySummary
{
    /// <summary>Target symbol ID, or null if unresolved.</summary>
    public string? TargetSymbolId { get; init; }
    /// <summary>Qualified name of the target symbol.</summary>
    public string? QualifiedName { get; init; }
    /// <summary>Semantic kind of the target symbol.</summary>
    public SymbolKind? Kind { get; init; }
    /// <summary>Language ID of the target symbol.</summary>
    public string? LanguageId { get; init; }
    /// <summary>Relationship kind for this edge.</summary>
    public RelationshipKind RelationshipKind { get; init; }
    /// <summary>How confidently this reference was resolved.</summary>
    public ResolutionClass ResolutionClass { get; init; }
    /// <summary>Source text evidence for this dependency.</summary>
    public string? Evidence { get; init; }
    /// <summary>Source location of the evidence text.</summary>
    public SourceSpan? EvidenceSpan { get; init; }
    /// <summary>Confidence in [0.0, 1.0].</summary>
    public double Confidence { get; init; }
    /// <summary>How the resolution was determined (e.g. "exact", "import-qualified", "scoped", "alias", "heuristic").</summary>
    public string ResolutionMethod { get; init; } = "exact";
}

/// <summary>Response containing outgoing dependency projections for a symbol.</summary>
public sealed class GetDependenciesResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }
    /// <summary>The symbol ID that was queried.</summary>
    public required string SymbolId { get; init; }
    /// <summary>Outgoing dependency summaries for this page.</summary>
    public IReadOnlyList<DependencySummary> Dependencies { get; init; } = [];
    /// <summary>Error message if the symbol was not found.</summary>
    public string? Error { get; init; }
    /// <summary>Current page (one-based).</summary>
    public int Page { get; init; } = 1;
    /// <summary>Page size used.</summary>
    public int PageSize { get; init; } = 50;
    /// <summary>Total dependencies across all pages.</summary>
    public int TotalItems { get; init; }
    /// <summary>Whether additional pages are available.</summary>
    public bool HasMore { get; init; }
}

/// <summary>Request parameters for <see cref="IIndexQueryService.GetDependentsAsync"/>.</summary>
public sealed class GetDependentsRequest
{
    /// <summary>Symbol ID to query incoming dependents for.</summary>
    public required string SymbolId { get; init; }
    /// <summary>Optional snapshot ID. If null, the latest snapshot is used.</summary>
    public string? SnapshotId { get; init; }
    /// <summary>Optional filter to restrict results to specific relationship kinds.</summary>
    public IReadOnlyList<RelationshipKind>? RelationshipKinds { get; init; }
    /// <summary>If true (default), only include hard-bound dependents (ExactBound, ScopedBound, ImportBound, AliasBound).</summary>
    public bool HardOnly { get; init; } = true;
    /// <summary>Whether to include evidence text in results.</summary>
    public bool IncludeEvidence { get; init; } = true;
}

/// <summary>A summarised inbound dependent: a source symbol that depends on the queried target.</summary>
public sealed class DependentSummary
{
    /// <summary>Source symbol ID.</summary>
    public string? SourceSymbolId { get; init; }
    /// <summary>Qualified name of the source symbol.</summary>
    public string? QualifiedName { get; init; }
    /// <summary>Semantic kind of the source symbol.</summary>
    public SymbolKind? Kind { get; init; }
    /// <summary>Language ID of the source symbol.</summary>
    public string? LanguageId { get; init; }
    /// <summary>Relationship kind for this edge.</summary>
    public RelationshipKind RelationshipKind { get; init; }
    /// <summary>How confidently this reference was resolved.</summary>
    public ResolutionClass ResolutionClass { get; init; }
    /// <summary>Source text evidence for this dependency.</summary>
    public string? Evidence { get; init; }
    /// <summary>Source location of the evidence text.</summary>
    public SourceSpan? EvidenceSpan { get; init; }
    /// <summary>Confidence in [0.0, 1.0].</summary>
    public double Confidence { get; init; }
    /// <summary>How the resolution was determined (e.g. "exact", "import-qualified", "scoped", "alias", "heuristic").</summary>
    public string ResolutionMethod { get; init; } = "exact";
}

/// <summary>Response containing inbound dependent projections for a symbol.</summary>
public sealed class GetDependentsResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }
    /// <summary>The symbol ID that was queried.</summary>
    public required string SymbolId { get; init; }
    /// <summary>Inbound dependent summaries for this page.</summary>
    public IReadOnlyList<DependentSummary> Dependents { get; init; } = [];
    /// <summary>Error message if the symbol was not found.</summary>
    public string? Error { get; init; }
    /// <summary>Current page (one-based).</summary>
    public int Page { get; init; } = 1;
    /// <summary>Page size used.</summary>
    public int PageSize { get; init; } = 50;
    /// <summary>Total dependents across all pages.</summary>
    public int TotalItems { get; init; }
    /// <summary>Whether additional pages are available.</summary>
    public bool HasMore { get; init; }
}

/// <summary>Request parameters for <see cref="IIndexQueryService.GetSymbolRelationshipsAsync"/>.</summary>
public sealed class GetSymbolRelationshipsRequest
{
    /// <summary>Symbol ID to query relationships for.</summary>
    public required string SymbolId { get; init; }
    /// <summary>Optional snapshot ID. If null, the latest snapshot is used.</summary>
    public string? SnapshotId { get; init; }
    /// <summary>Whether to include raw reference edges. Default: true.</summary>
    public bool IncludeReferences { get; init; } = true;
    /// <summary>Whether to include outgoing dependency projections (hard-only). Default: true.</summary>
    public bool IncludeDependencies { get; init; } = true;
    /// <summary>Whether to include inbound dependent projections (hard-only). Default: true.</summary>
    public bool IncludeDependents { get; init; } = true;
    /// <summary>Whether to include direct child symbols. Default: true.</summary>
    public bool IncludeChildren { get; init; } = true;
    /// <summary>Maximum items to return per section. Null means unlimited.</summary>
    public int? MaxItemsPerSection { get; init; }
}

/// <summary>Consolidated relationship payload for a symbol.</summary>
public sealed class GetSymbolRelationshipsResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }
    /// <summary>The symbol ID that was queried.</summary>
    public required string SymbolId { get; init; }
    /// <summary>Direct child symbols, included when requested.</summary>
    public IReadOnlyList<SymbolSummary>? Children { get; init; }
    /// <summary>Raw reference edges (outgoing + incoming), included when requested.</summary>
    public IReadOnlyList<ReferenceSummary>? References { get; init; }
    /// <summary>Outgoing hard dependency projections, included when requested.</summary>
    public IReadOnlyList<DependencySummary>? Dependencies { get; init; }
    /// <summary>Inbound hard dependent projections, included when requested.</summary>
    public IReadOnlyList<DependentSummary>? Dependents { get; init; }
    /// <summary>Error message if the symbol was not found.</summary>
    public string? Error { get; init; }
}

/// <summary>Request parameters for <see cref="IIndexQueryService.GetFileDependenciesAsync"/>.</summary>
public sealed class GetFileDependenciesRequest
{
    /// <summary>Relative file path to query file-level dependencies for.</summary>
    public required string FilePath { get; init; }
    /// <summary>Optional snapshot ID. If null, the latest snapshot is used.</summary>
    public string? SnapshotId { get; init; }
    /// <summary>Whether to collapse all edges to the same target file into one entry. Default: true.</summary>
    public bool CollapseByTargetFile { get; init; } = true;
    /// <summary>If true, only include hard-bound edges. If false, include all edges. Default: false.</summary>
    public bool HardOnly { get; init; } = false;
}

/// <summary>A single example edge used to illustrate a file-level dependency.</summary>
public sealed class ReferenceExample
{
    /// <summary>Source symbol ID.</summary>
    public string? SourceSymbolId { get; init; }
    /// <summary>Target symbol ID.</summary>
    public string? TargetSymbolId { get; init; }
    /// <summary>Relationship kind for this edge.</summary>
    public string? RelationshipKind { get; init; }
    /// <summary>Evidence text for this edge.</summary>
    public string? Evidence { get; init; }
}

/// <summary>Summary of all reference edges from one file to another file.</summary>
public sealed class FileDependencySummary
{
    /// <summary>Relative file path of the target file.</summary>
    public required string TargetFilePath { get; init; }
    /// <summary>Distinct relationship kinds observed across all edges to this target file.</summary>
    public IReadOnlyList<string> RelationshipKinds { get; init; } = [];
    /// <summary>Total number of edges pointing to this target file.</summary>
    public int EvidenceCount { get; init; }
    /// <summary>Representative examples (up to 3).</summary>
    public IReadOnlyList<ReferenceExample> Examples { get; init; } = [];
}

/// <summary>Response containing file-level dependency projections.</summary>
public sealed class GetFileDependenciesResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }
    /// <summary>The file path that was queried.</summary>
    public required string FilePath { get; init; }
    /// <summary>File-level dependency summaries grouped by target file.</summary>
    public IReadOnlyList<FileDependencySummary> Dependencies { get; init; } = [];
    /// <summary>Error message if the file was not found.</summary>
    public string? Error { get; init; }
}

// ── Phase 3 — Reference and import query request/response types ──────────────

/// <summary>Request parameters for <see cref="IIndexQueryService.GetImportsAsync"/>.</summary>
public sealed class GetImportsRequest
{
    /// <summary>Relative file path to query imports for.</summary>
    public required string FilePath { get; init; }

    /// <summary>Optional snapshot ID. If null, the latest snapshot is used.</summary>
    public string? SnapshotId { get; init; }

    /// <summary>Repository root path; used to resolve the latest snapshot.</summary>
    public string? RootPath { get; init; }
}

/// <summary>Summarised view of a single import/using/require directive.</summary>
public sealed class ImportSummary
{
    /// <summary>Import kind (e.g., "using", "import", "from-import", "using-alias", "using-static").</summary>
    public required string Kind { get; init; }

    /// <summary>Raw import text as written in source.</summary>
    public required string RawText { get; init; }

    /// <summary>Normalised target module or namespace name.</summary>
    public string? NormalizedTarget { get; init; }

    /// <summary>Alias introduced by this import, if any.</summary>
    public string? Alias { get; init; }

    /// <summary>Source span of the directive.</summary>
    public SourceSpan? SourceSpan { get; init; }

    /// <summary>How confidently this import was resolved.</summary>
    public ResolutionClass ResolutionClass { get; init; }

    /// <summary>Resolved symbol ID, if the import maps to a repository-defined symbol.</summary>
    public string? ResolvedSymbolId { get; init; }
}

/// <summary>Response containing all imports for a single file.</summary>
public sealed class GetImportsResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>Relative file path that was queried.</summary>
    public string? FilePath { get; init; }

    /// <summary>Import entries found in the file, for this page.</summary>
    public IReadOnlyList<ImportSummary> Items { get; init; } = [];

    /// <summary>Error message if the file was not found or has no index data.</summary>
    public string? Error { get; init; }

    /// <summary>Current page (one-based).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size used.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Total imports across all pages.</summary>
    public int TotalItems { get; init; }

    /// <summary>Whether additional pages are available.</summary>
    public bool HasMore { get; init; }
}

/// <summary>Request parameters for <see cref="IIndexQueryService.GetReferencesAsync"/>.</summary>
public sealed class GetReferencesRequest
{
    /// <summary>Symbol ID to query references for.</summary>
    public required string SymbolId { get; init; }

    /// <summary>Optional snapshot ID. If null, the latest snapshot is used.</summary>
    public string? SnapshotId { get; init; }

    /// <summary>Whether to include outgoing references (from this symbol to others). Default: true.</summary>
    public bool IncludeOutgoing { get; init; } = true;

    /// <summary>Whether to include incoming references (other symbols referencing this one). Default: false.</summary>
    public bool IncludeIncoming { get; init; } = false;

    /// <summary>Optional filter to restrict results to specific relationship kinds.</summary>
    public IReadOnlyList<RelationshipKind>? KindFilter { get; init; }
}

/// <summary>Summarised view of a single reference edge.</summary>
public sealed class ReferenceSummary
{
    /// <summary>Unique edge identifier.</summary>
    public required string EdgeId { get; init; }

    /// <summary>Source symbol ID (the symbol making the reference).</summary>
    public string? SourceSymbolId { get; init; }

    /// <summary>Target symbol ID (the symbol being referenced), if resolved.</summary>
    public string? TargetSymbolId { get; init; }

    /// <summary>Semantic relationship kind.</summary>
    public RelationshipKind RelationshipKind { get; init; }

    /// <summary>How confidently this reference was resolved.</summary>
    public ResolutionClass ResolutionClass { get; init; }

    /// <summary>Source text evidence for this reference.</summary>
    public string? Evidence { get; init; }

    /// <summary>Source location of the evidence text.</summary>
    public SourceSpan? EvidenceSpan { get; init; }

    /// <summary>Language of the file where this reference was found.</summary>
    public required string LanguageId { get; init; }

    /// <summary>Confidence in [0.0, 1.0].</summary>
    public double Confidence { get; init; }

    /// <summary>How the resolution was determined (e.g. "exact", "import-qualified", "scoped", "alias", "heuristic").</summary>
    public string ResolutionMethod { get; init; } = "exact";
}

/// <summary>Response containing outgoing and/or incoming references for a symbol.</summary>
public sealed class GetReferencesResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>The symbol ID that was queried.</summary>
    public required string SymbolId { get; init; }

    /// <summary>References from this symbol to others (outgoing), paginated.</summary>
    public IReadOnlyList<ReferenceSummary> OutgoingRefs { get; init; } = [];

    /// <summary>References from other symbols to this one (incoming), always full list.</summary>
    public IReadOnlyList<ReferenceSummary> IncomingRefs { get; init; } = [];

    /// <summary>Error message if the symbol was not found.</summary>
    public string? Error { get; init; }

    /// <summary>Current page for outgoing refs (one-based).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size used for outgoing refs.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Total outgoing references across all pages.</summary>
    public int TotalOutgoing { get; init; }

    /// <summary>Total incoming references (always full).</summary>
    public int TotalIncoming { get; init; }

    /// <summary>Whether additional pages of outgoing refs are available.</summary>
    public bool HasMore { get; init; }
}

/// <summary>Request parameters for <see cref="IIndexQueryService.GetFileReferencesAsync"/>.</summary>
public sealed class GetFileReferencesRequest
{
    /// <summary>Relative file path to query references for.</summary>
    public required string FilePath { get; init; }

    /// <summary>Optional snapshot ID. If null, the latest snapshot is used.</summary>
    public string? SnapshotId { get; init; }

    /// <summary>Optional filter to restrict results to specific relationship kinds.</summary>
    public IReadOnlyList<RelationshipKind>? KindFilter { get; init; }
}

/// <summary>Response containing all reference edges associated with a file.</summary>
public sealed class GetFileReferencesResponse
{
    /// <summary>Snapshot ID the query ran against.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>Relative file path that was queried.</summary>
    public string? FilePath { get; init; }

    /// <summary>Reference edges found in or targeting the file, for this page.</summary>
    public IReadOnlyList<ReferenceSummary> Items { get; init; } = [];

    /// <summary>Error message if the file was not found.</summary>
    public string? Error { get; init; }

    /// <summary>Current page (one-based).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size used.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Total file references across all pages.</summary>
    public int TotalItems { get; init; }

    /// <summary>Whether additional pages are available.</summary>
    public bool HasMore { get; init; }
}

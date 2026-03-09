using StewardessMCPService.CodeIndexing.Model.References;
using StewardessMCPService.CodeIndexing.Model.Semantic;
using StewardessMCPService.CodeIndexing.Model.Snapshots;
using StewardessMCPService.CodeIndexing.Model.Structural;
using StewardessMCPService.CodeIndexing.Snapshots;

namespace StewardessMCPService.CodeIndexing.Query;

/// <summary>
/// In-memory implementation of <see cref="IIndexQueryService"/> backed by an <see cref="ISnapshotStore"/>.
/// </summary>
public sealed class InMemoryIndexQueryService : IIndexQueryService
{
    private readonly ISnapshotStore _store;

    /// <summary>Initializes a new instance of <see cref="InMemoryIndexQueryService"/> with the given snapshot store.</summary>
    /// <param name="store">The snapshot store to query against.</param>
    public InMemoryIndexQueryService(ISnapshotStore store)
    {
        _store = store;
    }

    /// <inheritdoc/>
    public async Task<SnapshotMetadata?> GetSnapshotInfoAsync(
        string? snapshotId, string? rootPath, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(snapshotId, rootPath, ct);
        return snapshot?.Metadata;
    }

    /// <inheritdoc/>
    public Task<IndexSnapshot?> GetSnapshotAsync(
        string? snapshotId, string? rootPath, CancellationToken ct = default) =>
        ResolveSnapshotAsync(snapshotId, rootPath, ct);

    /// <inheritdoc/>
    public async Task<ListFilesResponse> ListFilesAsync(
        ListFilesRequest request, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, request.RootPath, ct);
        if (snapshot is null)
        {
            return new ListFilesResponse
            {
                SnapshotId = request.SnapshotId ?? "(none)",
                Items = [],
                Page = request.Page,
                PageSize = request.PageSize,
                TotalItems = 0,
                HasMore = false,
            };
        }

        var files = snapshot.Files.Values.AsEnumerable();

        // Apply filters
        if (request.LanguageFilter?.Count > 0)
        {
            var langSet = new HashSet<string>(request.LanguageFilter, StringComparer.OrdinalIgnoreCase);
            files = files.Where(f => langSet.Contains(f.LanguageId));
        }

        if (request.ParseStatusFilter?.Count > 0)
        {
            var statusSet = new HashSet<ParseStatus>(request.ParseStatusFilter);
            files = files.Where(f => statusSet.Contains(f.ParseStatus));
        }

        if (request.EligibilityFilter?.Count > 0)
        {
            var eligSet = new HashSet<EligibilityStatus>(request.EligibilityFilter);
            files = files.Where(f => eligSet.Contains(f.EligibilityStatus));
        }

        if (!string.IsNullOrEmpty(request.PathPrefix))
        {
            var prefix = request.PathPrefix.Replace('\\', '/').TrimEnd('/') + "/";
            files = files.Where(f => f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                  || f.Path.Equals(request.PathPrefix, StringComparison.OrdinalIgnoreCase));
        }

        var list = files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        int total = list.Count;

        int page = Math.Max(1, request.Page);
        int pageSize = request.PageSize <= 0 ? 50 : request.PageSize;
        var paged = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var items = paged.Select(f => new FileListItem
        {
            FileId = f.FileId,
            Path = f.Path,
            LanguageId = f.LanguageId,
            ContentHash = f.ContentHash,
            EligibilityStatus = f.EligibilityStatus,
            ParseStatus = f.ParseStatus,
            TopLevelNodeCount = f.TopLevelNodeIds.Count,
            DiagnosticCount = f.DiagnosticIds.Count,
        }).ToList();

        return new ListFilesResponse
        {
            SnapshotId = snapshot.Metadata.SnapshotId,
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            HasMore = (page * pageSize) < total,
        };
    }

    /// <inheritdoc/>
    public async Task<FileOutlineResponse?> GetFileOutlineAsync(
        GetFileOutlineRequest request, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct)
            ?? await FindSnapshotWithFileAsync(request.FilePath, ct);

        if (snapshot is null) return null;

        var normalizedPath = request.FilePath.Replace('\\', '/');
        if (!snapshot.PathToFileId.TryGetValue(normalizedPath.ToLowerInvariant(), out var fileId))
        {
            // Try exact match (some stores may be case-sensitive on lookup)
            fileId = snapshot.Files.Values
                .FirstOrDefault(f => f.Path.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
                ?.FileId;
        }

        if (fileId is null || !snapshot.Files.TryGetValue(fileId, out var fileRecord))
            return null;

        if (!snapshot.FileIdToTopNodeIds.TryGetValue(fileId, out var topNodeIds))
            topNodeIds = fileRecord.TopLevelNodeIds;

        int maxDepth = request.MaxDepth ?? int.MaxValue;

        var rootNodes = topNodeIds
            .Where(id => snapshot.Nodes.ContainsKey(id))
            .Select(id => BuildOutlineNode(snapshot, id, 0, maxDepth, request))
            .ToList();

        return new FileOutlineResponse
        {
            SnapshotId = snapshot.Metadata.SnapshotId,
            FileId = fileId,
            Path = fileRecord.Path,
            LanguageId = fileRecord.LanguageId,
            ParseStatus = fileRecord.ParseStatus,
            RootNodes = rootNodes,
        };
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListRootPathsAsync(CancellationToken ct = default) =>
        _store.ListRootPathsAsync(ct);

    // ── Phase 2 — Symbol queries ──────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<FindSymbolsResponse> FindSymbolsAsync(
        FindSymbolsRequest request, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        var snapshotId = snapshot?.Metadata.SnapshotId ?? "(none)";

        if (snapshot is null || snapshot.Symbols.Count == 0)
        {
            return new FindSymbolsResponse
            {
                SnapshotId = snapshotId,
                Items = [],
                Page = request.Page,
                PageSize = request.PageSize,
                TotalItems = 0,
                HasMore = false,
            };
        }

        var query = request.QueryText;
        var mode = request.MatchMode ?? "prefix";

        IEnumerable<LogicalSymbol> matches = mode switch
        {
            "exact" => snapshot.SymbolsByName.TryGetValue(query, out var ids)
                ? ids.Select(id => snapshot.Symbols[id])
                : [],
            "contains" => snapshot.Symbols.Values
                .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                         || s.QualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase)),
            _ => snapshot.Symbols.Values // prefix
                .Where(s => s.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                         || s.QualifiedName.StartsWith(query, StringComparison.OrdinalIgnoreCase)),
        };

        if (request.LanguageFilter?.Count > 0)
        {
            var langSet = new HashSet<string>(request.LanguageFilter, StringComparer.OrdinalIgnoreCase);
            matches = matches.Where(s => langSet.Contains(s.LanguageId));
        }

        if (request.KindFilter?.Count > 0)
        {
            var kindSet = new HashSet<SymbolKind>(request.KindFilter);
            matches = matches.Where(s => kindSet.Contains(s.Kind));
        }

        if (request.ContainerFilter?.Count > 0)
        {
            matches = matches.Where(s => request.ContainerFilter.Any(prefix =>
                s.QualifiedName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)
                || s.QualifiedName.Equals(prefix, StringComparison.OrdinalIgnoreCase)));
        }

        var list = matches.OrderBy(s => s.QualifiedName, StringComparer.OrdinalIgnoreCase).ToList();
        int total = list.Count;
        int page = Math.Max(1, request.Page);
        int pageSize = request.PageSize <= 0 ? 50 : request.PageSize;
        var paged = list.Skip((page - 1) * pageSize).Take(pageSize);

        var items = paged.Select(sym => BuildSymbolSummary(
            sym, snapshot, request.IncludeOccurrenceCount, request.IncludeMembersSummary)).ToList();

        return new FindSymbolsResponse
        {
            SnapshotId = snapshotId,
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            HasMore = (page * pageSize) < total,
        };
    }

    /// <inheritdoc/>
    public async Task<GetSymbolResponse> GetSymbolAsync(
        GetSymbolRequest request, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        var snapshotId = snapshot?.Metadata.SnapshotId ?? "(none)";

        if (snapshot is null || !snapshot.Symbols.TryGetValue(request.SymbolId, out var symbol))
            return new GetSymbolResponse { SnapshotId = snapshotId, Symbol = null };

        SymbolLocation? primaryOccurrence = null;
        if (request.IncludePrimaryOccurrence &&
            snapshot.Occurrences.TryGetValue(symbol.PrimaryOccurrenceId, out var occ) &&
            snapshot.Files.TryGetValue(occ.FileId, out var file))
        {
            primaryOccurrence = new SymbolLocation
            {
                FilePath = file.Path,
                SourceSpan = occ.SourceSpan,
                Role = occ.Role,
                IsPrimary = occ.IsPrimary,
            };
        }

        MembersSummary? membersSummary = null;
        if (request.IncludeMembersSummary && IsTypeLike(symbol.Kind))
            membersSummary = BuildMembersSummary(symbol.SymbolId, snapshot);

        return new GetSymbolResponse
        {
            SnapshotId = snapshotId,
            Symbol = symbol,
            PrimaryOccurrence = primaryOccurrence,
            MembersSummary = membersSummary,
        };
    }

    /// <inheritdoc/>
    public async Task<GetSymbolOccurrencesResponse> GetSymbolOccurrencesAsync(
        GetSymbolOccurrencesRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        var snapshotId = snapshot?.Metadata.SnapshotId ?? "(none)";

        if (snapshot is null)
            return new GetSymbolOccurrencesResponse { SnapshotId = snapshotId, SymbolId = request.SymbolId };

        IEnumerable<SymbolOccurrence> occurrences = [];
        if (snapshot.OccurrencesBySymbolId.TryGetValue(request.SymbolId, out var occIds))
            occurrences = occIds.Where(id => snapshot.Occurrences.ContainsKey(id))
                                .Select(id => snapshot.Occurrences[id]);

        if (request.RoleFilter?.Count > 0)
        {
            var roleSet = new HashSet<OccurrenceRole>(request.RoleFilter);
            occurrences = occurrences.Where(o => roleSet.Contains(o.Role));
        }

        var allDetails = occurrences.Select(occ =>
        {
            var filePath = snapshot.Files.TryGetValue(occ.FileId, out var f) ? f.Path : occ.FileId;
            return new OccurrenceDetail
            {
                OccurrenceId = occ.OccurrenceId,
                FilePath = filePath,
                Role = occ.Role,
                SourceSpan = occ.SourceSpan,
                IsPrimary = occ.IsPrimary,
                Confidence = occ.Confidence,
                ExtractionMode = occ.ExtractionMode,
            };
        }).ToList();

        int totalItems = allDetails.Count;
        int skip = (page - 1) * pageSize;
        var pageItems = allDetails.Skip(skip).Take(pageSize).ToList();

        return new GetSymbolOccurrencesResponse
        {
            SnapshotId = snapshotId,
            SymbolId = request.SymbolId,
            Occurrences = pageItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            HasMore = skip + pageItems.Count < totalItems,
        };
    }

    /// <inheritdoc/>
    public async Task<GetSymbolChildrenResponse> GetSymbolChildrenAsync(
        GetSymbolChildrenRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        var snapshotId = snapshot?.Metadata.SnapshotId ?? "(none)";

        if (snapshot is null)
            return new GetSymbolChildrenResponse { SnapshotId = snapshotId, SymbolId = request.SymbolId };

        IEnumerable<LogicalSymbol> children = [];
        if (snapshot.ChildSymbolsByParentId.TryGetValue(request.SymbolId, out var childIds))
            children = childIds.Where(id => snapshot.Symbols.ContainsKey(id))
                               .Select(id => snapshot.Symbols[id]);

        if (request.KindFilter?.Count > 0)
        {
            var kindSet = new HashSet<SymbolKind>(request.KindFilter);
            children = children.Where(s => kindSet.Contains(s.Kind));
        }

        if (!request.IncludeNestedTypes)
            children = children.Where(s => !IsTypeLike(s.Kind));

        var allSummaries = children
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(sym => BuildSymbolSummary(sym, snapshot, includeOccurrenceCount: true, includeMembersSummary: false))
            .ToList();

        int totalItems = allSummaries.Count;
        int skip = (page - 1) * pageSize;
        var pageItems = allSummaries.Skip(skip).Take(pageSize).ToList();

        return new GetSymbolChildrenResponse
        {
            SnapshotId = snapshotId,
            SymbolId = request.SymbolId,
            Children = pageItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            HasMore = skip + pageItems.Count < totalItems,
        };
    }

    /// <inheritdoc/>
    public async Task<GetTypeMembersResponse> GetTypeMembersAsync(
        GetTypeMembersRequest request, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        var snapshotId = snapshot?.Metadata.SnapshotId ?? "(none)";

        if (snapshot is null)
            return new GetTypeMembersResponse { SnapshotId = snapshotId, TypeSymbolId = request.TypeSymbolId, Error = "No snapshot available." };

        if (!snapshot.Symbols.TryGetValue(request.TypeSymbolId, out var typeSymbol))
            return new GetTypeMembersResponse { SnapshotId = snapshotId, TypeSymbolId = request.TypeSymbolId, Error = "Symbol not found." };

        if (!IsTypeLike(typeSymbol.Kind))
            return new GetTypeMembersResponse { SnapshotId = snapshotId, TypeSymbolId = request.TypeSymbolId, Error = $"Symbol '{request.TypeSymbolId}' is not a type (kind: {typeSymbol.Kind})." };

        IEnumerable<LogicalSymbol> children = [];
        if (snapshot.ChildSymbolsByParentId.TryGetValue(request.TypeSymbolId, out var childIds))
            children = childIds.Where(id => snapshot.Symbols.ContainsKey(id))
                               .Select(id => snapshot.Symbols[id]);

        var childList = children.ToList();

        IReadOnlyList<SymbolSummary> ToSummaries(IEnumerable<LogicalSymbol> syms) =>
            syms.Select(sym => BuildSymbolSummary(sym, snapshot, includeOccurrenceCount: true, includeMembersSummary: false))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var constructors = ToSummaries(childList.Where(s => s.Kind == SymbolKind.Constructor));
        var methods = ToSummaries(childList.Where(s => s.Kind == SymbolKind.Method));
        var properties = ToSummaries(childList.Where(s => s.Kind == SymbolKind.Property));
        var fields = ToSummaries(childList.Where(s => s.Kind == SymbolKind.Field));
        var events = ToSummaries(childList.Where(s => s.Kind == SymbolKind.Event));
        var nestedTypes = request.IncludeNestedTypes
            ? ToSummaries(childList.Where(s => IsTypeLike(s.Kind)))
            : (IReadOnlyList<SymbolSummary>)[];

        return new GetTypeMembersResponse
        {
            SnapshotId = snapshotId,
            TypeSymbolId = request.TypeSymbolId,
            Constructors = constructors,
            Methods = methods,
            Properties = properties,
            Fields = fields,
            Events = events,
            NestedTypes = nestedTypes,
        };
    }

    /// <inheritdoc/>
    public async Task<ResolveLocationResponse> ResolveLocationAsync(
        ResolveLocationRequest request, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        var snapshotId = snapshot?.Metadata.SnapshotId ?? "(none)";

        if (snapshot is null)
            return new ResolveLocationResponse { SnapshotId = snapshotId, Error = "No snapshot available." };

        if (request.SymbolId is null && request.OccurrenceId is null)
            return new ResolveLocationResponse { SnapshotId = snapshotId, Error = "Either symbolId or occurrenceId must be provided." };

        SymbolOccurrence? occ = null;
        string resolvedBy;

        if (request.OccurrenceId is not null)
        {
            resolvedBy = "occurrenceId";
            snapshot.Occurrences.TryGetValue(request.OccurrenceId, out occ);
        }
        else
        {
            resolvedBy = "symbolId";
            if (snapshot.Symbols.TryGetValue(request.SymbolId!, out var sym))
                snapshot.Occurrences.TryGetValue(sym.PrimaryOccurrenceId, out occ);
        }

        if (occ is null)
            return new ResolveLocationResponse { SnapshotId = snapshotId, ResolvedBy = resolvedBy, Error = "Symbol or occurrence not found." };

        var filePath = snapshot.Files.TryGetValue(occ.FileId, out var file) ? file.Path : occ.FileId;

        return new ResolveLocationResponse
        {
            SnapshotId = snapshotId,
            ResolvedBy = resolvedBy,
            FilePath = filePath,
            SourceSpan = occ.SourceSpan,
            Role = occ.Role,
            IsPrimary = occ.IsPrimary,
        };
    }

    /// <inheritdoc/>
    public async Task<GetNamespaceTreeResponse> GetNamespaceTreeAsync(
        GetNamespaceTreeRequest request, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        var snapshotId = snapshot?.Metadata.SnapshotId ?? "(none)";

        if (snapshot is null || snapshot.Symbols.Count == 0)
            return new GetNamespaceTreeResponse { SnapshotId = snapshotId, Roots = [] };

        var containerKinds = new HashSet<SymbolKind>
        {
            SymbolKind.Namespace, SymbolKind.Module, SymbolKind.Package, SymbolKind.Script,
        };

        IEnumerable<LogicalSymbol> containers = snapshot.Symbols.Values
            .Where(s => containerKinds.Contains(s.Kind));

        if (request.LanguageFilter?.Count > 0)
        {
            var langSet = new HashSet<string>(request.LanguageFilter, StringComparer.OrdinalIgnoreCase);
            containers = containers.Where(s => langSet.Contains(s.LanguageId));
        }

        if (!string.IsNullOrEmpty(request.RootContainer))
        {
            var prefix = request.RootContainer;
            containers = containers.Where(s =>
                s.QualifiedName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)
                || s.QualifiedName.Equals(prefix, StringComparison.OrdinalIgnoreCase));
        }

        // Build tree from the flat list of container symbols
        var roots = BuildContainerTree(
            containers.ToList(), snapshot, request.MaxDepth ?? int.MaxValue, request.IncludeCounts);

        return new GetNamespaceTreeResponse { SnapshotId = snapshotId, Roots = roots };
    }

    // ── Private helpers ───────────────────────────────────────────────────────
    private async Task<IndexSnapshot?> ResolveSnapshotAsync(
        string? snapshotId, string? rootPath, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(snapshotId))
            return await _store.GetSnapshotByIdAsync(snapshotId, ct);

        if (!string.IsNullOrEmpty(rootPath))
            return await _store.GetLatestSnapshotAsync(rootPath, ct);

        // Return any available snapshot (first root path found)
        var roots = await _store.ListRootPathsAsync(ct);
        if (roots.Count > 0)
            return await _store.GetLatestSnapshotAsync(roots[0], ct);

        return null;
    }

    private async Task<IndexSnapshot?> FindSnapshotWithFileAsync(string filePath, CancellationToken ct)
    {
        var roots = await _store.ListRootPathsAsync(ct);
        var normalizedPath = filePath.Replace('\\', '/').ToLowerInvariant();

        foreach (var root in roots)
        {
            var snapshot = await _store.GetLatestSnapshotAsync(root, ct);
            if (snapshot is not null && snapshot.PathToFileId.ContainsKey(normalizedPath))
                return snapshot;
        }

        return null;
    }

    private static OutlineNode BuildOutlineNode(
        IndexSnapshot snapshot, string nodeId, int depth, int maxDepth,
        GetFileOutlineRequest request)
    {
        var node = snapshot.Nodes[nodeId];

        var children = depth < maxDepth
            ? node.Children
                .Where(cid => snapshot.Nodes.ContainsKey(cid))
                .Where(cid => request.IncludeNonSemanticNodes || IsSemanticNode(snapshot.Nodes[cid]))
                .Select(cid => BuildOutlineNode(snapshot, cid, depth + 1, maxDepth, request))
                .ToList()
            : (IReadOnlyList<OutlineNode>)[];

        return new OutlineNode
        {
            NodeId = node.NodeId,
            Kind = node.Kind,
            Subkind = node.Subkind,
            Name = node.Name,
            DisplayName = node.DisplayName,
            SourceSpan = request.IncludeSourceSpans ? node.SourceSpan : null,
            Confidence = request.IncludeConfidence ? node.Confidence : null,
            Children = children,
        };
    }

    private static bool IsSemanticNode(StructuralNode node) =>
        node.Kind is NodeKind.Declaration or NodeKind.Callable or NodeKind.Member;

    /// <summary>Returns true if <paramref name="kind"/> represents a type-like symbol.</summary>
    private static bool IsTypeLike(SymbolKind kind) => kind is
        SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
        or SymbolKind.Enum or SymbolKind.Record or SymbolKind.Trait
        or SymbolKind.Union or SymbolKind.TypeAlias or SymbolKind.Object;

    /// <summary>Builds a <see cref="MembersSummary"/> for a type symbol by counting its children.</summary>
    private static MembersSummary BuildMembersSummary(string symbolId, IndexSnapshot snapshot)
    {
        if (!snapshot.ChildSymbolsByParentId.TryGetValue(symbolId, out var childIds))
            return new MembersSummary();

        int ctors = 0, methods = 0, props = 0, fields = 0, events = 0, nested = 0;
        foreach (var id in childIds)
        {
            if (!snapshot.Symbols.TryGetValue(id, out var child)) continue;
            switch (child.Kind)
            {
                case SymbolKind.Constructor: ctors++; break;
                case SymbolKind.Method: methods++; break;
                case SymbolKind.Property: props++; break;
                case SymbolKind.Field: fields++; break;
                case SymbolKind.Event: events++; break;
                default:
                    if (IsTypeLike(child.Kind)) nested++;
                    break;
            }
        }
        return new MembersSummary
        {
            ConstructorCount = ctors,
            MethodCount = methods,
            PropertyCount = props,
            FieldCount = fields,
            EventCount = events,
            NestedTypeCount = nested,
        };
    }

    /// <summary>Builds a <see cref="SymbolSummary"/> from a logical symbol.</summary>
    private static SymbolSummary BuildSymbolSummary(
        LogicalSymbol sym, IndexSnapshot snapshot,
        bool includeOccurrenceCount, bool includeMembersSummary)
    {
        SymbolLocation? primaryLocation = null;
        if (snapshot.Occurrences.TryGetValue(sym.PrimaryOccurrenceId, out var occ) &&
            snapshot.Files.TryGetValue(occ.FileId, out var file))
        {
            primaryLocation = new SymbolLocation
            {
                FilePath = file.Path,
                SourceSpan = occ.SourceSpan,
                Role = occ.Role,
                IsPrimary = occ.IsPrimary,
            };
        }

        int occCount = 0;
        if (includeOccurrenceCount &&
            snapshot.OccurrencesBySymbolId.TryGetValue(sym.SymbolId, out var occIds))
            occCount = occIds.Count;

        MembersSummary? membersSummary = null;
        if (includeMembersSummary && IsTypeLike(sym.Kind))
            membersSummary = BuildMembersSummary(sym.SymbolId, snapshot);

        return new SymbolSummary
        {
            SymbolId = sym.SymbolId,
            SymbolKey = sym.SymbolKey,
            Name = sym.Name,
            QualifiedName = sym.QualifiedName,
            Kind = sym.Kind,
            Subkind = sym.Subkind,
            LanguageId = sym.LanguageId,
            ContainerPath = sym.ContainerPath,
            PrimaryLocation = primaryLocation,
            Confidence = sym.Confidence,
            OccurrenceCount = occCount,
            MembersSummary = membersSummary,
        };
    }

    /// <summary>
    /// Builds a hierarchical container tree from a flat list of container symbols.
    /// </summary>
    private static IReadOnlyList<ContainerNode> BuildContainerTree(
        IReadOnlyList<LogicalSymbol> containers,
        IndexSnapshot snapshot,
        int maxDepth,
        bool includeCounts)
    {
        // Sort by qualified name so parents come before children
        var sorted = containers.OrderBy(c => c.QualifiedName, StringComparer.OrdinalIgnoreCase).ToList();

        // Group non-container children by their parent container
        var childSymbolCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fileCountMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        if (includeCounts)
        {
            foreach (var sym in snapshot.Symbols.Values)
            {
                if (sym.ContainerPath.Count == 0) continue;
                var containerQn = string.Join(".", sym.ContainerPath);
                if (!childSymbolCounts.ContainsKey(containerQn))
                    childSymbolCounts[containerQn] = 0;
                childSymbolCounts[containerQn]++;
                if (!fileCountMap.TryGetValue(containerQn, out var files))
                    fileCountMap[containerQn] = files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                files.Add(sym.PrimaryFileId);
            }
        }

        IReadOnlyList<ContainerNode> BuildChildren(string parentQn, int depth)
        {
            if (depth >= maxDepth) return [];
            return sorted
                .Where(c =>
                {
                    var parts = c.QualifiedName.Split('.');
                    if (parts.Length < 2) return string.IsNullOrEmpty(parentQn);
                    var parentPart = string.Join(".", parts[..^1]);
                    return parentPart.Equals(parentQn, StringComparison.OrdinalIgnoreCase);
                })
                .Select(c => new ContainerNode
                {
                    Name = c.Name,
                    QualifiedName = c.QualifiedName,
                    Kind = c.Kind,
                    Children = BuildChildren(c.QualifiedName, depth + 1),
                    SymbolCount = includeCounts && childSymbolCounts.TryGetValue(c.QualifiedName, out var cnt) ? cnt : 0,
                    FileCount = includeCounts && fileCountMap.TryGetValue(c.QualifiedName, out var fset) ? fset.Count : 0,
                })
                .ToList();
        }

        // Top-level: containers with no parent container in the list
        var topLevel = sorted.Where(c => !c.QualifiedName.Contains('.') ||
            !sorted.Any(p => c.QualifiedName.StartsWith(p.QualifiedName + ".", StringComparison.OrdinalIgnoreCase))).ToList();

        return topLevel.Select(c => new ContainerNode
        {
            Name = c.Name,
            QualifiedName = c.QualifiedName,
            Kind = c.Kind,
            Children = BuildChildren(c.QualifiedName, 1),
            SymbolCount = includeCounts && childSymbolCounts.TryGetValue(c.QualifiedName, out var cnt) ? cnt : 0,
            FileCount = includeCounts && fileCountMap.TryGetValue(c.QualifiedName, out var fset) ? fset.Count : 0,
        }).ToList();
    }

    // ── Phase 3 — Reference and import queries ────────────────────────────────

    /// <inheritdoc/>
    public async Task<GetImportsResponse> GetImportsAsync(
        GetImportsRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, request.RootPath, ct);
        if (snapshot is null)
            return new GetImportsResponse
            {
                SnapshotId = request.SnapshotId ?? "(none)",
                FilePath = request.FilePath,
                Error = "No snapshot found.",
            };

        var normalizedPath = request.FilePath.Replace('\\', '/');
        if (!snapshot.PathToFileId.TryGetValue(normalizedPath.ToLowerInvariant(), out var fileId))
            return new GetImportsResponse
            {
                SnapshotId = snapshot.Metadata.SnapshotId,
                FilePath = request.FilePath,
                Error = $"File not found in snapshot: {request.FilePath}",
            };

        if (!snapshot.ImportsByFileId.TryGetValue(fileId, out var imports))
            imports = [];

        var allItems = imports.Select(ToImportSummary).ToList();
        int totalItems = allItems.Count;
        int skip = (page - 1) * pageSize;
        var pageItems = allItems.Skip(skip).Take(pageSize).ToList();

        return new GetImportsResponse
        {
            SnapshotId = snapshot.Metadata.SnapshotId,
            FilePath = request.FilePath,
            Items = pageItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            HasMore = skip + pageItems.Count < totalItems,
        };
    }

    /// <inheritdoc/>
    public async Task<GetReferencesResponse> GetReferencesAsync(
        GetReferencesRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        if (snapshot is null)
            return new GetReferencesResponse
            {
                SnapshotId = request.SnapshotId ?? "(none)",
                SymbolId = request.SymbolId,
                Error = "No snapshot found.",
            };

        if (!snapshot.Symbols.ContainsKey(request.SymbolId))
            return new GetReferencesResponse
            {
                SnapshotId = snapshot.Metadata.SnapshotId,
                SymbolId = request.SymbolId,
                Error = $"Symbol not found: {request.SymbolId}",
            };

        var allOutgoing = new List<ReferenceSummary>();
        var incoming = new List<ReferenceSummary>();

        if (request.IncludeOutgoing &&
            snapshot.ReferencesBySourceSymbolId.TryGetValue(request.SymbolId, out var outEdgeIds))
        {
            foreach (var edgeId in outEdgeIds)
            {
                if (!snapshot.References.TryGetValue(edgeId, out var edge)) continue;
                if (request.KindFilter?.Count > 0 && !request.KindFilter.Contains(edge.RelationshipKind)) continue;
                allOutgoing.Add(ToReferenceSummary(edge));
            }
        }

        if (request.IncludeIncoming)
        {
            foreach (var edge in snapshot.References.Values)
            {
                if (edge.TargetSymbolId != request.SymbolId) continue;
                if (request.KindFilter?.Count > 0 && !request.KindFilter.Contains(edge.RelationshipKind)) continue;
                incoming.Add(ToReferenceSummary(edge));
            }
        }

        int totalOutgoing = allOutgoing.Count;
        int totalIncoming = incoming.Count;
        int skip = (page - 1) * pageSize;
        var pageOutgoing = allOutgoing.Skip(skip).Take(pageSize).ToList();

        return new GetReferencesResponse
        {
            SnapshotId = snapshot.Metadata.SnapshotId,
            SymbolId = request.SymbolId,
            OutgoingRefs = pageOutgoing,
            IncomingRefs = incoming,
            Page = page,
            PageSize = pageSize,
            TotalOutgoing = totalOutgoing,
            TotalIncoming = totalIncoming,
            HasMore = skip + pageOutgoing.Count < totalOutgoing,
        };
    }

    /// <inheritdoc/>
    public async Task<GetFileReferencesResponse> GetFileReferencesAsync(
        GetFileReferencesRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        if (snapshot is null)
            return new GetFileReferencesResponse
            {
                SnapshotId = request.SnapshotId ?? "(none)",
                FilePath = request.FilePath,
                Error = "No snapshot found.",
            };

        var normalizedPath = request.FilePath.Replace('\\', '/');
        if (!snapshot.PathToFileId.TryGetValue(normalizedPath.ToLowerInvariant(), out var fileId))
            return new GetFileReferencesResponse
            {
                SnapshotId = snapshot.Metadata.SnapshotId,
                FilePath = request.FilePath,
                Error = $"File not found in snapshot: {request.FilePath}",
            };

        var allItems = new List<ReferenceSummary>();
        if (snapshot.ReferencesByFileId.TryGetValue(fileId, out var edgeIds))
        {
            foreach (var edgeId in edgeIds)
            {
                if (!snapshot.References.TryGetValue(edgeId, out var edge)) continue;
                if (request.KindFilter?.Count > 0 && !request.KindFilter.Contains(edge.RelationshipKind)) continue;
                allItems.Add(ToReferenceSummary(edge));
            }
        }

        int totalItems = allItems.Count;
        int skip = (page - 1) * pageSize;
        var pageItems = allItems.Skip(skip).Take(pageSize).ToList();

        return new GetFileReferencesResponse
        {
            SnapshotId = snapshot.Metadata.SnapshotId,
            FilePath = request.FilePath,
            Items = pageItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            HasMore = skip + pageItems.Count < totalItems,
        };
    }

    // ── Phase 4 — Dependency projection queries ───────────────────────────────

    /// <inheritdoc/>
    public async Task<GetDependenciesResponse> GetDependenciesAsync(
        GetDependenciesRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        var snapshotId = snapshot?.Metadata.SnapshotId ?? "(none)";

        if (snapshot is null)
            return new GetDependenciesResponse { SnapshotId = snapshotId, SymbolId = request.SymbolId, Error = "No snapshot found." };

        if (!snapshot.Symbols.ContainsKey(request.SymbolId))
            return new GetDependenciesResponse { SnapshotId = snapshotId, SymbolId = request.SymbolId, Error = $"Symbol not found: {request.SymbolId}" };

        var allDeps = new List<DependencySummary>();

        if (snapshot.ReferencesBySourceSymbolId.TryGetValue(request.SymbolId, out var edgeIds))
        {
            foreach (var edgeId in edgeIds)
            {
                if (!snapshot.References.TryGetValue(edgeId, out var edge)) continue;
                if (request.HardOnly && !IsHardDependency(edge.ResolutionClass)) continue;
                if (request.RelationshipKinds?.Count > 0 && !request.RelationshipKinds.Contains(edge.RelationshipKind)) continue;

                string? qualifiedName = null;
                SymbolKind? kind = null;
                string? langId = null;
                if (edge.TargetSymbolId is not null && snapshot.Symbols.TryGetValue(edge.TargetSymbolId, out var targetSym))
                {
                    qualifiedName = targetSym.QualifiedName;
                    kind = targetSym.Kind;
                    langId = targetSym.LanguageId;
                }

                var (_, method) = GetConfidence(edge.ResolutionClass);
                allDeps.Add(new DependencySummary
                {
                    TargetSymbolId   = edge.TargetSymbolId,
                    QualifiedName    = qualifiedName,
                    Kind             = kind,
                    LanguageId       = langId,
                    RelationshipKind = edge.RelationshipKind,
                    ResolutionClass  = edge.ResolutionClass,
                    Evidence         = request.IncludeEvidence ? edge.Evidence : null,
                    EvidenceSpan     = request.IncludeEvidence ? edge.EvidenceSpan : null,
                    Confidence       = request.IncludeConfidence ? edge.Confidence : 0,
                    ResolutionMethod = method,
                });
            }
        }

        int totalItems = allDeps.Count;
        int skip = (page - 1) * pageSize;
        var pageItems = allDeps.Skip(skip).Take(pageSize).ToList();

        return new GetDependenciesResponse
        {
            SnapshotId = snapshotId,
            SymbolId = request.SymbolId,
            Dependencies = pageItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            HasMore = skip + pageItems.Count < totalItems,
        };
    }

    /// <inheritdoc/>
    public async Task<GetDependentsResponse> GetDependentsAsync(
        GetDependentsRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        var snapshotId = snapshot?.Metadata.SnapshotId ?? "(none)";

        if (snapshot is null)
            return new GetDependentsResponse { SnapshotId = snapshotId, SymbolId = request.SymbolId, Error = "No snapshot found." };

        if (!snapshot.Symbols.ContainsKey(request.SymbolId))
            return new GetDependentsResponse { SnapshotId = snapshotId, SymbolId = request.SymbolId, Error = $"Symbol not found: {request.SymbolId}" };

        var allDependents = new List<DependentSummary>();

        foreach (var edge in snapshot.References.Values)
        {
            if (edge.TargetSymbolId != request.SymbolId) continue;
            if (request.HardOnly && !IsHardDependency(edge.ResolutionClass)) continue;
            if (request.RelationshipKinds?.Count > 0 && !request.RelationshipKinds.Contains(edge.RelationshipKind)) continue;

            string? qualifiedName = null;
            SymbolKind? kind = null;
            string? langId = null;
            if (edge.SourceSymbolId is not null && snapshot.Symbols.TryGetValue(edge.SourceSymbolId, out var srcSym))
            {
                qualifiedName = srcSym.QualifiedName;
                kind = srcSym.Kind;
                langId = srcSym.LanguageId;
            }

            var (_, method) = GetConfidence(edge.ResolutionClass);
            allDependents.Add(new DependentSummary
            {
                SourceSymbolId   = edge.SourceSymbolId,
                QualifiedName    = qualifiedName,
                Kind             = kind,
                LanguageId       = langId,
                RelationshipKind = edge.RelationshipKind,
                ResolutionClass  = edge.ResolutionClass,
                Evidence         = request.IncludeEvidence ? edge.Evidence : null,
                EvidenceSpan     = request.IncludeEvidence ? edge.EvidenceSpan : null,
                Confidence       = edge.Confidence,
                ResolutionMethod = method,
            });
        }

        int totalItems = allDependents.Count;
        int skip = (page - 1) * pageSize;
        var pageItems = allDependents.Skip(skip).Take(pageSize).ToList();

        return new GetDependentsResponse
        {
            SnapshotId = snapshotId,
            SymbolId = request.SymbolId,
            Dependents = pageItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            HasMore = skip + pageItems.Count < totalItems,
        };
    }

    /// <inheritdoc/>
    public async Task<GetSymbolRelationshipsResponse> GetSymbolRelationshipsAsync(
        GetSymbolRelationshipsRequest request, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        var snapshotId = snapshot?.Metadata.SnapshotId ?? "(none)";

        if (snapshot is null)
            return new GetSymbolRelationshipsResponse { SnapshotId = snapshotId, SymbolId = request.SymbolId, Error = "No snapshot found." };

        if (!snapshot.Symbols.ContainsKey(request.SymbolId))
            return new GetSymbolRelationshipsResponse { SnapshotId = snapshotId, SymbolId = request.SymbolId, Error = $"Symbol not found: {request.SymbolId}" };

        int limit = request.MaxItemsPerSection ?? int.MaxValue;

        IReadOnlyList<SymbolSummary>? children = null;
        if (request.IncludeChildren)
        {
            var childResp = await GetSymbolChildrenAsync(
                new GetSymbolChildrenRequest { SymbolId = request.SymbolId, SnapshotId = snapshotId }, 1, int.MaxValue, ct);
            children = childResp.Children.Take(limit).ToList();
        }

        IReadOnlyList<ReferenceSummary>? references = null;
        if (request.IncludeReferences)
        {
            var refResp = await GetReferencesAsync(
                new GetReferencesRequest
                {
                    SymbolId        = request.SymbolId,
                    SnapshotId      = snapshotId,
                    IncludeOutgoing = true,
                    IncludeIncoming = true,
                }, 1, int.MaxValue, ct);
            references = refResp.OutgoingRefs.Concat(refResp.IncomingRefs).Take(limit).ToList();
        }

        IReadOnlyList<DependencySummary>? deps = null;
        if (request.IncludeDependencies)
        {
            var depResp = await GetDependenciesAsync(
                new GetDependenciesRequest { SymbolId = request.SymbolId, SnapshotId = snapshotId }, 1, int.MaxValue, ct);
            deps = depResp.Dependencies.Take(limit).ToList();
        }

        IReadOnlyList<DependentSummary>? dependents = null;
        if (request.IncludeDependents)
        {
            var depsResp = await GetDependentsAsync(
                new GetDependentsRequest { SymbolId = request.SymbolId, SnapshotId = snapshotId }, 1, int.MaxValue, ct);
            dependents = depsResp.Dependents.Take(limit).ToList();
        }

        return new GetSymbolRelationshipsResponse
        {
            SnapshotId   = snapshotId,
            SymbolId     = request.SymbolId,
            Children     = children,
            References   = references,
            Dependencies = deps,
            Dependents   = dependents,
        };
    }

    /// <inheritdoc/>
    public async Task<GetFileDependenciesResponse> GetFileDependenciesAsync(
        GetFileDependenciesRequest request, CancellationToken ct = default)
    {
        var snapshot = await ResolveSnapshotAsync(request.SnapshotId, null, ct);
        var snapshotId = snapshot?.Metadata.SnapshotId ?? "(none)";

        if (snapshot is null)
            return new GetFileDependenciesResponse { SnapshotId = snapshotId, FilePath = request.FilePath, Error = "No snapshot found." };

        var normalizedPath = request.FilePath.Replace('\\', '/');
        if (!snapshot.PathToFileId.TryGetValue(normalizedPath.ToLowerInvariant(), out var fileId))
            return new GetFileDependenciesResponse
            {
                SnapshotId = snapshotId,
                FilePath   = request.FilePath,
                Error      = $"File not found in snapshot: {request.FilePath}",
            };

        if (!snapshot.ReferencesByFileId.TryGetValue(fileId, out var edgeIds))
            return new GetFileDependenciesResponse { SnapshotId = snapshotId, FilePath = request.FilePath, Dependencies = [] };

        var grouped = new Dictionary<string, List<ReferenceEdge>>(StringComparer.OrdinalIgnoreCase);

        foreach (var edgeId in edgeIds)
        {
            if (!snapshot.References.TryGetValue(edgeId, out var edge)) continue;
            if (request.HardOnly && !IsHardDependency(edge.ResolutionClass)) continue;
            if (edge.TargetSymbolId is null) continue;

            if (!snapshot.Symbols.TryGetValue(edge.TargetSymbolId, out var targetSym)) continue;
            if (!snapshot.Files.TryGetValue(targetSym.PrimaryFileId, out var targetFile)) continue;

            // Skip self-references (same file)
            if (targetFile.FileId == fileId) continue;

            if (!grouped.TryGetValue(targetFile.Path, out var edgeList))
                grouped[targetFile.Path] = edgeList = new List<ReferenceEdge>();
            edgeList.Add(edge);
        }

        var result = grouped
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp =>
            {
                var edges = kvp.Value;
                var kinds = edges.Select(e => e.RelationshipKind.ToString())
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                                 .ToList();
                var examples = edges.Take(3).Select(e => new ReferenceExample
                {
                    SourceSymbolId   = e.SourceSymbolId,
                    TargetSymbolId   = e.TargetSymbolId,
                    RelationshipKind = e.RelationshipKind.ToString(),
                    Evidence         = e.Evidence,
                }).ToList();
                return new FileDependencySummary
                {
                    TargetFilePath    = kvp.Key,
                    RelationshipKinds = kinds,
                    EvidenceCount     = edges.Count,
                    Examples          = examples,
                };
            })
            .ToList();

        return new GetFileDependenciesResponse { SnapshotId = snapshotId, FilePath = request.FilePath, Dependencies = result };
    }

    // ── Phase 3 conversion helpers ────────────────────────────────────────────

    private static ImportSummary ToImportSummary(ImportEntry e) => new()
    {
        Kind = e.Kind,
        RawText = e.RawText,
        NormalizedTarget = e.NormalizedTarget,
        Alias = e.Alias,
        SourceSpan = e.SourceSpan,
        ResolutionClass = e.ResolutionClass,
        ResolvedSymbolId = e.ResolvedSymbolId,
    };

    private static ReferenceSummary ToReferenceSummary(ReferenceEdge e)
    {
        var (_, method) = e.TargetSymbolId != null
            ? GetConfidence(e.ResolutionClass)
            : (0.3, "ambiguous");
        return new ReferenceSummary
        {
            EdgeId           = e.EdgeId,
            SourceSymbolId   = e.SourceSymbolId,
            TargetSymbolId   = e.TargetSymbolId,
            RelationshipKind = e.RelationshipKind,
            ResolutionClass  = e.ResolutionClass,
            Evidence         = e.Evidence,
            EvidenceSpan     = e.EvidenceSpan,
            LanguageId       = e.LanguageId,
            Confidence       = e.Confidence,
            ResolutionMethod = method,
        };
    }

    /// <summary>Maps a <see cref="ResolutionClass"/> to a (confidence, method) tuple.</summary>
    private static (double confidence, string method) GetConfidence(ResolutionClass rc) => rc switch
    {
        ResolutionClass.ExactBound  => (1.0,  "exact"),
        ResolutionClass.ScopedBound => (0.95, "scoped"),
        ResolutionClass.ImportBound => (0.9,  "import-qualified"),
        ResolutionClass.AliasBound  => (0.85, "alias"),
        ResolutionClass.Ambiguous   => (0.3,  "ambiguous"),
        ResolutionClass.External    => (0.8,  "external"),
        _                           => (0.1,  "unresolved"),
    };

    private static bool IsHardDependency(ResolutionClass rc) =>
        rc is ResolutionClass.ExactBound or ResolutionClass.ScopedBound
           or ResolutionClass.ImportBound or ResolutionClass.AliasBound;
}

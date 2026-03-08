using StewardessMCPServive.CodeIndexing.Eligibility;
using StewardessMCPServive.CodeIndexing.LanguageDetection;
using StewardessMCPServive.CodeIndexing.Model.Diagnostics;
using StewardessMCPServive.CodeIndexing.Model.References;
using StewardessMCPServive.CodeIndexing.Model.Semantic;
using StewardessMCPServive.CodeIndexing.Model.Snapshots;
using StewardessMCPServive.CodeIndexing.Model.Structural;
using StewardessMCPServive.CodeIndexing.Parsers.Abstractions;
using StewardessMCPServive.CodeIndexing.Projection;
using StewardessMCPServive.CodeIndexing.Snapshots;
using StewardessMCPServive.CodeIndexing.Source;

namespace StewardessMCPServive.CodeIndexing.Indexing;

/// <summary>
/// Orchestrates the indexing pipeline: enumerate → detect → parse → build snapshot.
/// </summary>
public sealed class IndexingEngine : IIndexingEngine
{
    private readonly ISourceProvider _source;
    private readonly IEligibilityPolicy _eligibility;
    private readonly ILanguageDetector _languageDetector;
    private readonly IReadOnlyDictionary<string, IParserAdapter> _adapters;
    private readonly ISnapshotStore _store;
    private readonly IReadOnlyDictionary<string, ISymbolProjector> _projectors;

    // Per-root state tracking
    private readonly Dictionary<string, IndexState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _latestSnapshotIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _lastErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastCompletedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();

    /// <summary>
    /// Initializes a new instance of <see cref="IndexingEngine"/>.
    /// </summary>
    /// <param name="source">Source provider for file enumeration and content reading.</param>
    /// <param name="eligibility">Eligibility policy for filtering files.</param>
    /// <param name="languageDetector">Language detector for mapping files to language IDs.</param>
    /// <param name="adapters">Parser adapters, one per supported language.</param>
    /// <param name="store">Snapshot store for persisting published snapshots.</param>
    /// <param name="projectors">Optional symbol projectors for Phase 2 semantic projection.</param>
    public IndexingEngine(
        ISourceProvider source,
        IEligibilityPolicy eligibility,
        ILanguageDetector languageDetector,
        IEnumerable<IParserAdapter> adapters,
        ISnapshotStore store,
        IEnumerable<ISymbolProjector>? projectors = null)
    {
        _source = source;
        _eligibility = eligibility;
        _languageDetector = languageDetector;
        _adapters = adapters.ToDictionary(a => a.LanguageId, StringComparer.OrdinalIgnoreCase);
        _store = store;
        _projectors = projectors?.ToDictionary(p => p.LanguageId, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ISymbolProjector>(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<IndexBuildResult> BuildAsync(IndexBuildRequest request, CancellationToken ct = default)
    {
        var rootPath = request.RootPath;
        var startedAt = DateTimeOffset.UtcNow;

        SetState(rootPath, IndexState.Building);

        var allNodes = new Dictionary<string, StructuralNode>();
        var allFiles = new Dictionary<string, FileRecord>();
        var allDiagnostics = new Dictionary<string, IndexDiagnostic>();
        var pathToFileId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fileIdToTopNodeIds = new Dictionary<string, IReadOnlyList<string>>();
        var languageBreakdown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var adapterVersions = new Dictionary<string, string>();
        var warnings = new List<string>();
        var allImportsByFileId = new Dictionary<string, IReadOnlyList<ImportEntry>>();
        var allHintsByFileId = new Dictionary<string, IReadOnlyList<ReferenceHint>>();

        int filesScanned = 0, filesEligible = 0, filesIndexed = 0, filesSkipped = 0, filesFailed = 0;
        int counter = 0;

        try
        {
            // 1. Enumerate files
            var fileInfos = await _source.EnumerateFilesAsync(rootPath, _eligibility, ct);
            filesScanned = fileInfos.Count;

            foreach (var fileInfo in fileInfos)
            {
                ct.ThrowIfCancellationRequested();
                filesEligible++;

                var relativePath = GetRelativePath(rootPath, fileInfo.FilePath);
                var fileId = $"file-{++counter}";

                // Detect language
                string? contentHint = null;
                var langResult = _languageDetector.Detect(fileInfo.FilePath, contentHint);

                if (!langResult.IsKnown || !_adapters.ContainsKey(langResult.LanguageId))
                {
                    // Unknown or unsupported language
                    filesSkipped++;
                    allFiles[fileId] = new FileRecord
                    {
                        FileId = fileId,
                        Path = relativePath,
                        LanguageId = langResult.LanguageId,
                        ContentHash = "",
                        SizeBytes = fileInfo.SizeBytes,
                        EligibilityStatus = EligibilityStatus.Eligible,
                        ParseStatus = ParseStatus.Skipped,
                    };
                    pathToFileId[relativePath.ToLowerInvariant()] = fileId;
                    fileIdToTopNodeIds[fileId] = [];
                    continue;
                }

                // Read content
                SourceFileContent content;
                try
                {
                    content = await _source.ReadFileAsync(fileInfo.FilePath, ct);
                }
                catch (Exception ex)
                {
                    filesFailed++;
                    var diagId = $"diag-{fileId}-read";
                    allDiagnostics[diagId] = new IndexDiagnostic
                    {
                        DiagnosticId = diagId,
                        Severity = DiagnosticSeverity.Error,
                        Source = DiagnosticSource.Indexing,
                        Code = "FILE_READ_ERROR",
                        Message = $"Could not read file: {ex.Message}",
                        FilePath = relativePath,
                    };
                    allFiles[fileId] = new FileRecord
                    {
                        FileId = fileId,
                        Path = relativePath,
                        LanguageId = langResult.LanguageId,
                        ContentHash = "",
                        SizeBytes = fileInfo.SizeBytes,
                        ParseStatus = ParseStatus.Failed,
                        DiagnosticIds = [diagId],
                    };
                    pathToFileId[relativePath.ToLowerInvariant()] = fileId;
                    fileIdToTopNodeIds[fileId] = [];
                    continue;
                }

                // Parse
                var adapter = _adapters[langResult.LanguageId];
                var parseRequest = new ParseRequest
                {
                    FileId = fileId,
                    FilePath = relativePath,
                    Content = content.Content,
                    LanguageId = langResult.LanguageId,
                    Mode = request.ParseMode,
                    MaxFileSizeBytes = request.MaxFileSizeBytes ?? _eligibility.MaxFileSizeBytes,
                };

                ParseResult parseResult;
                try
                {
                    parseResult = await adapter.ParseAsync(parseRequest, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    filesFailed++;
                    var diagId = $"diag-{fileId}-parse";
                    allDiagnostics[diagId] = new IndexDiagnostic
                    {
                        DiagnosticId = diagId,
                        Severity = DiagnosticSeverity.Error,
                        Source = DiagnosticSource.ParserAdapter,
                        Code = "ADAPTER_EXCEPTION",
                        Message = ex.Message,
                        FilePath = relativePath,
                    };
                    allFiles[fileId] = new FileRecord
                    {
                        FileId = fileId,
                        Path = relativePath,
                        LanguageId = langResult.LanguageId,
                        ContentHash = content.ContentHash,
                        SizeBytes = fileInfo.SizeBytes,
                        Encoding = content.Encoding,
                        ParseStatus = ParseStatus.Failed,
                        DiagnosticIds = [diagId],
                    };
                    pathToFileId[relativePath.ToLowerInvariant()] = fileId;
                    fileIdToTopNodeIds[fileId] = [];
                    continue;
                }

                // Collect nodes
                var topNodeIds = new List<string>();
                foreach (var node in parseResult.Nodes)
                {
                    allNodes[node.NodeId] = node;
                    if (node.ParentNodeId is null)
                        topNodeIds.Add(node.NodeId);
                }

                // Collect diagnostics
                var diagIds = new List<string>();
                foreach (var diag in parseResult.Diagnostics)
                {
                    allDiagnostics[diag.DiagnosticId] = diag;
                    diagIds.Add(diag.DiagnosticId);
                }

                // Collect imports and reference hints (Phase 3)
                if (parseResult.Imports.Count > 0)
                    allImportsByFileId[fileId] = parseResult.Imports;
                if (parseResult.ReferenceHints.Count > 0)
                    allHintsByFileId[fileId] = parseResult.ReferenceHints;

                var isPartial = parseResult.Status == ParseStatus.Partial;
                if (parseResult.Status == ParseStatus.Failed) filesFailed++;
                else filesIndexed++;

                allFiles[fileId] = new FileRecord
                {
                    FileId = fileId,
                    Path = relativePath,
                    LanguageId = langResult.LanguageId,
                    ContentHash = content.ContentHash,
                    SizeBytes = fileInfo.SizeBytes,
                    Encoding = content.Encoding,
                    EligibilityStatus = EligibilityStatus.Eligible,
                    ParseStatus = parseResult.Status,
                    TopLevelNodeIds = topNodeIds,
                    DiagnosticIds = diagIds,
                };

                pathToFileId[relativePath.ToLowerInvariant()] = fileId;
                fileIdToTopNodeIds[fileId] = topNodeIds;

                languageBreakdown.TryGetValue(langResult.LanguageId, out int langCount);
                languageBreakdown[langResult.LanguageId] = langCount + 1;

                adapterVersions[langResult.LanguageId] = parseResult.AdapterVersion;

                // Report progress
                request.ProgressCallback?.Invoke(new IndexProgress
                {
                    TotalFiles = filesEligible,
                    ProcessedFiles = filesIndexed + filesFailed + filesSkipped,
                    SkippedFiles = filesSkipped,
                    FailedFiles = filesFailed,
                    CurrentFile = relativePath,
                    ElapsedMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                });
            }

            var completedAt = DateTimeOffset.UtcNow;
            var snapshotId = GenerateSnapshotId(rootPath, completedAt);

            // --- Phase 2: Symbol projection ---
            var repoScope = SymbolIdBuilder.DeriveRepoScope(rootPath);
            var allSymbols = new Dictionary<string, LogicalSymbol>();
            var allOccurrences = new Dictionary<string, SymbolOccurrence>();

            foreach (var (fileId, file) in allFiles)
            {
                if (!_projectors.TryGetValue(file.LanguageId, out var projector)) continue;

                // Build a per-file node map for the projector
                var fileNodeMap = new Dictionary<string, StructuralNode>();
                foreach (var nodeId in file.TopLevelNodeIds)
                    CollectNodeSubtree(nodeId, allNodes, fileNodeMap);

                var projectionResult = projector.Project(fileId, repoScope, fileNodeMap);

                foreach (var sym in projectionResult.Symbols)
                    allSymbols[sym.SymbolId] = sym;
                foreach (var occ in projectionResult.Occurrences)
                    allOccurrences[occ.OccurrenceId] = occ;
            }

            // Build symbol reverse indexes
            var symbolsByName = BuildNameIndex(allSymbols.Values);
            var symbolsByQualifiedName = BuildQualifiedNameIndex(allSymbols.Values);
            var symbolsByFileId = BuildFileIndex(allSymbols.Values);
            var occurrencesBySymbolId = BuildOccurrenceIndex(allOccurrences.Values);
            var childSymbolsByParentId = BuildChildIndex(allSymbols.Values);

            // --- Phase 3: Reference resolution ---
            var allReferences = new Dictionary<string, ReferenceEdge>();
            int edgeCounter = 0;

            foreach (var (fid, hints) in allHintsByFileId)
            {
                if (!allFiles.TryGetValue(fid, out var file)) continue;

                foreach (var hint in hints)
                {
                    // Resolve source symbol by qualified name
                    string? sourceSymId = symbolsByQualifiedName.TryGetValue(hint.SourceQualifiedPath, out var srcId)
                        ? srcId : null;

                    // Resolve target symbol
                    string? targetSymId = null;
                    var resClass = ResolutionClass.Unknown;
                    double confidence = sourceSymId != null ? 0.85 : 0.6;

                    if (symbolsByQualifiedName.TryGetValue(hint.TargetName, out var exactId))
                    {
                        targetSymId = exactId;
                        resClass = ResolutionClass.ExactBound;
                        confidence = 1.0;
                    }
                    else if (symbolsByName.TryGetValue(hint.TargetName, out var candidates))
                    {
                        if (candidates.Count == 1)
                        {
                            targetSymId = candidates[0];
                            resClass = ResolutionClass.ScopedBound;
                            confidence = 0.9;
                        }
                        else
                        {
                            resClass = ResolutionClass.Ambiguous;
                            confidence = 0.7;
                        }
                    }
                    else if (IsLikelyExternalType(hint.TargetName))
                    {
                        resClass = ResolutionClass.External;
                        confidence = 0.8;
                    }

                    var edgeId = $"ref-{fid}-{++edgeCounter}";
                    allReferences[edgeId] = new ReferenceEdge
                    {
                        EdgeId = edgeId,
                        SourceSymbolId = sourceSymId,
                        TargetSymbolId = targetSymId,
                        RelationshipKind = hint.Kind,
                        ResolutionClass = resClass,
                        Evidence = hint.Evidence ?? hint.TargetName,
                        EvidenceSpan = hint.EvidenceSpan,
                        LanguageId = file.LanguageId,
                        ExtractionMode = ExtractionMode.CompilerSyntax,
                        Confidence = confidence,
                    };
                }
            }

            // Build reference reverse indexes
            var referencesBySourceSymbolId = BuildReferencesBySourceIndex(allReferences.Values);
            var referencesByFileId = BuildReferencesByFileIdIndex(allReferences, allFiles, allSymbols);

            var metadata = new SnapshotMetadata
            {
                SnapshotId = snapshotId,
                CreatedAt = completedAt,
                RootPath = rootPath,
                IndexMode = Model.Snapshots.IndexMode.Full,
                FileCount = allFiles.Count,
                StructuralNodeCount = allNodes.Count,
                SymbolCount = allSymbols.Count,
                OccurrenceCount = allOccurrences.Count,
                ReferenceCount = allReferences.Count,
                ImportCount = allImportsByFileId.Values.Sum(list => list.Count),
                DiagnosticCount = allDiagnostics.Count,
                LanguageBreakdown = languageBreakdown,
                AdapterVersions = adapterVersions,
            };

            var snapshot = new IndexSnapshot
            {
                Metadata = metadata,
                Files = allFiles,
                Nodes = allNodes,
                Symbols = allSymbols,
                Occurrences = allOccurrences,
                References = allReferences,
                ImportsByFileId = allImportsByFileId,
                ReferencesBySourceSymbolId = referencesBySourceSymbolId,
                ReferencesByFileId = referencesByFileId,
                Diagnostics = allDiagnostics,
                PathToFileId = pathToFileId,
                FileIdToTopNodeIds = fileIdToTopNodeIds,
                SymbolsByName = symbolsByName,
                SymbolsByQualifiedName = symbolsByQualifiedName,
                SymbolsByFileId = symbolsByFileId,
                OccurrencesBySymbolId = occurrencesBySymbolId,
                ChildSymbolsByParentId = childSymbolsByParentId,
            };

            if (request.PersistSnapshot)
                await _store.SaveSnapshotAsync(snapshot, ct);

            SetState(rootPath, IndexState.Ready, snapshotId, completedAt);

            var overallStatus = filesFailed > 0 && filesIndexed == 0 ? "failed"
                : filesFailed > 0 ? "partial"
                : "success";

            return new IndexBuildResult
            {
                SnapshotId = snapshotId,
                RootPath = rootPath,
                StartedAt = startedAt,
                CompletedAt = completedAt,
                DurationMs = (long)(completedAt - startedAt).TotalMilliseconds,
                ParseMode = request.ParseMode,
                FilesScanned = filesScanned,
                FilesEligible = filesEligible,
                FilesIndexed = filesIndexed,
                FilesSkipped = filesSkipped,
                FilesFailed = filesFailed,
                StructuralNodeCount = allNodes.Count,
                SymbolCount = allSymbols.Count,
                OccurrenceCount = allOccurrences.Count,
                ReferenceCount = allReferences.Count,
                DiagnosticCount = allDiagnostics.Count,
                LanguageBreakdown = languageBreakdown,
                Status = overallStatus,
                Warnings = warnings,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetState(rootPath, IndexState.Failed, errorMessage: ex.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task<IndexStatus> GetStatusAsync(string rootPath, CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            _states.TryGetValue(rootPath, out var state);
            _latestSnapshotIds.TryGetValue(rootPath, out var snapshotId);
            _lastErrors.TryGetValue(rootPath, out var lastError);
            _lastCompletedAt.TryGetValue(rootPath, out var lastCompleted);

            return Task.FromResult(new IndexStatus
            {
                RootPath = rootPath,
                State = state,
                LatestSnapshotId = snapshotId,
                LastCompletedAt = lastCompleted == default ? null : lastCompleted,
                LastError = lastError,
            });
        }
    }

    private void SetState(string rootPath, IndexState state,
        string? snapshotId = null, DateTimeOffset completedAt = default, string? errorMessage = null)
    {
        lock (_stateLock)
        {
            _states[rootPath] = state;
            if (snapshotId != null) _latestSnapshotIds[rootPath] = snapshotId;
            if (completedAt != default) _lastCompletedAt[rootPath] = completedAt;
            if (errorMessage != null) _lastErrors[rootPath] = errorMessage;
        }
    }

    private static string GetRelativePath(string rootPath, string absolutePath)
    {
        var root = rootPath.Replace('\\', '/').TrimEnd('/') + "/";
        var abs = absolutePath.Replace('\\', '/');
        return abs.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? abs[root.Length..]
            : abs;
    }

    private static string GenerateSnapshotId(string rootPath, DateTimeOffset at)
    {
        var rootHash = Math.Abs(rootPath.GetHashCode()).ToString("x8");
        return $"snap-{rootHash}-{at:yyyyMMddHHmmss}-1";
    }

    /// <summary>
    /// Recursively collects a node and all its descendants into <paramref name="result"/>.
    /// </summary>
    private static void CollectNodeSubtree(
        string nodeId,
        IReadOnlyDictionary<string, StructuralNode> allNodes,
        Dictionary<string, StructuralNode> result)
    {
        if (!allNodes.TryGetValue(nodeId, out var node)) return;
        result[nodeId] = node;
        foreach (var childId in node.Children)
            CollectNodeSubtree(childId, allNodes, result);
    }

    /// <summary>Builds a name → [symbolIds] index from all symbols.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildNameIndex(
        IEnumerable<LogicalSymbol> symbols)
    {
        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sym in symbols)
        {
            if (!dict.TryGetValue(sym.Name, out var list))
                dict[sym.Name] = list = new List<string>();
            list.Add(sym.SymbolId);
        }
        return dict.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Builds a qualifiedName → symbolId index from all symbols.</summary>
    private static IReadOnlyDictionary<string, string> BuildQualifiedNameIndex(
        IEnumerable<LogicalSymbol> symbols)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sym in symbols)
            dict[sym.QualifiedName] = sym.SymbolId;
        return dict;
    }

    /// <summary>Builds a fileId → [symbolIds] index from all symbols.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildFileIndex(
        IEnumerable<LogicalSymbol> symbols)
    {
        var dict = new Dictionary<string, List<string>>();
        foreach (var sym in symbols)
        {
            if (!dict.TryGetValue(sym.PrimaryFileId, out var list))
                dict[sym.PrimaryFileId] = list = new List<string>();
            list.Add(sym.SymbolId);
        }
        return dict.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value);
    }

    /// <summary>Builds a symbolId → [occurrenceIds] index from all occurrences.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildOccurrenceIndex(
        IEnumerable<SymbolOccurrence> occurrences)
    {
        var dict = new Dictionary<string, List<string>>();
        foreach (var occ in occurrences)
        {
            if (!dict.TryGetValue(occ.SymbolId, out var list))
                dict[occ.SymbolId] = list = new List<string>();
            list.Add(occ.OccurrenceId);
        }
        return dict.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value);
    }

    /// <summary>Builds a parentSymbolId → [childSymbolIds] index from all symbols.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildChildIndex(
        IEnumerable<LogicalSymbol> symbols)
    {
        var dict = new Dictionary<string, List<string>>();
        foreach (var sym in symbols)
        {
            if (sym.ParentSymbolId is null) continue;
            if (!dict.TryGetValue(sym.ParentSymbolId, out var list))
                dict[sym.ParentSymbolId] = list = new List<string>();
            list.Add(sym.SymbolId);
        }
        return dict.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value);
    }

    /// <summary>Builds a sourceSymbolId → [edgeIds] reverse index from reference edges.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildReferencesBySourceIndex(
        IEnumerable<ReferenceEdge> edges)
    {
        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            if (edge.SourceSymbolId is null) continue;
            if (!dict.TryGetValue(edge.SourceSymbolId, out var list))
                dict[edge.SourceSymbolId] = list = [];
            list.Add(edge.EdgeId);
        }
        return dict.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Builds a fileId → [edgeIds] reverse index by resolving source symbol to its primary file.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildReferencesByFileIdIndex(
        IReadOnlyDictionary<string, ReferenceEdge> edges,
        IReadOnlyDictionary<string, FileRecord> files,
        IReadOnlyDictionary<string, LogicalSymbol> symbols)
    {
        var dict = new Dictionary<string, List<string>>();
        foreach (var (edgeId, edge) in edges)
        {
            string? fileId = null;
            if (edge.SourceSymbolId != null && symbols.TryGetValue(edge.SourceSymbolId, out var sym))
                fileId = sym.PrimaryFileId;

            if (fileId == null) continue;
            if (!dict.TryGetValue(fileId, out var list))
                dict[fileId] = list = [];
            list.Add(edgeId);
        }
        return dict.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }

    /// <summary>
    /// Returns true if a type name is likely from an external library (BCL or well-known packages).
    /// </summary>
    private static bool IsLikelyExternalType(string typeName) =>
        s_externalTypeNames.Contains(typeName);

    private static readonly HashSet<string> s_externalTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "string", "int", "long", "bool", "double", "float", "decimal", "byte",
        "object", "void", "char", "short", "uint", "ulong", "sbyte", "ushort",
        "IEnumerable", "IList", "ICollection", "IDictionary", "IReadOnlyList",
        "IReadOnlyDictionary", "IReadOnlyCollection", "IComparable", "IEquatable",
        "IDisposable", "ICloneable", "Nullable", "List", "Dictionary", "HashSet",
        "Queue", "Stack", "SortedList", "SortedDictionary", "LinkedList",
        "Array", "Exception", "ArgumentException", "ArgumentNullException",
        "InvalidOperationException", "NotImplementedException", "NotSupportedException",
        "Task", "ValueTask", "CancellationToken", "Stream", "TextWriter", "TextReader",
        "StringBuilder", "Guid", "DateTime", "DateTimeOffset", "TimeSpan",
        "Console", "Math", "String", "Int32", "Boolean", "Object",
        "Action", "Func", "Predicate", "EventHandler", "EventArgs",
        // Python builtins
        "list", "dict", "tuple", "set", "frozenset", "str", "bytes",
        "bytearray", "memoryview", "complex", "type",
    };
}

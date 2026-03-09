// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.CodeIndexing.Eligibility;
using StewardessMCPService.CodeIndexing.LanguageDetection;
using StewardessMCPService.CodeIndexing.Model.Diagnostics;
using StewardessMCPService.CodeIndexing.Model.References;
using StewardessMCPService.CodeIndexing.Model.Semantic;
using StewardessMCPService.CodeIndexing.Model.Snapshots;
using StewardessMCPService.CodeIndexing.Model.Structural;
using StewardessMCPService.CodeIndexing.Parsers.Abstractions;
using StewardessMCPService.CodeIndexing.Projection;
using StewardessMCPService.CodeIndexing.Snapshots;
using StewardessMCPService.CodeIndexing.Source;

namespace StewardessMCPService.CodeIndexing.Indexing;

/// <summary>
///     Orchestrates the indexing pipeline: enumerate → detect → parse → build snapshot.
/// </summary>
public sealed class IndexingEngine : IIndexingEngine
{
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
        "bytearray", "memoryview", "complex", "type"
    };

    private readonly IReadOnlyDictionary<string, IParserAdapter> _adapters;
    private readonly IEligibilityPolicy _eligibility;
    private readonly ILanguageDetector _languageDetector;
    private readonly Dictionary<string, DateTimeOffset> _lastCompletedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _lastErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SnapshotDelta?> _latestDeltas = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _latestFileCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _latestReferenceCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _latestSnapshotIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _latestSymbolCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, ISymbolProjector> _projectors;
    private readonly ISourceProvider _source;
    private readonly object _stateLock = new();

    // Per-root state tracking
    private readonly Dictionary<string, IndexState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISnapshotStore _store;

    /// <summary>
    ///     Initializes a new instance of <see cref="IndexingEngine" />.
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

    /// <inheritdoc />
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
        var counter = 0;

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
                        ParseStatus = ParseStatus.Skipped
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
                        FilePath = relativePath
                    };
                    allFiles[fileId] = new FileRecord
                    {
                        FileId = fileId,
                        Path = relativePath,
                        LanguageId = langResult.LanguageId,
                        ContentHash = "",
                        SizeBytes = fileInfo.SizeBytes,
                        ParseStatus = ParseStatus.Failed,
                        DiagnosticIds = [diagId]
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
                    MaxFileSizeBytes = request.MaxFileSizeBytes ?? _eligibility.MaxFileSizeBytes
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
                        FilePath = relativePath
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
                        DiagnosticIds = [diagId]
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
                    DiagnosticIds = diagIds
                };

                pathToFileId[relativePath.ToLowerInvariant()] = fileId;
                fileIdToTopNodeIds[fileId] = topNodeIds;

                languageBreakdown.TryGetValue(langResult.LanguageId, out var langCount);
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
                    ElapsedMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds
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
            var edgeCounter = 0;

            foreach (var (fid, hints) in allHintsByFileId)
            {
                if (!allFiles.TryGetValue(fid, out var file)) continue;

                foreach (var hint in hints)
                {
                    // Resolve source symbol by qualified name
                    var sourceSymId = symbolsByQualifiedName.TryGetValue(hint.SourceQualifiedPath, out var srcId)
                        ? srcId
                        : null;

                    // Resolve target symbol
                    string? targetSymId = null;
                    var resClass = ResolutionClass.Unknown;
                    var confidence = sourceSymId != null ? 0.85 : 0.6;

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
                            // Ambiguous by simple name — try import-based disambiguation
                            var importId = TryResolveViaImports(hint.TargetName, fid, allImportsByFileId,
                                symbolsByQualifiedName);
                            if (importId is not null)
                            {
                                targetSymId = importId;
                                resClass = ResolutionClass.ImportBound;
                                confidence = 0.95;
                            }
                            else
                            {
                                resClass = ResolutionClass.Ambiguous;
                                confidence = 0.7;
                            }
                        }
                    }
                    else
                    {
                        // Not found by simple name — try import-based resolution
                        var importId = TryResolveViaImports(hint.TargetName, fid, allImportsByFileId,
                            symbolsByQualifiedName);
                        if (importId is not null)
                        {
                            targetSymId = importId;
                            resClass = ResolutionClass.ImportBound;
                            confidence = 0.95;
                        }
                        else if (IsLikelyExternalType(hint.TargetName))
                        {
                            resClass = ResolutionClass.External;
                            confidence = 0.8;
                        }
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
                        Confidence = confidence
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
                IndexMode = IndexMode.Full,
                FileCount = allFiles.Count,
                StructuralNodeCount = allNodes.Count,
                SymbolCount = allSymbols.Count,
                OccurrenceCount = allOccurrences.Count,
                ReferenceCount = allReferences.Count,
                ImportCount = allImportsByFileId.Values.Sum(list => list.Count),
                DiagnosticCount = allDiagnostics.Count,
                LanguageBreakdown = languageBreakdown,
                AdapterVersions = adapterVersions
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
                HintsByFileId = allHintsByFileId,
                ReferencesBySourceSymbolId = referencesBySourceSymbolId,
                ReferencesByFileId = referencesByFileId,
                Diagnostics = allDiagnostics,
                PathToFileId = pathToFileId,
                FileIdToTopNodeIds = fileIdToTopNodeIds,
                SymbolsByName = symbolsByName,
                SymbolsByQualifiedName = symbolsByQualifiedName,
                SymbolsByFileId = symbolsByFileId,
                OccurrencesBySymbolId = occurrencesBySymbolId,
                ChildSymbolsByParentId = childSymbolsByParentId
            };

            if (request.PersistSnapshot)
                await _store.SaveSnapshotAsync(snapshot, ct);

            SetState(rootPath, IndexState.Ready, snapshotId, completedAt,
                fileCount: allFiles.Count, symbolCount: allSymbols.Count, referenceCount: allReferences.Count);

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
                Warnings = warnings
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetState(rootPath, IndexState.Failed, errorMessage: ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<IndexStatus> GetStatusAsync(string rootPath, CancellationToken ct = default)
    {
        var key = NormalizeRootPath(rootPath);
        lock (_stateLock)
        {
            _states.TryGetValue(key, out var state);
            _latestSnapshotIds.TryGetValue(key, out var snapshotId);
            _lastErrors.TryGetValue(key, out var lastError);
            _lastCompletedAt.TryGetValue(key, out var lastCompleted);
            _latestFileCounts.TryGetValue(key, out var fileCount);
            _latestSymbolCounts.TryGetValue(key, out var symbolCount);
            _latestReferenceCounts.TryGetValue(key, out var referenceCount);
            _latestDeltas.TryGetValue(key, out var delta);

            return Task.FromResult(new IndexStatus
            {
                RootPath = rootPath,
                State = state,
                LatestSnapshotId = snapshotId,
                LastCompletedAt = lastCompleted == default ? null : lastCompleted,
                LastError = lastError,
                FileCount = fileCount,
                SymbolCount = symbolCount,
                ReferenceCount = referenceCount,
                LastDelta = delta
            });
        }
    }

    /// <inheritdoc />
    public async Task<IndexUpdateResult> UpdateAsync(IndexUpdateRequest request, CancellationToken ct = default)
    {
        var rootPath = request.RootPath;
        var startedAt = DateTimeOffset.UtcNow;

        // If no previous snapshot exists, fall back to a full build
        var previousSnapshot = await _store.GetLatestSnapshotAsync(rootPath, ct).ConfigureAwait(false);
        if (previousSnapshot is null)
        {
            SetState(rootPath, IndexState.Building);
            try
            {
                var buildResult = await BuildAsync(new IndexBuildRequest
                {
                    RootPath = rootPath,
                    PersistSnapshot = true,
                    ProgressCallback = request.ProgressCallback
                }, ct).ConfigureAwait(false);

                var completedAt = DateTimeOffset.UtcNow;
                return new IndexUpdateResult
                {
                    SnapshotId = buildResult.SnapshotId,
                    PreviousSnapshotId = null,
                    RootPath = rootPath,
                    StartedAt = startedAt,
                    CompletedAt = completedAt,
                    DurationMs = (long)(completedAt - startedAt).TotalMilliseconds,
                    FilesAdded = buildResult.FilesIndexed,
                    FilesModified = 0,
                    FilesDeleted = 0,
                    FilesUnchanged = 0
                };
            }
            catch (Exception ex)
            {
                SetState(rootPath, IndexState.Failed, errorMessage: ex.Message);
                return new IndexUpdateResult
                {
                    SnapshotId = "(none)",
                    RootPath = rootPath,
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    DurationMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                    Error = ex.Message
                };
            }
        }

        SetState(rootPath, IndexState.Updating);

        try
        {
            var prevSnapshotId = previousSnapshot.Metadata.SnapshotId;

            // ── Step 1: Build old file map (normalizedPath → FileRecord) ─────────────
            var oldFileMap = previousSnapshot.Files.Values
                .ToDictionary(f => f.Path.ToLowerInvariant(), f => f, StringComparer.OrdinalIgnoreCase);

            // ── Step 2: Enumerate current files ──────────────────────────────────────
            var currentFiles = await _source.EnumerateFilesAsync(rootPath, _eligibility, ct).ConfigureAwait(false);

            // ── Step 3: Classify files ────────────────────────────────────────────────
            var addedFiles = new List<SourceFileInfo>();
            var modifiedFiles = new List<SourceFileInfo>();
            var unchangedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentNormalizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var fileInfo in currentFiles)
            {
                var relativePath = GetRelativePath(rootPath, fileInfo.FilePath);
                var normalizedPath = relativePath.ToLowerInvariant();
                currentNormalizedPaths.Add(normalizedPath);

                if (!oldFileMap.TryGetValue(normalizedPath, out var oldRecord))
                {
                    addedFiles.Add(fileInfo);
                }
                else
                {
                    SourceFileContent content;
                    try
                    {
                        content = await _source.ReadFileAsync(fileInfo.FilePath, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        addedFiles.Add(fileInfo);
                        continue;
                    }

                    if (!string.Equals(content.ContentHash, oldRecord.ContentHash, StringComparison.OrdinalIgnoreCase))
                        modifiedFiles.Add(fileInfo);
                    else
                        unchangedPaths.Add(normalizedPath);
                }
            }

            var deletedPaths = oldFileMap.Keys
                .Where(p => !currentNormalizedPaths.Contains(p))
                .ToList();

            // ── Step 4: Copy data for unchanged files from old snapshot ───────────────
            var allNodes = new Dictionary<string, StructuralNode>();
            var allFiles = new Dictionary<string, FileRecord>();
            var allDiagnostics = new Dictionary<string, IndexDiagnostic>();
            var pathToFileId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fileIdToTopNodeIds = new Dictionary<string, IReadOnlyList<string>>();
            var allImportsByFileId = new Dictionary<string, IReadOnlyList<ImportEntry>>();
            var allHintsByFileId = new Dictionary<string, IReadOnlyList<ReferenceHint>>();
            var allSymbols = new Dictionary<string, LogicalSymbol>();
            var allOccurrences = new Dictionary<string, SymbolOccurrence>();
            var languageBreakdown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var adapterVersions = new Dictionary<string, string>();

            var unchangedCount = 0;
            foreach (var normalizedPath in unchangedPaths)
            {
                var oldRecord = oldFileMap[normalizedPath];
                var oldFileId = oldRecord.FileId;

                allFiles[oldFileId] = oldRecord;
                pathToFileId[normalizedPath] = oldFileId;

                if (previousSnapshot.FileIdToTopNodeIds.TryGetValue(oldFileId, out var topNodeIds))
                {
                    fileIdToTopNodeIds[oldFileId] = topNodeIds;
                    CopyNodesRecursive(oldFileId, previousSnapshot, allNodes);
                }
                else
                {
                    fileIdToTopNodeIds[oldFileId] = [];
                }

                if (previousSnapshot.ImportsByFileId.TryGetValue(oldFileId, out var imports))
                    allImportsByFileId[oldFileId] = imports;

                if (previousSnapshot.HintsByFileId.TryGetValue(oldFileId, out var hints))
                    allHintsByFileId[oldFileId] = hints;

                if (previousSnapshot.SymbolsByFileId.TryGetValue(oldFileId, out var symIds))
                    foreach (var symId in symIds)
                    {
                        if (!previousSnapshot.Symbols.TryGetValue(symId, out var sym)) continue;
                        allSymbols[symId] = sym;

                        if (previousSnapshot.OccurrencesBySymbolId.TryGetValue(symId, out var occIds))
                            foreach (var occId in occIds)
                                if (previousSnapshot.Occurrences.TryGetValue(occId, out var occ))
                                    allOccurrences[occId] = occ;
                    }

                if (!languageBreakdown.ContainsKey(oldRecord.LanguageId))
                    languageBreakdown[oldRecord.LanguageId] = 0;
                languageBreakdown[oldRecord.LanguageId]++;

                unchangedCount++;
            }

            // ── Step 5: Re-parse added and modified files ─────────────────────────────
            var counter = allFiles.Count;
            var modifiedNormalizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in modifiedFiles)
            {
                var rp = GetRelativePath(rootPath, f.FilePath).ToLowerInvariant();
                modifiedNormalizedPaths.Add(rp);
            }

            var repoScope = SymbolIdBuilder.DeriveRepoScope(rootPath);
            var filesToReparse = addedFiles.Concat(modifiedFiles).ToList();

            foreach (var fileInfo in filesToReparse)
            {
                ct.ThrowIfCancellationRequested();

                var relativePath = GetRelativePath(rootPath, fileInfo.FilePath);
                var normalizedPath = relativePath.ToLowerInvariant();

                string fileId;
                if (modifiedNormalizedPaths.Contains(normalizedPath) &&
                    oldFileMap.TryGetValue(normalizedPath, out var oldRec))
                    fileId = oldRec.FileId;
                else
                    fileId = $"file-{++counter}";

                var langResult = _languageDetector.Detect(fileInfo.FilePath);

                if (!langResult.IsKnown || !_adapters.ContainsKey(langResult.LanguageId))
                {
                    allFiles[fileId] = new FileRecord
                    {
                        FileId = fileId,
                        Path = relativePath,
                        LanguageId = langResult.IsKnown ? langResult.LanguageId : "unknown",
                        ContentHash = "",
                        SizeBytes = fileInfo.SizeBytes,
                        EligibilityStatus = EligibilityStatus.Eligible,
                        ParseStatus = ParseStatus.Skipped
                    };
                    pathToFileId[normalizedPath] = fileId;
                    fileIdToTopNodeIds[fileId] = [];
                    continue;
                }

                SourceFileContent content;
                try
                {
                    content = await _source.ReadFileAsync(fileInfo.FilePath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var diagId = $"diag-{fileId}-read";
                    allDiagnostics[diagId] = new IndexDiagnostic
                    {
                        DiagnosticId = diagId,
                        Severity = DiagnosticSeverity.Error,
                        Source = DiagnosticSource.Indexing,
                        Code = "FILE_READ_ERROR",
                        Message = $"Could not read file: {ex.Message}",
                        FilePath = relativePath
                    };
                    allFiles[fileId] = new FileRecord
                    {
                        FileId = fileId,
                        Path = relativePath,
                        LanguageId = langResult.LanguageId,
                        ContentHash = "",
                        SizeBytes = fileInfo.SizeBytes,
                        ParseStatus = ParseStatus.Failed,
                        DiagnosticIds = [diagId]
                    };
                    pathToFileId[normalizedPath] = fileId;
                    fileIdToTopNodeIds[fileId] = [];
                    continue;
                }

                var adapter = _adapters[langResult.LanguageId];
                var parseRequest = new ParseRequest
                {
                    FileId = fileId,
                    FilePath = relativePath,
                    Content = content.Content,
                    LanguageId = langResult.LanguageId,
                    Mode = ParseMode.Declarations,
                    MaxFileSizeBytes = _eligibility.MaxFileSizeBytes
                };

                ParseResult parseResult;
                try
                {
                    parseResult = await adapter.ParseAsync(parseRequest, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var diagId = $"diag-{fileId}-parse";
                    allDiagnostics[diagId] = new IndexDiagnostic
                    {
                        DiagnosticId = diagId,
                        Severity = DiagnosticSeverity.Error,
                        Source = DiagnosticSource.Indexing,
                        Code = "PARSE_ERROR",
                        Message = $"Parse failed: {ex.Message}",
                        FilePath = relativePath
                    };
                    allFiles[fileId] = new FileRecord
                    {
                        FileId = fileId,
                        Path = relativePath,
                        LanguageId = langResult.LanguageId,
                        ContentHash = content.ContentHash,
                        SizeBytes = fileInfo.SizeBytes,
                        ParseStatus = ParseStatus.Failed,
                        DiagnosticIds = [diagId]
                    };
                    pathToFileId[normalizedPath] = fileId;
                    fileIdToTopNodeIds[fileId] = [];
                    continue;
                }

                foreach (var node in parseResult.Nodes)
                    allNodes[node.NodeId] = node;
                foreach (var diag in parseResult.Diagnostics)
                    allDiagnostics[diag.DiagnosticId] = diag;

                var diagIds = parseResult.Diagnostics.Select(d => d.DiagnosticId).ToList();
                var topNodeIds2 = parseResult.Nodes
                    .Where(n => n.ParentNodeId is null)
                    .Select(n => n.NodeId)
                    .ToList();

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
                    TopLevelNodeIds = topNodeIds2,
                    DiagnosticIds = diagIds
                };
                pathToFileId[normalizedPath] = fileId;
                fileIdToTopNodeIds[fileId] = topNodeIds2;

                if (parseResult.Imports.Count > 0)
                    allImportsByFileId[fileId] = parseResult.Imports;
                if (parseResult.ReferenceHints.Count > 0)
                    allHintsByFileId[fileId] = parseResult.ReferenceHints;

                if (!adapterVersions.ContainsKey(langResult.LanguageId))
                    adapterVersions[langResult.LanguageId] = parseResult.AdapterVersion;
                if (!languageBreakdown.ContainsKey(langResult.LanguageId))
                    languageBreakdown[langResult.LanguageId] = 0;
                languageBreakdown[langResult.LanguageId]++;

                if (_projectors.TryGetValue(langResult.LanguageId, out var projector))
                {
                    var fileNodeMap = new Dictionary<string, StructuralNode>();
                    foreach (var nodeId in topNodeIds2)
                        CollectNodeSubtree(nodeId, allNodes, fileNodeMap);
                    var projResult = projector.Project(fileId, repoScope, fileNodeMap);
                    foreach (var sym in projResult.Symbols)
                        allSymbols[sym.SymbolId] = sym;
                    foreach (var occ in projResult.Occurrences)
                        allOccurrences[occ.OccurrenceId] = occ;
                }
            }

            // ── Step 6: Re-run reference resolution across all files ──────────────────
            var symbolsByName = BuildNameIndex(allSymbols.Values);
            var symbolsByQualifiedName = BuildQualifiedNameIndex(allSymbols.Values);
            var symbolsByFileId = BuildFileIndex(allSymbols.Values);
            var occurrencesBySymbolId = BuildOccurrenceIndex(allOccurrences.Values);
            var childSymbolsByParentId = BuildChildIndex(allSymbols.Values);

            var allReferences = new Dictionary<string, ReferenceEdge>();
            var edgeCounter = 0;

            foreach (var (fid, hints) in allHintsByFileId)
            {
                if (!allFiles.TryGetValue(fid, out var file)) continue;

                foreach (var hint in hints)
                {
                    var sourceSymId = symbolsByQualifiedName.TryGetValue(hint.SourceQualifiedPath, out var srcId)
                        ? srcId
                        : null;

                    string? targetSymId = null;
                    var resClass = ResolutionClass.Unknown;
                    var confidence = sourceSymId != null ? 0.85 : 0.6;

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
                            var importId = TryResolveViaImports(hint.TargetName, fid, allImportsByFileId,
                                symbolsByQualifiedName);
                            if (importId is not null)
                            {
                                targetSymId = importId;
                                resClass = ResolutionClass.ImportBound;
                                confidence = 0.95;
                            }
                            else
                            {
                                resClass = ResolutionClass.Ambiguous;
                                confidence = 0.7;
                            }
                        }
                    }
                    else
                    {
                        var importId = TryResolveViaImports(hint.TargetName, fid, allImportsByFileId,
                            symbolsByQualifiedName);
                        if (importId is not null)
                        {
                            targetSymId = importId;
                            resClass = ResolutionClass.ImportBound;
                            confidence = 0.95;
                        }
                        else if (IsLikelyExternalType(hint.TargetName))
                        {
                            resClass = ResolutionClass.External;
                            confidence = 0.8;
                        }
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
                        Confidence = confidence
                    };
                }
            }

            var referencesBySourceSymbolId = BuildReferencesBySourceIndex(allReferences.Values);
            var referencesByFileId = BuildReferencesByFileIdIndex(allReferences, allFiles, allSymbols);

            // ── Step 7: Build and publish new snapshot ─────────────────────────────────
            var completedAtUpdate = DateTimeOffset.UtcNow;
            var snapshotId = GenerateSnapshotId(rootPath, completedAtUpdate);

            var delta = new SnapshotDelta
            {
                AddedFilePaths = addedFiles.Select(f => GetRelativePath(rootPath, f.FilePath)).ToList(),
                ModifiedFilePaths = modifiedFiles.Select(f => GetRelativePath(rootPath, f.FilePath)).ToList(),
                DeletedFilePaths = deletedPaths.Select(p => oldFileMap[p].Path).ToList(),
                UnchangedFileCount = unchangedCount,
                SymbolCountDelta = allSymbols.Count - previousSnapshot.Metadata.SymbolCount,
                ReferenceCountDelta = allReferences.Count - previousSnapshot.Metadata.ReferenceCount,
                DurationMs = (long)(completedAtUpdate - startedAt).TotalMilliseconds,
                PreviousSnapshotId = prevSnapshotId
            };

            var metadata = new SnapshotMetadata
            {
                SnapshotId = snapshotId,
                CreatedAt = completedAtUpdate,
                RootPath = rootPath,
                IndexMode = IndexMode.Incremental,
                FileCount = allFiles.Count,
                StructuralNodeCount = allNodes.Count,
                SymbolCount = allSymbols.Count,
                OccurrenceCount = allOccurrences.Count,
                ReferenceCount = allReferences.Count,
                ImportCount = allImportsByFileId.Values.Sum(l => l.Count),
                DiagnosticCount = allDiagnostics.Count,
                LanguageBreakdown = languageBreakdown,
                AdapterVersions = adapterVersions
            };

            var newSnapshot = new IndexSnapshot
            {
                Metadata = metadata,
                Files = allFiles,
                Nodes = allNodes,
                Symbols = allSymbols,
                Occurrences = allOccurrences,
                References = allReferences,
                ImportsByFileId = allImportsByFileId,
                HintsByFileId = allHintsByFileId,
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
                Delta = delta
            };

            await _store.SaveSnapshotAsync(newSnapshot, ct).ConfigureAwait(false);

            SetState(rootPath, IndexState.Ready,
                snapshotId,
                completedAtUpdate,
                fileCount: allFiles.Count,
                symbolCount: allSymbols.Count,
                referenceCount: allReferences.Count,
                delta: delta);

            return new IndexUpdateResult
            {
                SnapshotId = snapshotId,
                PreviousSnapshotId = prevSnapshotId,
                RootPath = rootPath,
                StartedAt = startedAt,
                CompletedAt = completedAtUpdate,
                DurationMs = (long)(completedAtUpdate - startedAt).TotalMilliseconds,
                FilesAdded = addedFiles.Count,
                FilesModified = modifiedFiles.Count,
                FilesDeleted = deletedPaths.Count,
                FilesUnchanged = unchangedCount
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetState(rootPath, IndexState.Failed, errorMessage: ex.Message);
            return new IndexUpdateResult
            {
                SnapshotId = "(none)",
                RootPath = rootPath,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                DurationMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                Error = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<int> ClearRepositoryAsync(string rootPath, CancellationToken ct = default)
    {
        var removed = await _store.ClearRepositoryAsync(rootPath, ct).ConfigureAwait(false);
        var key = NormalizeRootPath(rootPath);
        lock (_stateLock)
        {
            _states[key] = IndexState.NotIndexed;
            _latestSnapshotIds.Remove(key);
            _lastErrors.Remove(key);
            _lastCompletedAt.Remove(key);
            _latestFileCounts.Remove(key);
            _latestSymbolCounts.Remove(key);
            _latestReferenceCounts.Remove(key);
            _latestDeltas.Remove(key);
        }

        return removed;
    }

    private void SetState(string rootPath, IndexState state,
        string? snapshotId = null, DateTimeOffset completedAt = default, string? errorMessage = null,
        int fileCount = 0, int symbolCount = 0, int referenceCount = 0, SnapshotDelta? delta = null)
    {
        var key = NormalizeRootPath(rootPath);
        lock (_stateLock)
        {
            _states[key] = state;
            if (snapshotId != null) _latestSnapshotIds[key] = snapshotId;
            if (completedAt != default) _lastCompletedAt[key] = completedAt;
            if (errorMessage != null) _lastErrors[key] = errorMessage;
            if (fileCount > 0) _latestFileCounts[key] = fileCount;
            if (symbolCount > 0) _latestSymbolCounts[key] = symbolCount;
            if (referenceCount > 0) _latestReferenceCounts[key] = referenceCount;
            if (delta != null) _latestDeltas[key] = delta;
        }
    }

    private static string NormalizeRootPath(string rootPath)
    {
        return rootPath.Replace('\\', '/').TrimEnd('/');
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
    ///     Recursively collects a node and all its descendants into <paramref name="result" />.
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

    /// <summary>
    ///     Copies all structural nodes reachable from a file's top-level nodes
    ///     from an existing snapshot into the target dictionary.
    /// </summary>
    private static void CopyNodesRecursive(string fileId, IndexSnapshot snapshot,
        Dictionary<string, StructuralNode> target)
    {
        if (!snapshot.FileIdToTopNodeIds.TryGetValue(fileId, out var topIds)) return;
        var queue = new Queue<string>(topIds);
        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!snapshot.Nodes.TryGetValue(nodeId, out var node)) continue;
            target[nodeId] = node;
            foreach (var childId in node.Children)
                queue.Enqueue(childId);
        }
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
    ///     Returns true if a type name is likely from an external library (BCL or well-known packages).
    /// </summary>
    private static bool IsLikelyExternalType(string typeName)
    {
        return s_externalTypeNames.Contains(typeName);
    }

    /// <summary>
    ///     Attempts to resolve <paramref name="targetName" /> via import/using directives in <paramref name="fileId" />.
    ///     Returns a unique symbol ID if exactly one match is found, or null if zero or multiple matches exist.
    /// </summary>
    private static string? TryResolveViaImports(
        string targetName,
        string fileId,
        Dictionary<string, IReadOnlyList<ImportEntry>> importsByFileId,
        IReadOnlyDictionary<string, string> symbolsByQualifiedName)
    {
        if (!importsByFileId.TryGetValue(fileId, out var imports) || imports.Count == 0)
            return null;

        var matches = new HashSet<string>(StringComparer.Ordinal);
        foreach (var imp in imports)
            // Only consider plain namespace/module imports (not static or alias imports)
            if (imp.Kind is "using" or "import" or "from-import")
            {
                if (string.IsNullOrEmpty(imp.NormalizedTarget)) continue;
                var candidate = imp.NormalizedTarget + "." + targetName;
                if (symbolsByQualifiedName.TryGetValue(candidate, out var candId))
                    matches.Add(candId);
            }

        return matches.Count == 1 ? matches.First() : null;
    }
}
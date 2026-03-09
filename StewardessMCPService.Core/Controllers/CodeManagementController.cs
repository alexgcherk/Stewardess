// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using StewardessMCPService.Models;
using StewardessMCPService.Services;
using IndexQuery = StewardessMCPService.CodeIndexing.Query;

namespace StewardessMCPService.Controllers;

[ApiController]
public sealed class CodeManagementController : ControllerBase
{
    private readonly ICommandService _cmd;
    private readonly IEditService _edit;
    private readonly IFileSystemService _fs;
    private readonly IGitService _git;
    private readonly IndexQuery.IIndexQueryService? _indexQuery;
    private readonly ISearchService _search;

    public CodeManagementController(
        IFileSystemService fs,
        ISearchService search,
        IEditService edit,
        IGitService git,
        ICommandService cmd,
        IServiceProvider sp)
    {
        _fs = fs;
        _search = search;
        _edit = edit;
        _git = git;
        _cmd = cmd;
        _indexQuery = sp.GetService(typeof(IndexQuery.IIndexQueryService)) as IndexQuery.IIndexQueryService;
    }

    // ── GET /repository/info ─────────────────────────────────────────────────

    [HttpGet("/repository/info", Name = "getRepositoryInfo")]
    public async Task<IActionResult> GetRepositoryInfo(CancellationToken ct)
    {
        try
        {
            var info = await _fs.GetRepositoryInfoAsync(ct);
            return Ok(new CmRepositoryInfoResponse
            {
                Name = info.RepositoryName,
                RootPath = info.RepositoryRoot,
                DefaultBranch = info.GitInfo?.CurrentBranch ?? "main",
                Languages = new List<CmLanguageInfo>(),
                IgnoreRules = new List<string>(),
                Policy = new CmPolicy
                {
                    AllowsEdits = !info.ReadOnlyMode,
                    RequiresApprovalForDelete = info.Policy?.ApprovalRequiredForDestructive ?? false,
                    RequiresApprovalForRename = false,
                    MaxReadBytes = info.Policy?.MaxFileReadBytes ?? 0,
                    MaxWriteBytes = info.Policy?.MaxFileReadBytes ?? 0
                }
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /repository/tree ────────────────────────────────────────────────

    [HttpPost("/repository/tree", Name = "getRepositoryTree")]
    public async Task<IActionResult> GetRepositoryTree([FromBody] CmRepositoryTreeRequest req, CancellationToken ct)
    {
        req ??= new CmRepositoryTreeRequest();
        try
        {
            var result = await _fs.ListTreeAsync(new ListTreeRequest
            {
                Path = req.Path == "." ? "" : req.Path,
                MaxDepth = req.Depth
            }, ct);

            var entries = new List<CmTreeEntry>();
            FlattenTree(result.Root, entries);

            return Ok(new CmRepositoryTreeResponse
            {
                Root = result.Path,
                Entries = entries
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /search/files ───────────────────────────────────────────────────

    [HttpPost("/search/files", Name = "searchFiles")]
    public async Task<IActionResult> SearchFiles([FromBody] CmFileSearchRequest req, CancellationToken ct)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Query))
            return BadRequest(new { error = "'query' is required." });
        try
        {
            var result = await _search.SearchFileNamesAsync(new SearchFileNamesRequest
            {
                Pattern = req.Query,
                SearchPath = req.BasePath == "." ? "" : req.BasePath,
                MaxResults = req.MaxResults,
                IgnoreCase = true,
                MatchFullPath = req.MatchMode == "regex" || req.MatchMode == "glob"
            }, ct);

            var matches = result.Matches.AsEnumerable();
            if (req.Extensions?.Count > 0)
                matches = matches.Where(m => req.Extensions.Any(e =>
                    m.RelativePath.EndsWith(e, StringComparison.OrdinalIgnoreCase)));

            return Ok(new CmFileSearchResponse
            {
                Results = matches.Select(m => new CmFileSearchResult
                {
                    Path = m.RelativePath,
                    Language = null!,
                    Size = m.SizeBytes > 0 ? m.SizeBytes : null,
                    Score = null
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /search/text ────────────────────────────────────────────────────

    [HttpPost("/search/text", Name = "searchText")]
    public async Task<IActionResult> SearchText([FromBody] CmTextSearchRequest req, CancellationToken ct)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Pattern))
            return BadRequest(new { error = "'pattern' is required." });
        try
        {
            SearchResponse result;
            if (req.IsRegex)
                result = await _search.SearchRegexAsync(new SearchRegexRequest
                {
                    Pattern = req.Pattern,
                    SearchPath = req.BasePath == "." ? "" : req.BasePath,
                    IgnoreCase = !req.CaseSensitive,
                    MaxResults = req.MaxResults,
                    ContextLinesBefore = req.ContextBefore,
                    ContextLinesAfter = req.ContextAfter
                }, ct);
            else
                result = await _search.SearchTextAsync(new SearchTextRequest
                {
                    Query = req.Pattern,
                    IgnoreCase = !req.CaseSensitive,
                    WholeWord = req.WholeWord,
                    SearchPath = req.BasePath == "." ? "" : req.BasePath,
                    MaxResults = req.MaxResults,
                    ContextLinesBefore = req.ContextBefore,
                    ContextLinesAfter = req.ContextAfter
                }, ct);

            var matches = result.Files
                .SelectMany(f => f.Matches.Select(m => new CmTextMatch
                {
                    Path = f.RelativePath,
                    Line = m.LineNumber,
                    Column = m.Column,
                    Preview = m.LineText,
                    ContextBefore = m.ContextBefore ?? new List<string>(),
                    ContextAfter = m.ContextAfter ?? new List<string>()
                }))
                .ToList();

            return Ok(new CmTextSearchResponse { Matches = matches });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /files/read ─────────────────────────────────────────────────────

    [HttpPost("/files/read", Name = "readFile")]
    public async Task<IActionResult> ReadFile([FromBody] CmReadFileRequest req, CancellationToken ct)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Path))
            return BadRequest(new { error = "'path' is required." });
        try
        {
            var firstRange = req.Ranges?.FirstOrDefault();
            if (firstRange != null)
            {
                var rangeResult = await _fs.ReadFileRangeAsync(new ReadFileRangeRequest
                {
                    Path = req.Path,
                    StartLine = firstRange.StartLine,
                    EndLine = firstRange.EndLine,
                    IncludeLineNumbers = req.IncludeLineNumbers
                }, ct);

                return Ok(new CmReadFileResponse
                {
                    Path = rangeResult.RelativePath,
                    Etag = ComputeMd5(rangeResult.Content),
                    Encoding = "utf-8",
                    Newline = "lf",
                    Content = rangeResult.Content,
                    LineCount = rangeResult.TotalLines
                });
            }

            var fileResult = await _fs.ReadFileAsync(new ReadFileRequest
            {
                Path = req.Path,
                MaxBytes = req.MaxBytes
            }, ct);

            return Ok(new CmReadFileResponse
            {
                Path = fileResult.RelativePath,
                Etag = ComputeMd5(fileResult.Content!),
                Encoding = fileResult.Encoding,
                Newline = MapLineEnding(fileResult.LineEnding),
                Content = fileResult.Content!,
                LineCount = fileResult.LineCount
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /files/read-batch ───────────────────────────────────────────────

    [HttpPost("/files/read-batch", Name = "readFilesBatch")]
    public async Task<IActionResult> ReadFilesBatch([FromBody] CmBatchReadFilesRequest req, CancellationToken ct)
    {
        req ??= new CmBatchReadFilesRequest();
        try
        {
            var files = new List<CmReadFileResponse>();
            foreach (var item in req.Items)
                try
                {
                    var fileResult = await _fs.ReadFileAsync(new ReadFileRequest
                    {
                        Path = item.Path,
                        MaxBytes = item.MaxBytes
                    }, ct);

                    files.Add(new CmReadFileResponse
                    {
                        Path = fileResult.RelativePath,
                        Etag = ComputeMd5(fileResult.Content!),
                        Encoding = fileResult.Encoding,
                        Newline = MapLineEnding(fileResult.LineEnding),
                        Content = fileResult.Content!,
                        LineCount = fileResult.LineCount
                    });
                }
                catch
                {
                    files.Add(new CmReadFileResponse { Path = item.Path });
                }

            return Ok(new CmBatchReadFilesResponse { Files = files });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /files/write ────────────────────────────────────────────────────

    [HttpPost("/files/write", Name = "writeFile")]
    public async Task<IActionResult> WriteFile([FromBody] CmWriteFileRequest req, CancellationToken ct)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Path))
            return BadRequest(new { error = "'path' is required." });
        try
        {
            var result = await _edit.WriteFileAsync(new WriteFileRequest
            {
                Path = req.Path,
                Content = req.Content,
                Encoding = req.Encoding ?? "utf-8",
                LineEnding = req.Newline == "lf" ? "LF" : req.Newline == "crlf" ? "CRLF" : "auto",
                Options = new EditOptions
                {
                    DryRun = req.DryRun,
                    CreateBackup = req.MakeBackup,
                    ChangeReason = req.Reason
                }
            }, ct);

            return Ok(new CmWriteFileResponse
            {
                Path = result.RelativePath,
                Etag = ComputeMd5(req.Content),
                BytesWritten = Encoding.UTF8.GetByteCount(req.Content ?? ""),
                Created = result.Operation?.Equals("create", StringComparison.OrdinalIgnoreCase) == true,
                DryRun = result.WasDryRun,
                BackupPath = result.BackupPath
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /files/delete ───────────────────────────────────────────────────

    [HttpPost("/files/delete", Name = "deleteFile")]
    public async Task<IActionResult> DeleteFile([FromBody] CmDeleteFileRequest req, CancellationToken ct)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Path))
            return BadRequest(new { error = "'path' is required." });
        try
        {
            var result = await _edit.DeleteFileAsync(new DeleteFileRequest
            {
                Path = req.Path,
                Options = new EditOptions { DryRun = req.DryRun, ChangeReason = req.Reason }
            }, ct);

            return Ok(new CmDeleteFileResponse
            {
                Deleted = result.Success,
                Path = result.RelativePath,
                BackupPath = result.BackupPath,
                DryRun = result.WasDryRun
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /files/move ─────────────────────────────────────────────────────

    [HttpPost("/files/move", Name = "moveFile")]
    public async Task<IActionResult> MoveFile([FromBody] CmMoveFileRequest req, CancellationToken ct)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.SourcePath))
            return BadRequest(new { error = "'sourcePath' is required." });
        try
        {
            var result = await _edit.MovePathAsync(new MovePathRequest
            {
                SourcePath = req.SourcePath,
                DestinationPath = req.DestinationPath,
                Overwrite = req.Overwrite,
                Options = new EditOptions { DryRun = req.DryRun, ChangeReason = req.Reason }
            }, ct);

            return Ok(new CmMoveFileResponse
            {
                Moved = result.Success,
                SourcePath = req.SourcePath,
                DestinationPath = req.DestinationPath,
                UpdatedReferenceCount = result.AffectedCount,
                DryRun = result.WasDryRun
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /edits/apply-patch ──────────────────────────────────────────────

    [HttpPost("/edits/apply-patch", Name = "applyPatch")]
    public async Task<IActionResult> ApplyPatch([FromBody] CmApplyPatchRequest req, CancellationToken ct)
    {
        req ??= new CmApplyPatchRequest();
        try
        {
            var fileResults = new List<CmApplyPatchFileResult>();
            var allOrNothing = req.TransactionMode == "allOrNothing";

            foreach (var edit in req.Edits)
                try
                {
                    var result = edit.PatchType switch
                    {
                        "unifiedDiff" => await _edit.PatchFileAsync(new PatchFileRequest
                        {
                            Path = edit.Path,
                            Patch = edit.UnifiedDiff!,
                            Options = new EditOptions
                            {
                                DryRun = req.DryRun,
                                CreateBackup = req.MakeBackup,
                                ChangeReason = req.Reason
                            }
                        }, ct),
                        "searchReplace" => await _edit.ReplaceTextAsync(new ReplaceTextRequest
                        {
                            Path = edit.Path,
                            OldText = edit.Search!,
                            NewText = edit.Replace!,
                            IgnoreCase = false,
                            MaxReplacements = edit.ReplaceAll ? 0 : 1,
                            Options = new EditOptions
                            {
                                DryRun = req.DryRun,
                                CreateBackup = req.MakeBackup,
                                ChangeReason = req.Reason
                            }
                        }, ct),
                        "replaceRange" => await _edit.ReplaceLinesAsync(new ReplaceLinesRequest
                        {
                            Path = edit.Path,
                            StartLine = edit.Range?.StartLine ?? 0,
                            EndLine = edit.Range?.EndLine ?? 0,
                            NewContent = edit.Replace!,
                            Options = new EditOptions
                            {
                                DryRun = req.DryRun,
                                CreateBackup = req.MakeBackup,
                                ChangeReason = req.Reason
                            }
                        }, ct),
                        _ => throw new ArgumentException($"Unknown PatchType: {edit.PatchType}")
                    };

                    fileResults.Add(new CmApplyPatchFileResult
                    {
                        Path = result.RelativePath,
                        Etag = ComputeMd5(""),
                        Changed = result.Success,
                        BackupPath = result.BackupPath,
                        Message = result.ErrorMessage
                    });
                }
                catch (Exception ex)
                {
                    fileResults.Add(new CmApplyPatchFileResult
                    {
                        Path = edit.Path,
                        Changed = false,
                        Message = ex.Message
                    });
                    if (allOrNothing)
                        return Ok(new CmApplyPatchResponse
                        {
                            Applied = false,
                            DryRun = req.DryRun,
                            Files = fileResults
                        });
                }

            var applied = fileResults.Count > 0 && fileResults.All(f => f.Changed);
            return Ok(new CmApplyPatchResponse { Applied = applied, DryRun = req.DryRun, Files = fileResults });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /index/symbols/find ─────────────────────────────────────────────

    [HttpPost("/index/symbols/find", Name = "findSymbols")]
    public async Task<IActionResult> FindSymbols([FromBody] CmFindSymbolsRequest req, CancellationToken ct)
    {
        req ??= new CmFindSymbolsRequest();
        if (_indexQuery == null)
            return Ok(new CmFindSymbolsResponse());
        try
        {
            var result = await _indexQuery.FindSymbolsAsync(new IndexQuery.FindSymbolsRequest
            {
                QueryText = req.Query ?? "",
                MatchMode = req.Fuzzy ? "contains" : "exact",
                PageSize = Math.Min(req.MaxResults, 100)
            }, ct);

            return Ok(new CmFindSymbolsResponse
            {
                Results = result.Items.Select(MapSymbolToRef).ToList()
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /index/references/find ──────────────────────────────────────────

    [HttpPost("/index/references/find", Name = "findReferences")]
    public async Task<IActionResult> FindReferences([FromBody] CmFindReferencesRequest req, CancellationToken ct)
    {
        req ??= new CmFindReferencesRequest();
        if (_indexQuery == null || (string.IsNullOrEmpty(req.SymbolId) && string.IsNullOrEmpty(req.Name)))
            return Ok(new CmFindReferencesResponse());
        try
        {
            if (!string.IsNullOrEmpty(req.SymbolId))
            {
                var result = await _indexQuery.GetReferencesAsync(
                    new IndexQuery.GetReferencesRequest
                    {
                        SymbolId = req.SymbolId,
                        IncludeOutgoing = true,
                        IncludeIncoming = true
                    },
                    pageSize: req.MaxResults,
                    ct: ct);

                var refs = result.OutgoingRefs.Concat(result.IncomingRefs)
                    .Select(r => new CmReferenceResult
                    {
                        Path = "",
                        StartLine = r.EvidenceSpan?.StartLine ?? 0,
                        EndLine = r.EvidenceSpan?.EndLine ?? 0,
                        Preview = r.Evidence ?? "",
                        ReferenceKind = r.RelationshipKind.ToString()
                    })
                    .ToList();

                return Ok(new CmFindReferencesResponse { References = refs });
            }
            else
            {
                var result = await _search.SearchSymbolAsync(new SearchSymbolRequest
                {
                    SymbolName = req.Name!,
                    MaxResults = req.MaxResults
                }, ct);

                var refs = result.Files
                    .SelectMany(f => f.Matches.Select(m => new CmReferenceResult
                    {
                        Path = f.RelativePath,
                        StartLine = m.LineNumber,
                        EndLine = m.LineNumber,
                        Preview = m.LineText,
                        ReferenceKind = "reference"
                    }))
                    .ToList();

                return Ok(new CmFindReferencesResponse { References = refs });
            }
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /index/dependencies ─────────────────────────────────────────────

    [HttpPost("/index/dependencies", Name = "getDependencies")]
    public async Task<IActionResult> GetDependencies([FromBody] CmDependenciesRequest req, CancellationToken ct)
    {
        req ??= new CmDependenciesRequest();
        if (_indexQuery == null || string.IsNullOrEmpty(req.Path))
            return Ok(new CmDependenciesResponse());
        try
        {
            var result = await _indexQuery.GetFileDependenciesAsync(
                new IndexQuery.GetFileDependenciesRequest { FilePath = req.Path }, ct);

            var nodes = new List<CmDependencyNode>
            {
                new() { Id = req.Path, Label = req.Path, Kind = "file", Path = req.Path }
            };
            var edges = new List<CmDependencyEdge>();

            foreach (var dep in result.Dependencies)
            {
                nodes.Add(new CmDependencyNode
                {
                    Id = dep.TargetFilePath,
                    Label = dep.TargetFilePath,
                    Kind = "file",
                    Path = dep.TargetFilePath
                });
                edges.Add(new CmDependencyEdge
                {
                    From = req.Path,
                    To = dep.TargetFilePath,
                    Kind = string.Join(", ", dep.RelationshipKinds)
                });
            }

            return Ok(new CmDependenciesResponse { Nodes = nodes, Edges = edges });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /index/call-graph ───────────────────────────────────────────────

    [HttpPost("/index/call-graph", Name = "getCallGraph")]
    public async Task<IActionResult> GetCallGraph([FromBody] CmCallGraphRequest req, CancellationToken ct)
    {
        req ??= new CmCallGraphRequest();
        if (_indexQuery == null || string.IsNullOrEmpty(req.SymbolId))
            return Ok(new CmCallGraphResponse());
        try
        {
            var result = await _indexQuery.GetSymbolRelationshipsAsync(
                new IndexQuery.GetSymbolRelationshipsRequest
                {
                    SymbolId = req.SymbolId,
                    IncludeReferences = true,
                    IncludeDependencies = true,
                    IncludeDependents = false,
                    IncludeChildren = true,
                    MaxItemsPerSection = req.MaxNodes
                }, ct);

            var nodes = (result.Children ?? Enumerable.Empty<IndexQuery.SymbolSummary>())
                .Select(MapSymbolToRef)
                .ToList();

            var edges = (result.References ?? Enumerable.Empty<IndexQuery.ReferenceSummary>())
                .Select(r => new CmCallEdge
                {
                    FromSymbolId = r.SourceSymbolId ?? "",
                    ToSymbolId = r.TargetSymbolId ?? ""
                })
                .ToList();

            return Ok(new CmCallGraphResponse
            {
                Center = new CmSymbolRef { SymbolId = req.SymbolId },
                Nodes = nodes,
                Edges = edges
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /validation/build ───────────────────────────────────────────────

    [HttpPost("/validation/build", Name = "buildWorkspace")]
    public async Task<IActionResult> BuildWorkspace([FromBody] CmBuildRequest req, CancellationToken ct)
    {
        req ??= new CmBuildRequest();
        try
        {
            var args = (req.Target ?? "")
                       + (req.Configuration != null ? $" -c {req.Configuration}" : "")
                       + (req.NoRestore ? " --no-restore" : "");

            var result = await _cmd.RunBuildAsync(new RunBuildRequest
            {
                WorkingDirectory = "",
                BuildCommand = "dotnet build",
                Arguments = args.Trim(),
                TimeoutSeconds = req.TimeoutSeconds > 0 ? req.TimeoutSeconds : null
            }, ct);

            var diagnostics = new List<CmDiagnostic>();
            foreach (var d in result.Summary?.Errors ?? new List<BuildDiagnostic>())
                diagnostics.Add(new CmDiagnostic
                {
                    Severity = d.Severity,
                    Code = d.Code,
                    Message = d.Message,
                    Path = d.FilePath,
                    Line = d.Line,
                    Column = d.Column
                });
            foreach (var d in result.Summary?.Warnings ?? new List<BuildDiagnostic>())
                diagnostics.Add(new CmDiagnostic
                {
                    Severity = d.Severity,
                    Code = d.Code,
                    Message = d.Message,
                    Path = d.FilePath,
                    Line = d.Line,
                    Column = d.Column
                });

            return Ok(new CmBuildResponse
            {
                Success = result.Succeeded,
                ExitCode = result.ExitCode,
                Diagnostics = diagnostics,
                Stdout = result.StandardOutput,
                Stderr = result.StandardError,
                DurationMs = result.ElapsedMs
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /validation/format ──────────────────────────────────────────────

    [HttpPost("/validation/format", Name = "formatFiles")]
    public async Task<IActionResult> FormatFiles([FromBody] CmFormatRequest req, CancellationToken ct)
    {
        req ??= new CmFormatRequest();
        try
        {
            var command = string.IsNullOrEmpty(req.Target)
                ? "dotnet format"
                : $"dotnet format {req.Target}";

            var result = await _cmd.RunCustomCommandAsync(new RunCustomCommandRequest
            {
                Command = command,
                WorkingDirectory = "",
                TimeoutSeconds = req.TimeoutSeconds > 0 ? req.TimeoutSeconds : null
            }, ct);

            return Ok(new CmFormatResponse
            {
                FormattedFiles = new List<string>(),
                Changed = result.Succeeded,
                Stdout = result.StandardOutput,
                Stderr = result.StandardError
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /validation/test ────────────────────────────────────────────────

    [HttpPost("/validation/test", Name = "runTests")]
    public async Task<IActionResult> RunTests([FromBody] CmTestRequest req, CancellationToken ct)
    {
        req ??= new CmTestRequest();
        try
        {
            var result = await _cmd.RunTestsAsync(new RunTestsRequest
            {
                WorkingDirectory = "",
                TestCommand = "dotnet test",
                Arguments = req.Target ?? "",
                Filter = req.Filter ?? "",
                TimeoutSeconds = req.TimeoutSeconds > 0 ? req.TimeoutSeconds : null
            }, ct);

            return Ok(new CmTestResponse
            {
                Success = result.Succeeded,
                Summary = new CmTestSummary
                {
                    Total = result.Summary?.TestsTotal ?? 0,
                    Passed = result.Summary?.TestsPassed ?? 0,
                    Failed = result.Summary?.TestsFailed ?? 0,
                    Skipped = result.Summary?.TestsSkipped ?? 0
                },
                Tests = new List<CmTestCaseResult>(),
                Stdout = result.StandardOutput,
                Stderr = result.StandardError,
                DurationMs = result.ElapsedMs
            });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── POST /history/changes ────────────────────────────────────────────────

    [HttpPost("/history/changes", Name = "getChanges")]
    public async Task<IActionResult> GetChanges([FromBody] CmChangesRequest req, CancellationToken ct)
    {
        req ??= new CmChangesRequest();
        try
        {
            var statusResult = await _git.GetStatusAsync(new GitStatusRequest(), ct);

            var patchByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (req.IncludePatch)
            {
                var diffResult = await _git.GetDiffAsync(new GitDiffRequest
                {
                    Scope = req.BaseRef != null ? "commit" : "unstaged"
                }, ct);

                foreach (var fileDiff in diffResult.Files ?? new List<GitFileDiff>())
                    patchByPath[fileDiff.RelativePath] = BuildFilePatch(fileDiff);
            }

            var files = statusResult.Files
                .Where(f => req.IncludeUntracked || !f.IsUntracked)
                .Select(f => new CmChangedFile
                {
                    Path = f.RelativePath,
                    OldPath = f.OldPath,
                    Status = MapGitStatus(f),
                    Patch = patchByPath.TryGetValue(f.RelativePath, out var patch) ? patch : null!
                })
                .ToList();

            return Ok(new CmChangesResponse { Files = files });
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void FlattenTree(TreeNode node, List<CmTreeEntry> entries)
    {
        if (node == null) return;
        entries.Add(new CmTreeEntry
        {
            Path = node.RelativePath,
            Kind = node.Type == "directory" ? "directory" : "file",
            Size = node.SizeBytes
        });
        foreach (var child in node.Children ?? new List<TreeNode>())
            FlattenTree(child, entries);
    }

    private static string ComputeMd5(string content)
    {
        if (content == null) return "";
        var bytes = Encoding.UTF8.GetBytes(content);
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static string MapLineEnding(string lineEnding)
    {
        return lineEnding switch
        {
            "LF" => "lf",
            "CRLF" => "crlf",
            "CR" => "cr",
            "Mixed" => "mixed",
            _ => "lf"
        };
    }

    private static CmSymbolRef MapSymbolToRef(IndexQuery.SymbolSummary s)
    {
        return new CmSymbolRef
        {
            SymbolId = s.SymbolId,
            Name = s.Name,
            Kind = s.Kind.ToString(),
            Language = s.LanguageId,
            Namespace = "",
            ContainerName = string.Join(".", s.ContainerPath),
            Path = s.PrimaryLocation?.FilePath ?? "",
            StartLine = s.PrimaryLocation?.SourceSpan?.StartLine ?? 0,
            EndLine = s.PrimaryLocation?.SourceSpan?.EndLine ?? 0,
            Signature = s.QualifiedName
        };
    }

    private static string MapGitStatus(GitStatusEntry e)
    {
        if (e.IsUntracked) return "untracked";
        var status = string.IsNullOrEmpty(e.IndexStatus) ? e.WorkTreeStatus : e.IndexStatus;
        return status switch
        {
            "M" => "modified",
            "A" => "added",
            "D" => "deleted",
            "R" => "renamed",
            "?" => "untracked",
            _ => status ?? "unknown"
        };
    }

    private static string BuildFilePatch(GitFileDiff fileDiff)
    {
        var sb = new StringBuilder();
        foreach (var hunk in fileDiff.Hunks ?? new List<GitDiffHunk>())
        {
            sb.AppendLine(hunk.Header);
            foreach (var line in hunk.Lines ?? new List<GitDiffLine>())
            {
                var prefix = line.Type == "+" ? "+" : line.Type == "-" ? "-" : " ";
                sb.AppendLine(prefix + line.Text);
            }
        }

        return sb.ToString();
    }

    private IActionResult HandleException(Exception ex)
    {
        return ex switch
        {
            ArgumentException ae => BadRequest(new { error = ae.Message }),
            FileNotFoundException _ or DirectoryNotFoundException _ =>
                NotFound(new { error = ex.Message }),
            UnauthorizedAccessException _ => StatusCode(403, new { error = ex.Message }),
            OperationCanceledException _ => StatusCode(408, new { error = "The operation timed out." }),
            _ => StatusCode(500, new { error = "An unexpected error occurred." })
        };
    }
}
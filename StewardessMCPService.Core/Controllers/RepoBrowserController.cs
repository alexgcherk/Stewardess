// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;
using StewardessMCPService.Services;

namespace StewardessMCPService.Controllers
{
    /// <summary>
    /// Repository browser endpoints — a minimal companion toolset for agent-driven
    /// repository navigation.
    ///
    /// GET  /api/repo-browser/tree   — print_tree
    /// POST /api/repo-browser/grep   — grep
    /// GET  /api/repo-browser/file   — read_file
    /// POST /api/repo-browser/find   — find_path
    /// GET  /api/repo-browser/search — search
    /// </summary>
    [Route("api/repo-browser")]
    public sealed class RepoBrowserController : BaseController
    {
        private IFileSystemService FileService  => GetService<IFileSystemService>();
        private ISearchService     SearchService => GetService<ISearchService>();
        private McpServiceSettings Settings      => GetService<McpServiceSettings>();

        // ── print_tree ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a flat list of repository files and directories.
        /// Use to discover candidate paths before calling read_file or grep.
        /// </summary>
        [HttpGet, Route("tree")]
        public IActionResult PrintTree(
            [FromQuery] string relativePath      = "",
            [FromQuery] int    maxDepth          = 4,
            [FromQuery] bool   includeFiles      = true,
            [FromQuery] bool   includeDirectories = true,
            [FromQuery] int    maxEntries        = 1000,
            [FromQuery] bool   showHidden        = false)
        {
            try
            {
                // Normalise "." / "./" to root — consistent with empty string.
                if (relativePath == "." || relativePath == "./" || relativePath == ".\\")
                    relativePath = "";

                var treeReq = new ListTreeRequest
                {
                    Path            = relativePath ?? "",
                    MaxDepth        = Math.Clamp(maxDepth, 1, 20),
                    DirectoriesOnly = !includeFiles,
                };
                var treeResp = FileService.ListTreeAsync(treeReq, CancellationToken.None).GetAwaiter().GetResult();

                var items  = new List<RepoBrowserTreeItem>();
                bool trunc = treeResp.Truncated;

                void WalkNode(TreeNode node, int depth)
                {
                    if (node == null || items.Count >= maxEntries) { trunc = true; return; }
                    var isDir = node.Type == "directory";
                    if (!showHidden && node.Name.StartsWith(".")) return;
                    if (isDir && !includeDirectories)
                    {
                        if (node.Children != null)
                            foreach (var c in node.Children) WalkNode(c, depth + 1);
                        return;
                    }
                    if (!isDir && !includeFiles) return;

                    items.Add(new RepoBrowserTreeItem
                    {
                        Path        = node.RelativePath ?? "",
                        Name        = node.Name,
                        Kind        = isDir ? "directory" : "file",
                        Depth       = depth,
                        HasChildren = isDir ? (bool?)(node.Children != null && node.Children.Count > 0) : null,
                        SizeBytes   = node.SizeBytes,
                    });

                    if (isDir && node.Children != null)
                        foreach (var c in node.Children) WalkNode(c, depth + 1);
                }

                if (treeResp.Root?.Children != null)
                    foreach (var c in treeResp.Root.Children) WalkNode(c, 0);

                return Ok(new RepoBrowserTreeResponse
                {
                    RootPath     = Settings.RepositoryRoot,
                    RelativePath = relativePath ?? "",
                    MaxDepth     = maxDepth,
                    EntryCount   = items.Count,
                    Truncated    = trunc,
                    Items        = items,
                });
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── grep ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Searches file contents for text patterns across the repository.
        /// Supports literal, word, regex, and symbol_hint modes.
        /// Returns a flat match list with line numbers and surrounding context.
        /// </summary>
        [HttpPost, Route("grep")]
        public IActionResult Grep([FromBody] RepoBrowserGrepRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Query))
                return BadRequest(ErrorCodes.MissingParameter, "'query' is required.");

            try
            {
                var mode       = (request.Mode ?? "literal").ToLowerInvariant();
                var maxResults = request.MaxResults > 0 ? request.MaxResults : 100;
                var maxPerFile = request.MaxMatchesPerFile > 0 ? request.MaxMatchesPerFile : 20;
                var ctxLines   = request.ContextLines >= 0 ? request.ContextLines : 2;

                SearchResponse sr;
                if (mode == "regex")
                {
                    sr = SearchService.SearchRegexAsync(new SearchRegexRequest
                    {
                        Pattern            = request.Query,
                        SearchPath         = request.PathPrefix ?? "",
                        IgnoreCase         = !request.CaseSensitive,
                        MaxResults         = maxResults * 2,
                        ContextLinesBefore = ctxLines,
                        ContextLinesAfter  = ctxLines,
                    }, CancellationToken.None).GetAwaiter().GetResult();
                }
                else
                {
                    sr = SearchService.SearchTextAsync(new SearchTextRequest
                    {
                        Query              = request.Query,
                        SearchPath         = request.PathPrefix ?? "",
                        IgnoreCase         = !request.CaseSensitive,
                        WholeWord          = mode == "word" || mode == "symbol_hint",
                        MaxResults         = maxResults * 2,
                        ContextLinesBefore = ctxLines,
                        ContextLinesAfter  = ctxLines,
                    }, CancellationToken.None).GetAwaiter().GetResult();
                }

                var flat  = new List<RepoBrowserGrepMatch>();
                bool trunc = sr.Truncated;

                foreach (var file in sr.Files)
                {
                    int fileHits = 0;
                    foreach (var m in file.Matches)
                    {
                        if (flat.Count >= maxResults) { trunc = true; break; }
                        if (fileHits >= maxPerFile)   { trunc = true; break; }
                        flat.Add(new RepoBrowserGrepMatch
                        {
                            FilePath      = file.RelativePath,
                            LineNumber    = m.LineNumber,
                            ColumnStart   = m.Column > 0 ? (int?)m.Column : null,
                            LineText      = m.LineText,
                            BeforeContext = m.ContextBefore ?? new List<string>(),
                            AfterContext  = m.ContextAfter  ?? new List<string>(),
                        });
                        fileHits++;
                    }
                    if (flat.Count >= maxResults) break;
                }

                return Ok(new RepoBrowserGrepResponse
                {
                    RootPath   = Settings.RepositoryRoot,
                    Query      = request.Query,
                    Mode       = mode,
                    MatchCount = flat.Count,
                    Truncated  = trunc,
                    Items      = flat,
                });
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── read_file ────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads and returns the contents of a specific file.
        /// Specify startLine/endLine to read a partial range for large files.
        /// </summary>
        [HttpGet, Route("file")]
        public IActionResult ReadFile(
            [FromQuery] string filePath,
            [FromQuery] int?   startLine          = null,
            [FromQuery] int?   endLine            = null,
            [FromQuery] int    maxBytes           = 65536,
            [FromQuery] bool   includeLineNumbers = true)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return BadRequest(ErrorCodes.MissingParameter, "'filePath' is required.");

            var resp = new RepoBrowserReadFileResponse
            {
                RootPath = Settings.RepositoryRoot,
                FilePath = filePath,
            };

            try
            {
                if (startLine.HasValue)
                {
                    var r = FileService.ReadFileRangeAsync(new ReadFileRangeRequest
                    {
                        Path               = filePath,
                        StartLine          = startLine.Value,
                        EndLine            = endLine ?? -1,
                        IncludeLineNumbers = includeLineNumbers,
                    }, CancellationToken.None).GetAwaiter().GetResult();

                    resp.Exists    = true;
                    resp.StartLine = r.StartLine;
                    resp.EndLine   = r.EndLine;
                    resp.Content   = r.Content;
                }
                else
                {
                    var r = FileService.ReadFileAsync(new ReadFileRequest
                    {
                        Path     = filePath,
                        MaxBytes = maxBytes,
                    }, CancellationToken.None).GetAwaiter().GetResult();

                    resp.Exists    = true;
                    resp.Encoding  = r.Encoding;
                    resp.SizeBytes = r.SizeBytes;
                    resp.Truncated = r.Truncated;
                    resp.Content   = includeLineNumbers ? AddLineNumbers(r.Content!) : r.Content!;
                }

                return Ok(resp);
            }
            catch (Exception ex) when (
                ex.Message.Contains("not found",    StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("outside the repository", StringComparison.OrdinalIgnoreCase))
            {
                resp.Exists  = false;
                resp.Content = string.Empty;
                return Ok(resp);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── find_path ────────────────────────────────────────────────────────────

        /// <summary>
        /// Locates files or directories by name or path fragment.
        /// Use before read_file when the exact path is unknown.
        /// </summary>
        [HttpPost, Route("find")]
        public IActionResult FindPath([FromBody] RepoBrowserFindPathRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Query))
                return BadRequest(ErrorCodes.MissingParameter, "'query' is required.");

            try
            {
                var matchMode  = (request.MatchMode ?? "name").ToLowerInvariant();
                var targetKind = (request.TargetKind ?? "any").ToLowerInvariant();
                var maxResults = request.MaxResults > 0 ? request.MaxResults : 50;
                var cmp        = request.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var items      = new List<RepoBrowserPathMatch>();
                bool trunc     = false;
                var qn         = request.Query.Replace('\\', '/');

                var regexOpts = RegexOptions.Compiled | (request.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                var useRegex  = PatternHelper.IsLikelyRegex(request.Query);
                Regex? queryRegex = null;
                if (useRegex)
                    queryRegex = new Regex(request.Query, regexOpts);

                // ── Files ────────────────────────────────────────────────────────
                if (targetKind == "file" || targetKind == "any")
                {
                    var fileResp = SearchService.SearchFileNamesAsync(new SearchFileNamesRequest
                    {
                        // When regex is active, fetch all files and filter in the loop below.
                        Pattern       = useRegex ? "*" : (matchMode == "name" ? request.Query : "*"),
                        MaxResults    = maxResults * 3,
                        IgnoreCase    = !request.CaseSensitive,
                        MatchFullPath = !useRegex && matchMode != "name",
                    }, CancellationToken.None).GetAwaiter().GetResult();

                    foreach (var m in fileResp.Matches)
                    {
                        if (items.Count >= maxResults) { trunc = true; break; }
                        var rel  = m.RelativePath ?? "";
                        var norm = rel.Replace('\\', '/');

                        string? reason;
                        if (useRegex && queryRegex != null)
                        {
                            var target = matchMode == "name" ? m.Name : norm;
                            reason = queryRegex.IsMatch(target ?? "") ? "regex match" : null;
                        }
                        else
                        {
                            reason = matchMode switch
                            {
                                "name"          => "file name match",
                                "path_fragment" => norm.Contains(qn, cmp) ? "path fragment match" : null,
                                "exact_path"    => string.Equals(norm, qn, cmp) ? "exact path match" : null,
                                "prefix"        => norm.StartsWith(qn, cmp) ? "path prefix match" : null,
                                _               => null,
                            };
                        }
                        if (reason == null) continue;

                        items.Add(new RepoBrowserPathMatch
                        {
                            Path        = rel,
                            Name        = m.Name,
                            Kind        = "file",
                            MatchReason = reason,
                        });
                    }
                }

                // ── Directories ──────────────────────────────────────────────────
                if ((targetKind == "directory" || targetKind == "any") && items.Count < maxResults)
                {
                    var treeResp = FileService.ListTreeAsync(new ListTreeRequest
                    {
                        Path            = "",
                        MaxDepth        = 15,
                        DirectoriesOnly = true,
                    }, CancellationToken.None).GetAwaiter().GetResult();

                    void WalkDirs(TreeNode node)
                    {
                        if (node == null || items.Count >= maxResults) return;
                        if (node.Type == "directory" && !string.IsNullOrEmpty(node.RelativePath))
                        {
                            var rel  = node.RelativePath;
                            var norm = rel.Replace('\\', '/');

                            string? reason;
                            if (useRegex && queryRegex != null)
                            {
                                var target = matchMode == "name" ? node.Name : norm;
                                reason = queryRegex.IsMatch(target) ? "regex match" : null;
                            }
                            else
                            {
                                reason = matchMode switch
                                {
                                    "name"          => node.Name.Contains(request.Query, cmp) ? "directory name match" : null,
                                    "path_fragment" => norm.Contains(qn, cmp) ? "path fragment match" : null,
                                    "exact_path"    => string.Equals(norm, qn, cmp) ? "exact path match" : null,
                                    "prefix"        => norm.StartsWith(qn, cmp) ? "path prefix match" : null,
                                    _               => null,
                                };
                            }

                            if (reason != null)
                            {
                                items.Add(new RepoBrowserPathMatch
                                {
                                    Path        = rel,
                                    Name        = node.Name,
                                    Kind        = "directory",
                                    MatchReason = reason,
                                });
                            }
                        }
                        if (node.Children != null)
                            foreach (var c in node.Children) WalkDirs(c);
                    }

                    WalkDirs(treeResp.Root);
                    if (treeResp.Truncated) trunc = true;
                }

                var capped = items.Count > maxResults ? items.GetRange(0, maxResults) : items;
                return Ok(new RepoBrowserFindPathResponse
                {
                    RootPath    = Settings.RepositoryRoot,
                    Query       = request.Query,
                    MatchMode   = matchMode,
                    TargetKind  = targetKind,
                    ResultCount = capped.Count,
                    Truncated   = trunc,
                    Items       = capped,
                });
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── search ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Finds files whose names contain the query as a substring.
        /// Use when you know part of a filename but not its location.
        /// </summary>
        [HttpGet, Route("search")]
        public IActionResult Search(
            [FromQuery] string query,
            [FromQuery] string pathPrefix    = "",
            [FromQuery] bool   caseSensitive = false,
            [FromQuery] int    maxResults    = 50)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest(ErrorCodes.MissingParameter, "'query' is required.");

            try
            {
                maxResults = Math.Max(1, maxResults);

                var fileResp = SearchService.SearchFileNamesAsync(new SearchFileNamesRequest
                {
                    Pattern    = query,
                    SearchPath = pathPrefix ?? "",
                    MaxResults = maxResults + 1,
                    IgnoreCase = !caseSensitive,
                }, CancellationToken.None).GetAwaiter().GetResult();

                bool truncated = fileResp.Matches.Count > maxResults || fileResp.Truncated;
                var capped = fileResp.Matches.Count > maxResults
                    ? fileResp.Matches.GetRange(0, maxResults)
                    : fileResp.Matches;

                return Ok(new RepoBrowserSearchResponse
                {
                    RootPath    = Settings.RepositoryRoot,
                    Query       = query,
                    PathPrefix  = pathPrefix ?? "",
                    ResultCount = capped.Count,
                    Truncated   = truncated,
                    Items       = capped.Select(m => new RepoBrowserSearchMatch
                    {
                        Path      = m.RelativePath ?? "",
                        Name      = m.Name ?? "",
                        Kind      = "file",
                        SizeBytes = m.SizeBytes,
                    }).ToList(),
                });
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static string AddLineNumbers(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;
            var lines = content.Split('\n');
            var sb    = new System.Text.StringBuilder(content.Length + lines.Length * 6);
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append(i + 1).Append(": ").Append(lines[i]);
                if (i < lines.Length - 1) sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    // ── Request models used by this controller only ──────────────────────────────

    /// <summary>Request body for the repo-browser grep endpoint.</summary>
    public sealed class RepoBrowserGrepRequest
    {
        /// <summary>Text or pattern to search for.</summary>
        public string Query { get; set; } = null!;

        /// <summary>Search mode: literal (default), regex, word, symbol_hint.</summary>
        public string Mode { get; set; } = "literal";

        /// <summary>Restrict search to this subdirectory relative path.</summary>
        public string PathPrefix { get; set; } = null!;

        /// <summary>Case-sensitive search (default false).</summary>
        public bool CaseSensitive { get; set; } = false;

        /// <summary>Maximum total match lines to return (default 100).</summary>
        public int MaxResults { get; set; } = 100;

        /// <summary>Maximum match lines per file (default 20).</summary>
        public int MaxMatchesPerFile { get; set; } = 20;

        /// <summary>Context lines before and after each match (default 2).</summary>
        public int ContextLines { get; set; } = 2;
    }

    /// <summary>Request body for the repo-browser find-path endpoint.</summary>
    public sealed class RepoBrowserFindPathRequest
    {
        /// <summary>File name, directory name, or partial path to search for.</summary>
        public string Query { get; set; } = null!;

        /// <summary>Matching mode: name (default), path_fragment, exact_path, prefix.</summary>
        public string MatchMode { get; set; } = "name";

        /// <summary>What to find: file, directory, or any (default).</summary>
        public string TargetKind { get; set; } = "any";

        /// <summary>Case-sensitive matching (default false).</summary>
        public bool CaseSensitive { get; set; } = false;

        /// <summary>Maximum results to return (default 50).</summary>
        public int MaxResults { get; set; } = 50;
    }
}

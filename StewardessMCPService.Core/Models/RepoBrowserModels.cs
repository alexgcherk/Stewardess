// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;

namespace StewardessMCPService.Models
{
    // ── repo_browser.print_tree ──────────────────────────────────────────────────

    /// <summary>Response from the repo_browser.print_tree tool.</summary>
    public sealed class RepoBrowserTreeResponse
    {
        /// <summary>Absolute path of the repository root on the server.</summary>
        public string RootPath { get; set; } = null!;
        /// <summary>Repository-relative path that was inspected.</summary>
        public string RelativePath { get; set; } = null!;
        /// <summary>Maximum depth that was traversed.</summary>
        public int MaxDepth { get; set; }
        /// <summary>Number of entries returned.</summary>
        public int EntryCount { get; set; }
        /// <summary>True when the result was capped at maxEntries.</summary>
        public bool Truncated { get; set; }
        /// <summary>Flat list of tree entries.</summary>
        public List<RepoBrowserTreeItem> Items { get; set; } = new List<RepoBrowserTreeItem>();
    }

    /// <summary>A single entry in a print_tree result.</summary>
    public sealed class RepoBrowserTreeItem
    {
        /// <summary>Repository-relative path of the entry.</summary>
        public string Path { get; set; } = null!;
        /// <summary>File or directory name without path prefix.</summary>
        public string Name { get; set; } = null!;
        /// <summary>"file" or "directory".</summary>
        public string Kind { get; set; } = null!;
        /// <summary>Depth relative to the requested root (0 = immediate child).</summary>
        public int Depth { get; set; }
        /// <summary>True when the directory has children; null for files.</summary>
        public bool? HasChildren { get; set; }
        /// <summary>File size in bytes; null for directories.</summary>
        public long? SizeBytes { get; set; }
    }

    // ── repo_browser.grep ────────────────────────────────────────────────────────

    /// <summary>Response from the repo_browser.grep tool.</summary>
    public sealed class RepoBrowserGrepResponse
    {
        /// <summary>Absolute path of the repository root on the server.</summary>
        public string RootPath { get; set; } = null!;
        /// <summary>The query string that was searched.</summary>
        public string Query { get; set; } = null!;
        /// <summary>Search mode that was used.</summary>
        public string Mode { get; set; } = null!;
        /// <summary>Total number of match lines returned.</summary>
        public int MatchCount { get; set; }
        /// <summary>True when results were capped at the requested limits.</summary>
        public bool Truncated { get; set; }
        /// <summary>Flat list of individual match lines across all files.</summary>
        public List<RepoBrowserGrepMatch> Items { get; set; } = new List<RepoBrowserGrepMatch>();
    }

    /// <summary>A single line match from a repo_browser.grep call.</summary>
    public sealed class RepoBrowserGrepMatch
    {
        /// <summary>Repository-relative file path.</summary>
        public string FilePath { get; set; } = null!;
        /// <summary>1-based line number of the match.</summary>
        public int LineNumber { get; set; }
        /// <summary>0-based column of the match start; null when not available.</summary>
        public int? ColumnStart { get; set; }
        /// <summary>0-based column of the match end; null when not available.</summary>
        public int? ColumnEnd { get; set; }
        /// <summary>Full text of the matching line.</summary>
        public string LineText { get; set; } = null!;
        /// <summary>Context lines immediately before the match.</summary>
        public List<string> BeforeContext { get; set; } = new List<string>();
        /// <summary>Context lines immediately after the match.</summary>
        public List<string> AfterContext { get; set; } = new List<string>();
    }

    // ── repo_browser.read_file ───────────────────────────────────────────────────

    /// <summary>Response from the repo_browser.read_file tool.</summary>
    public sealed class RepoBrowserReadFileResponse
    {
        /// <summary>Absolute path of the repository root on the server.</summary>
        public string RootPath { get; set; } = null!;
        /// <summary>Repository-relative file path that was requested.</summary>
        public string FilePath { get; set; } = null!;
        /// <summary>True when the file exists and was read successfully.</summary>
        public bool Exists { get; set; }
        /// <summary>Detected character encoding (e.g. "utf-8"); null when not available.</summary>
        public string? Encoding { get; set; }
        /// <summary>File size in bytes; null when not available.</summary>
        public long? SizeBytes { get; set; }
        /// <summary>True when the file was larger than the read limit and content was cut.</summary>
        public bool Truncated { get; set; }
        /// <summary>Actual 1-based start line returned; null for full-file reads.</summary>
        public int? StartLine { get; set; }
        /// <summary>Actual 1-based end line returned; null for full-file reads.</summary>
        public int? EndLine { get; set; }
        /// <summary>File content, optionally prefixed with line numbers.</summary>
        public string Content { get; set; } = null!;
    }

    // ── repo_browser.find_path ───────────────────────────────────────────────────

    /// <summary>Response from the repo_browser.find_path tool.</summary>
    public sealed class RepoBrowserFindPathResponse
    {
        /// <summary>Absolute path of the repository root on the server.</summary>
        public string RootPath { get; set; } = null!;
        /// <summary>The query that was matched against.</summary>
        public string Query { get; set; } = null!;
        /// <summary>Match mode that was used.</summary>
        public string MatchMode { get; set; } = null!;
        /// <summary>Target kind filter that was applied.</summary>
        public string? TargetKind { get; set; }
        /// <summary>Total number of results returned.</summary>
        public int ResultCount { get; set; }
        /// <summary>True when results were capped at maxResults.</summary>
        public bool Truncated { get; set; }
        /// <summary>Matched file and directory entries.</summary>
        public List<RepoBrowserPathMatch> Items { get; set; } = new List<RepoBrowserPathMatch>();
    }

    /// <summary>A single result entry from a repo_browser.find_path call.</summary>
    public sealed class RepoBrowserPathMatch
    {
        /// <summary>Repository-relative path of the entry.</summary>
        public string Path { get; set; } = null!;
        /// <summary>File or directory name without path prefix.</summary>
        public string Name { get; set; } = null!;
        /// <summary>"file" or "directory".</summary>
        public string Kind { get; set; } = null!;
        /// <summary>Human-readable description of why this entry matched.</summary>
        public string? MatchReason { get; set; }
    }

    /// <summary>Response from a repo_browser.search call.</summary>
    public sealed class RepoBrowserSearchResponse
    {
        /// <summary>Absolute path of the repository root on the server.</summary>
        public string RootPath { get; set; } = null!;
        /// <summary>The filename substring that was searched for.</summary>
        public string Query { get; set; } = null!;
        /// <summary>Subdirectory the search was restricted to (empty = whole repo).</summary>
        public string? PathPrefix { get; set; }
        /// <summary>Total number of results returned.</summary>
        public int ResultCount { get; set; }
        /// <summary>True when results were capped at max_results.</summary>
        public bool Truncated { get; set; }
        /// <summary>Matched file entries.</summary>
        public List<RepoBrowserSearchMatch> Items { get; set; } = new List<RepoBrowserSearchMatch>();
    }

    /// <summary>A single file result from a repo_browser.search call.</summary>
    public sealed class RepoBrowserSearchMatch
    {
        /// <summary>Repository-relative path of the file.</summary>
        public string Path { get; set; } = null!;
        /// <summary>Filename without path prefix.</summary>
        public string Name { get; set; } = null!;
        /// <summary>Always "file".</summary>
        public string Kind { get; set; } = "file";
        /// <summary>File size in bytes.</summary>
        public long SizeBytes { get; set; }
    }
}

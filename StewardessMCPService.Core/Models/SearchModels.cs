using System.Collections.Generic;

namespace StewardessMCPService.Models
{
    // ────────────────────────────────────────────────────────────────────────────
    //  Search request base
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Common parameters shared by all search requests.</summary>
    public abstract class SearchRequestBase
    {
        /// <summary>Restrict search to this subdirectory (relative path; empty = whole repo).</summary>
        public string SearchPath { get; set; } = "";

        /// <summary>Restrict to these file extensions (e.g. ".cs", ".json").  Empty = all.</summary>
        public List<string> Extensions { get; set; }

        /// <summary>Maximum number of results to return.  Capped at server limit.</summary>
        public int MaxResults { get; set; } = 100;

        /// <summary>When true, results are sorted by file path.</summary>
        public bool SortByPath { get; set; } = true;

        /// <summary>Number of context lines to include before each match.</summary>
        public int ContextLinesBefore { get; set; } = 2;

        /// <summary>Number of context lines to include after each match.</summary>
        public int ContextLinesAfter { get; set; } = 2;
    }

    // ── search_text ──────────────────────────────────────────────────────────────

    /// <summary>Request to search for literal text across repository files.</summary>
    public sealed class SearchTextRequest : SearchRequestBase
    {
        /// <summary>Literal text to search for.</summary>
        public string Query { get; set; }

        /// <summary>Case-insensitive search (default true).</summary>
        public bool IgnoreCase { get; set; } = true;

        /// <summary>Match whole words only.</summary>
        public bool WholeWord { get; set; } = false;
    }

    // ── search_regex ─────────────────────────────────────────────────────────────

    /// <summary>Request to search repository files using a .NET regular expression.</summary>
    public sealed class SearchRegexRequest : SearchRequestBase
    {
        /// <summary>.NET regular expression pattern.</summary>
        public string Pattern { get; set; }

        /// <summary>Case-insensitive (default true).</summary>
        public bool IgnoreCase { get; set; } = true;

        /// <summary>Apply the pattern in multiline mode (^ and $ match line boundaries).</summary>
        public bool Multiline { get; set; } = true;
    }

    // ── search_file_names ────────────────────────────────────────────────────────

    /// <summary>Request to search for files by name pattern.</summary>
    public sealed class SearchFileNamesRequest
    {
        /// <summary>Name substring or wildcard pattern (*, ?) to match against file names.</summary>
        public string Pattern { get; set; }

        /// <summary>Sub-directory to search in; empty = repository root.</summary>
        public string SearchPath { get; set; } = "";

        /// <summary>Maximum results.</summary>
        public int MaxResults { get; set; } = 100;

        /// <summary>When true, performs a case-insensitive name comparison.</summary>
        public bool IgnoreCase { get; set; } = true;

        /// <summary>When true, search full relative path instead of name only.</summary>
        public bool MatchFullPath { get; set; } = false;

        /// <summary>When true, treats <see cref="Pattern"/> as a .NET regular expression.</summary>
        public bool UseRegex { get; set; } = false;
    }

    // ── search_by_extension ──────────────────────────────────────────────────────

    /// <summary>Request to find all files with specified extensions.</summary>
    public sealed class SearchByExtensionRequest
    {
        /// <summary>Extensions to find (include the leading dot, e.g. ".cs").</summary>
        public List<string> Extensions { get; set; } = new List<string>();

        /// <summary>Sub-directory to search in; empty = repository root.</summary>
        public string SearchPath { get; set; } = "";
        /// <summary>Maximum number of results to return.</summary>
        public int MaxResults { get; set; } = 200;
    }

    // ── search_symbol_like ───────────────────────────────────────────────────────

    /// <summary>
    /// Best-effort symbol search using text heuristics.
    /// Finds class names, method signatures, interface names, etc.
    /// </summary>
    public sealed class SearchSymbolRequest : SearchRequestBase
    {
        /// <summary>Symbol name or partial name to search for.</summary>
        public string SymbolName { get; set; }

        /// <summary>Symbol kind filter: "class", "interface", "method", "property", "field", "" (all).</summary>
        public string SymbolKind { get; set; } = "";

        /// <summary>When true, performs a case-insensitive name comparison.</summary>
        public bool IgnoreCase { get; set; } = true;
    }

    // ── find_references_like ─────────────────────────────────────────────────────

    /// <summary>Request to find textual usages of an identifier across the repository.</summary>
    public sealed class FindReferencesRequest : SearchRequestBase
    {
        /// <summary>Identifier name whose usages (textual references) should be found.</summary>
        public string IdentifierName { get; set; }

        /// <summary>When true, performs a case-insensitive comparison.</summary>
        public bool IgnoreCase { get; set; } = false;
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Search results
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Aggregated search results.</summary>
    public sealed class SearchResponse
    {
        /// <summary>All file-level match groups.</summary>
        public List<FileSearchResult> Files { get; set; } = new List<FileSearchResult>();

        /// <summary>Total number of individual match lines across all files.</summary>
        public int TotalMatchCount { get; set; }

        /// <summary>Number of files that contained at least one match.</summary>
        public int FilesWithMatchesCount { get; set; }

        /// <summary>True when the result was capped at the server limit.</summary>
        public bool Truncated { get; set; }

        /// <summary>The effective query used after normalisation.</summary>
        public string EffectiveQuery { get; set; }

        /// <summary>Search duration in milliseconds.</summary>
        public long ElapsedMs { get; set; }

        /// <summary>Current page number (1-based).</summary>
        public int Page { get; set; } = 1;

        /// <summary>Number of file results per page.</summary>
        public int PageSize { get; set; }

        /// <summary>Total number of file results across all pages.</summary>
        public int TotalItems { get; set; }

        /// <summary>True when there are additional pages of results.</summary>
        public bool HasMore { get; set; }
    }

    /// <summary>Matches within a single file.</summary>
    public sealed class FileSearchResult
    {
        /// <summary>Repository-relative path of the file.</summary>
        public string RelativePath { get; set; }
        /// <summary>File name without path.</summary>
        public string FileName { get; set; }
        /// <summary>Number of individual matches within this file.</summary>
        public int MatchCount { get; set; }
        /// <summary>Per-line match details.</summary>
        public List<SearchMatch> Matches { get; set; } = new List<SearchMatch>();
    }

    /// <summary>A single match within a file.</summary>
    public sealed class SearchMatch
    {
        /// <summary>1-based line number of the match.</summary>
        public int LineNumber { get; set; }

        /// <summary>0-based column of the match start.</summary>
        public int Column { get; set; }

        /// <summary>The matching text fragment.</summary>
        public string MatchText { get; set; }

        /// <summary>Full content of the matching line.</summary>
        public string LineText { get; set; }

        /// <summary>Context lines before the match.</summary>
        public List<string> ContextBefore { get; set; } = new List<string>();

        /// <summary>Context lines after the match.</summary>
        public List<string> ContextAfter { get; set; } = new List<string>();
    }

    // ── simple file-name search result ──────────────────────────────────────────

    /// <summary>Response containing file-name search matches.</summary>
    public sealed class FileNameSearchResponse
    {
        /// <summary>Matched file entries.</summary>
        public List<FileNameMatch> Matches { get; set; } = new List<FileNameMatch>();
        /// <summary>Total number of matches (may exceed the returned list when truncated).</summary>
        public int TotalCount { get; set; }
        /// <summary>True when results were capped at the server limit.</summary>
        public bool Truncated { get; set; }
        /// <summary>Search duration in milliseconds.</summary>
        public long ElapsedMs { get; set; }

        /// <summary>Current page number (1-based).</summary>
        public int Page { get; set; } = 1;

        /// <summary>Number of matches per page.</summary>
        public int PageSize { get; set; }

        /// <summary>True when there are additional pages of results.</summary>
        public bool HasMore { get; set; }
    }

    /// <summary>A single file-name match.</summary>
    public sealed class FileNameMatch
    {
        /// <summary>Repository-relative path of the matched file.</summary>
        public string RelativePath { get; set; }
        /// <summary>File name without path.</summary>
        public string Name { get; set; }
        /// <summary>File extension including the leading dot.</summary>
        public string Extension { get; set; }
        /// <summary>File size in bytes.</summary>
        public long SizeBytes { get; set; }
    }
}

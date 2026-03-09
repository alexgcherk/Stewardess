using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StewardessMCPService.CodeIndexing.Indexing;
using StewardessMCPService.CodeIndexing.Query;
using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Models;
using StewardessMCPService.Services;

namespace StewardessMCPService.Mcp
{
    /// <summary>
    /// Registers all MCP tools and maps each tool name to a typed handler delegate.
    /// The handler receives the raw arguments dictionary from the JSON-RPC call,
    /// converts them to the appropriate request object, invokes the service, and
    /// returns a <see cref="McpToolCallResult"/>.
    /// </summary>
    public sealed class McpToolRegistry
    {
        // ── Internal entry type ──────────────────────────────────────────────────

        internal sealed class ToolEntry
        {
            public McpToolDefinition Definition { get; set; }
            public Func<Dictionary<string, object>, CancellationToken, Task<McpToolCallResult>> Handler { get; set; }
        }

        // ── Fields ───────────────────────────────────────────────────────────────

        private readonly Dictionary<string, ToolEntry> _tools =
            new Dictionary<string, ToolEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly McpServiceSettings  _settings;
        private readonly IFileSystemService  _files;
        private readonly ISearchService      _search;
        private readonly IEditService        _edit;
        private readonly IGitService         _git;
        private readonly ICommandService     _command;
        private readonly IIndexingEngine?    _indexer;
        private readonly IIndexQueryService? _indexQuery;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>Initialises a new instance of <see cref="McpToolRegistry"/>.</summary>
        public McpToolRegistry(
            McpServiceSettings settings,
            IFileSystemService files,
            ISearchService     search,
            IEditService       edit,
            IGitService        git,
            ICommandService    command,
            IIndexingEngine?    indexer = null,
            IIndexQueryService? indexQuery = null)
        {
            _settings   = settings   ?? throw new ArgumentNullException(nameof(settings));
            _files      = files      ?? throw new ArgumentNullException(nameof(files));
            _search     = search     ?? throw new ArgumentNullException(nameof(search));
            _edit       = edit       ?? throw new ArgumentNullException(nameof(edit));
            _git        = git        ?? throw new ArgumentNullException(nameof(git));
            _command    = command    ?? throw new ArgumentNullException(nameof(command));
            _indexer    = indexer;
            _indexQuery = indexQuery;

            RegisterAll();
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>Returns all registered tool definitions.</summary>
        public IReadOnlyList<McpToolDefinition> GetAllDefinitions()
            => _tools.Values.Select(e => e.Definition).ToList();

        /// <summary>Returns true and the definition when a tool with the given name exists.</summary>
        public bool TryGetDefinition(string name, out McpToolDefinition definition)
        {
            definition = null;
            if (_tools.TryGetValue(name, out var entry))
            {
                definition = entry.Definition;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Invokes the named tool with the provided arguments.
        /// Returns a <see cref="McpToolCallResult"/> containing the serialized response
        /// or an error content block.
        /// </summary>
        public async Task<McpToolCallResult> InvokeAsync(
            string name,
            Dictionary<string, object> arguments,
            CancellationToken ct = default)
        {
            if (!_tools.TryGetValue(name, out var entry))
                throw new KeyNotFoundException($"Tool not found: {name}");

            return await entry.Handler(arguments ?? new Dictionary<string, object>(), ct)
                               .ConfigureAwait(false);
        }

        // ── Registration ─────────────────────────────────────────────────────────

        private void RegisterAll()
        {
            RegisterRepositoryTools();
            RegisterSearchTools();
            RegisterReadTools();
            RegisterWriteTools();
            RegisterGitTools();
            RegisterCommandTools();
            RegisterCodeIndexTools();
            RegisterRepoBrowserTools();
            AnnotateAllTools();
        }

        // ── Repository / navigation tools ────────────────────────────────────────

        private void RegisterRepositoryTools()
        {
            Add("get_repository_info",
                category: "repository",
                description: "Returns high-level information about the repository: name, root, file counts, and allowed policy.",
                schema: Schema(),
                handler: async (args, ct) =>
                {
                    var result = await _files.GetRepositoryInfoAsync(ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("list_directory",
                category: "repository",
                description: "Lists immediate children (files and sub-directories) of the given directory.",
                schema: Schema(
                    Prop("path",           "string",  "Relative path to the directory (empty = repo root)."),
                    Prop("include_blocked","boolean", "Include blocked folders in the listing (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = new ListDirectoryRequest
                    {
                        Path           = Str(args, "path", ""),
                        IncludeBlocked = Bool(args, "include_blocked", false)
                    };
                    var result = await _files.ListDirectoryAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("list_tree",
                category: "repository",
                description: "Returns a recursive directory tree up to a configurable depth.",
                schema: Schema(
                    Prop("path",      "string",  "Root path of the tree (empty = repo root)."),
                    Prop("max_depth", "integer", "Maximum recursion depth (1-10, default 3).", def: 3)),
                handler: async (args, ct) =>
                {
                    var req = new ListTreeRequest
                    {
                        Path     = Str(args, "path", ""),
                        MaxDepth = Int(args, "max_depth", 3)
                    };
                    var result = await _files.ListTreeAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("path_exists",
                category: "repository",
                description: "Checks whether a path (file or directory) exists in the repository.",
                schema: Schema(
                    Prop("path", "string", "Relative path to check.", required: true)),
                handler: async (args, ct) =>
                {
                    var path   = Str(args, "path");
                    var result = await _files.PathExistsAsync(path, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("get_metadata",
                category: "repository",
                description: "Returns detailed metadata (size, dates, encoding, line count) for a file or directory.",
                schema: Schema(
                    Prop("path", "string", "Relative path of the file or directory.", required: true)),
                handler: async (args, ct) =>
                {
                    var req    = new FileMetadataRequest { Path = Str(args, "path") };
                    var result = await _files.GetMetadataAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("detect_encoding",
                category: "repository",
                description: "Detects the character encoding of a file (UTF-8, UTF-16, ASCII, etc.).",
                schema: Schema(
                    Prop("path", "string", "Relative file path.", required: true)),
                handler: async (args, ct) =>
                {
                    var enc = await _files.DetectEncodingAsync(Str(args, "path"), ct).ConfigureAwait(false);
                    return ToResult(new { Encoding = enc });
                });
        }

        // ── Search tools ─────────────────────────────────────────────────────────

        private void RegisterSearchTools()
        {
            Add("search_text",
                category: "search",
                description: "Searches for a literal text string across repository files. Returns file paths, line numbers, and match excerpts.",
                schema: Schema(
                    Prop("query",       "string",  "Literal text to search for.", required: true),
                    Prop("search_path", "string",  "Restrict search to this subdirectory (empty = whole repo)."),
                    Prop("extensions",  "array",   "File extensions to include, e.g. [\".cs\",\".json\"]. Empty = all."),
                    Prop("ignore_case", "boolean", "Case-insensitive search (default true).", def: true),
                    Prop("whole_word",  "boolean", "Match whole words only (default false).", def: false),
                    Prop("max_results", "integer", "Max results to return (default 100).", def: 100),
                    Prop("page",        "integer", "Page number (1-based, default 1).", def: 1),
                    Prop("page_size",   "integer", "Results per page (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var req = new SearchTextRequest
                    {
                        Query      = Str(args, "query"),
                        SearchPath = Str(args, "search_path", ""),
                        Extensions = StrList(args, "extensions"),
                        IgnoreCase = Bool(args, "ignore_case", true),
                        WholeWord  = Bool(args, "whole_word", false),
                        MaxResults = Int(args, "max_results", 100)
                    };
                    var result = await _search.SearchTextAsync(req, ct).ConfigureAwait(false);
                    var page = Math.Max(1, Int(args, "page", 1));
                    var pageSize = Int(args, "page_size", 50);
                    ApplyPagination(result, page, pageSize);
                    return ToResult(result);
                });

            Add("search_regex",
                category: "search",
                description: "Searches using a .NET regular expression pattern.",
                schema: Schema(
                    Prop("pattern",     "string",  ".NET regex pattern.", required: true),
                    Prop("search_path", "string",  "Restrict to subdirectory."),
                    Prop("extensions",  "array",   "File extensions to include."),
                    Prop("ignore_case", "boolean", "Case-insensitive (default true).", def: true),
                    Prop("max_results", "integer", "Max results (default 100).", def: 100),
                    Prop("page",        "integer", "Page number (1-based, default 1).", def: 1),
                    Prop("page_size",   "integer", "Results per page (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var req = new SearchRegexRequest
                    {
                        Pattern    = Str(args, "pattern"),
                        SearchPath = Str(args, "search_path", ""),
                        Extensions = StrList(args, "extensions"),
                        IgnoreCase = Bool(args, "ignore_case", true),
                        MaxResults = Int(args, "max_results", 100)
                    };
                    var result = await _search.SearchRegexAsync(req, ct).ConfigureAwait(false);
                    var page = Math.Max(1, Int(args, "page", 1));
                    var pageSize = Int(args, "page_size", 50);
                    ApplyPagination(result, page, pageSize);
                    return ToResult(result);
                });

            Add("search_file_names",
                category: "search",
                description: "Finds files whose names match a substring or wildcard pattern.",
                schema: Schema(
                    Prop("pattern",     "string", "Filename pattern (substring or glob).", required: true),
                    Prop("search_path", "string", "Restrict to subdirectory."),
                    Prop("max_results", "integer", "Max results (default 100).", def: 100),
                    Prop("page",        "integer", "Page number (1-based, default 1).", def: 1),
                    Prop("page_size",   "integer", "Results per page (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var req = new SearchFileNamesRequest
                    {
                        Pattern    = Str(args, "pattern"),
                        SearchPath = Str(args, "search_path", ""),
                        MaxResults = Int(args, "max_results", 100)
                    };
                    var result = await _search.SearchFileNamesAsync(req, ct).ConfigureAwait(false);
                    var page = Math.Max(1, Int(args, "page", 1));
                    var pageSize = Int(args, "page_size", 50);
                    ApplyFileNamePagination(result, page, pageSize);
                    return ToResult(result);
                });

            Add("search_by_extension",
                category: "search",
                description: "Returns all files matching one or more file extensions.",
                schema: Schema(
                    Prop("extensions",  "array",  "Extensions to find, e.g. [\".cs\",\".js\"].", required: true),
                    Prop("search_path", "string", "Restrict to subdirectory."),
                    Prop("max_results", "integer","Max results (default 200).", def: 200),
                    Prop("page",        "integer", "Page number (1-based, default 1).", def: 1),
                    Prop("page_size",   "integer", "Results per page (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var req = new SearchByExtensionRequest
                    {
                        Extensions = StrList(args, "extensions"),
                        SearchPath = Str(args, "search_path", ""),
                        MaxResults = Int(args, "max_results", 200)
                    };
                    var result = await _search.SearchByExtensionAsync(req, ct).ConfigureAwait(false);
                    var page = Math.Max(1, Int(args, "page", 1));
                    var pageSize = Int(args, "page_size", 50);
                    ApplyFileNamePagination(result, page, pageSize);
                    return ToResult(result);
                });

            Add("search_symbol",
                category: "search",
                description: "Best-effort symbol search using text heuristics (class, method, interface declarations).",
                schema: Schema(
                    Prop("symbol_name", "string",  "Symbol name to search for.", required: true),
                    Prop("symbol_kind", "string",  "Optional symbol kind filter.", enums: new[] { "class", "interface", "method", "property", "enum", "field", "constructor", "event", "delegate", "namespace" }),
                    Prop("search_path", "string",  "Restrict to subdirectory."),
                    Prop("extensions",  "array",   "File extensions."),
                    Prop("max_results", "integer", "Max results (default 50).", def: 50)),
                handler: async (args, ct) =>
                {
                    var req = new SearchSymbolRequest
                    {
                        SymbolName = Str(args, "symbol_name"),
                        SymbolKind = Str(args, "symbol_kind", ""),
                        SearchPath = Str(args, "search_path", ""),
                        Extensions = StrList(args, "extensions"),
                        MaxResults = Int(args, "max_results", 50)
                    };
                    var result = await _search.SearchSymbolAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("find_references",
                category: "search",
                description: "Best-effort reference search — finds textual usages of an identifier across all files.",
                schema: Schema(
                    Prop("identifier",  "string",  "Identifier to look for.", required: true),
                    Prop("search_path", "string",  "Restrict to subdirectory."),
                    Prop("extensions",  "array",   "File extensions."),
                    Prop("max_results", "integer", "Max results (default 100).", def: 100)),
                handler: async (args, ct) =>
                {
                    var req = new FindReferencesRequest
                    {
                        IdentifierName = Str(args, "identifier"),
                        SearchPath     = Str(args, "search_path", ""),
                        Extensions     = StrList(args, "extensions"),
                        MaxResults     = Int(args, "max_results", 100)
                    };
                    var result = await _search.FindReferencesAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });
        }

        // ── Read tools ───────────────────────────────────────────────────────────

        private void RegisterReadTools()
        {
            Add("read_file",
                category: "files",
                description: "Reads the full content of a file. Large files are automatically truncated at the server limit.",
                schema: Schema(
                    Prop("path",        "string",  "Relative file path.", required: true),
                    Prop("max_bytes",   "integer", "Maximum bytes to return (server limit applies)."),
                    Prop("return_base64","boolean","Return base64-encoded content instead of text (for binary files).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = new ReadFileRequest
                    {
                        Path         = Str(args, "path"),
                        MaxBytes     = NullableInt(args, "max_bytes"),
                        ReturnBase64 = Bool(args, "return_base64", false)
                    };
                    var result = await _files.ReadFileAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("read_file_range",
                category: "files",
                description: "Reads a specific range of lines from a file.",
                schema: Schema(
                    Prop("path",       "string",  "Relative file path.", required: true),
                    Prop("start_line", "integer", "First line (1-based, inclusive).", required: true),
                    Prop("end_line",   "integer", "Last line (1-based, inclusive).", required: true)),
                handler: async (args, ct) =>
                {
                    var req = new ReadFileRangeRequest
                    {
                        Path      = Str(args, "path"),
                        StartLine = Int(args, "start_line", 1),
                        EndLine   = Int(args, "end_line", 1)
                    };
                    var result = await _files.ReadFileRangeAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("read_multiple_files",
                category: "files",
                description: "Reads multiple files in a single call. Returns contents and any per-file errors.",
                schema: Schema(
                    Prop("paths", "array", "Array of relative file paths to read.", required: true)),
                handler: async (args, ct) =>
                {
                    var req = new ReadMultipleFilesRequest
                    {
                        Paths = StrList(args, "paths")
                    };
                    var result = await _files.ReadMultipleFilesAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("get_file_hash",
                category: "files",
                description: "Computes a hash digest (SHA-256 by default) of a file's contents.",
                schema: Schema(
                    Prop("path",      "string", "Relative file path.", required: true),
                    Prop("algorithm", "string", "Hash algorithm: SHA256 (default), MD5, SHA1.", enums: new[] { "MD5", "SHA1", "SHA256" })),
                handler: async (args, ct) =>
                {
                    var req = new FileHashRequest
                    {
                        Path      = Str(args, "path"),
                        Algorithm = Str(args, "algorithm", "SHA256")
                    };
                    var result = await _files.GetFileHashAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("get_file_structure",
                category: "code-intelligence",
                description: "Returns a structural summary of a code file: namespaces, types, and methods (parsed via text heuristics).",
                schema: Schema(
                    Prop("path", "string", "Relative file path.", required: true)),
                handler: async (args, ct) =>
                {
                    var req    = new FileStructureSummaryRequest { Path = Str(args, "path") };
                    var result = await _files.GetFileStructureSummaryAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });
        }

        // ── Write / edit tools ───────────────────────────────────────────────────

        private void RegisterWriteTools()
        {
            bool ro = _settings.ReadOnlyMode;

            Add("write_file",
                category: "edit",
                description: "Overwrites (or creates) a file with new content. Supports dry-run and backup.",
                isDestructive: true,
                isDisabled: ro,
                disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("path",         "string",  "Relative file path.", required: true),
                    Prop("content",      "string",  "New full content of the file.", required: true),
                    Prop("encoding",     "string",  "Encoding: utf-8 (default), utf-8-bom, utf-16, ascii.", enums: new[] { "utf-8", "utf-8-bom", "utf-16", "ascii" }),
                    Prop("dry_run",      "boolean", "Simulate without writing (default false).", def: false),
                    Prop("create_backup","boolean", "Create backup before overwriting (default true).", def: true),
                    Prop("change_reason","string",  "Agent-supplied reason for the change (audit log).")),
                handler: async (args, ct) =>
                {
                    var req = new WriteFileRequest
                    {
                        Path    = Str(args, "path"),
                        Content = Str(args, "content"),
                        Encoding = Str(args, "encoding", "utf-8"),
                        Options = EditOpts(args)
                    };
                    var result = await _edit.WriteFileAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("create_file",
                category: "edit",
                description: "Creates a new file. Fails by default if the file already exists.",
                isDestructive: true, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("path",      "string",  "Relative file path.", required: true),
                    Prop("content",   "string",  "Initial content (default empty)."),
                    Prop("overwrite", "boolean", "Overwrite if it already exists (default false).", def: false),
                    Prop("dry_run",   "boolean", "Simulate without writing (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = new CreateFileRequest
                    {
                        Path      = Str(args, "path"),
                        Content   = Str(args, "content", ""),
                        Overwrite = Bool(args, "overwrite", false),
                        Options   = EditOpts(args)
                    };
                    var result = await _edit.CreateFileAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("create_directory",
                category: "edit",
                description: "Creates a directory and optionally all parent directories.",
                isDestructive: false, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("path",           "string",  "Relative directory path.", required: true),
                    Prop("create_parents", "boolean", "Create missing parent directories (default true).", def: true),
                    Prop("dry_run",        "boolean", "Simulate (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = new CreateDirectoryRequest
                    {
                        Path          = Str(args, "path"),
                        CreateParents = Bool(args, "create_parents", true),
                        Options       = EditOpts(args)
                    };
                    var result = await _edit.CreateDirectoryAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("rename_path",
                category: "edit",
                description: "Renames a file or directory in place (new name, same parent directory).",
                isDestructive: true, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("path",     "string", "Relative path of the item to rename.", required: true),
                    Prop("new_name", "string", "New name (filename only, no path separator).", required: true),
                    Prop("dry_run",  "boolean","Simulate (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = new RenamePathRequest
                    {
                        Path    = Str(args, "path"),
                        NewName = Str(args, "new_name"),
                        Options = EditOpts(args)
                    };
                    var result = await _edit.RenamePathAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("move_path",
                category: "edit",
                description: "Moves a file or directory to a new location within the repository.",
                isDestructive: true, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("source_path",      "string",  "Relative source path.", required: true),
                    Prop("destination_path", "string",  "Relative destination path.", required: true),
                    Prop("overwrite",        "boolean", "Overwrite destination if it exists (default false).", def: false),
                    Prop("dry_run",          "boolean", "Simulate (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = new MovePathRequest
                    {
                        SourcePath      = Str(args, "source_path"),
                        DestinationPath = Str(args, "destination_path"),
                        Overwrite       = Bool(args, "overwrite", false),
                        Options         = EditOpts(args)
                    };
                    var result = await _edit.MovePathAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("delete_file",
                category: "edit",
                description: "Deletes a file. Creates a backup first by default (rollback is possible).",
                isDestructive: true, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("path",         "string",  "Relative file path.", required: true),
                    Prop("create_backup","boolean", "Create backup for rollback (default true).", def: true),
                    Prop("dry_run",      "boolean", "Simulate (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = new DeleteFileRequest
                    {
                        Path    = Str(args, "path"),
                        Options = EditOpts(args)
                    };
                    var result = await _edit.DeleteFileAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("delete_directory",
                category: "edit",
                description: "Deletes a directory. Requires recursive=true to delete non-empty directories.",
                isDestructive: true, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("path",      "string",  "Relative directory path.", required: true),
                    Prop("recursive", "boolean", "Delete non-empty directories (default false).", def: false),
                    Prop("dry_run",   "boolean", "Simulate (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = new DeleteDirectoryRequest
                    {
                        Path      = Str(args, "path"),
                        Recursive = Bool(args, "recursive", false),
                        Options   = EditOpts(args)
                    };
                    var result = await _edit.DeleteDirectoryAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("append_file",
                category: "edit",
                description: "Appends content to the end of an existing file.",
                isDestructive: true, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("path",    "string", "Relative file path.", required: true),
                    Prop("content", "string", "Content to append.", required: true),
                    Prop("dry_run", "boolean","Simulate (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = new AppendFileRequest
                    {
                        Path    = Str(args, "path"),
                        Content = Str(args, "content"),
                        Options = EditOpts(args)
                    };
                    var result = await _edit.AppendFileAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("replace_text",
                category: "edit",
                description: "Replaces all (or a limited number of) occurrences of a literal string in a file.",
                isDestructive: true, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("path",         "string",  "Relative file path.", required: true),
                    Prop("old_text",     "string",  "Text to replace.", required: true),
                    Prop("new_text",     "string",  "Replacement text.", required: true),
                    Prop("max_replacements","integer","Max occurrences to replace (0 = all, default 0).", def: 0),
                    Prop("ignore_case",  "boolean", "Case-insensitive match (default false).", def: false),
                    Prop("dry_run",      "boolean", "Simulate (default false).", def: false),
                    Prop("create_backup","boolean", "Create backup (default true).", def: true)),
                handler: async (args, ct) =>
                {
                    var req = new ReplaceTextRequest
                    {
                        Path            = Str(args, "path"),
                        OldText         = Str(args, "old_text"),
                        NewText         = Str(args, "new_text"),
                        MaxReplacements = Int(args, "max_replacements", 0),
                        IgnoreCase      = Bool(args, "ignore_case", false),
                        Options         = EditOpts(args)
                    };
                    var result = await _edit.ReplaceTextAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("replace_lines",
                category: "edit",
                description: "Replaces a contiguous range of lines in a file with new content.",
                isDestructive: true, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("path",        "string",  "Relative file path.", required: true),
                    Prop("start_line",  "integer", "First line to replace (1-based, inclusive).", required: true),
                    Prop("end_line",    "integer", "Last line to replace (1-based, inclusive).", required: true),
                    Prop("new_content", "string",  "Replacement content.", required: true),
                    Prop("dry_run",     "boolean", "Simulate (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = new ReplaceLinesRequest
                    {
                        Path       = Str(args, "path"),
                        StartLine  = Int(args, "start_line", 1),
                        EndLine    = Int(args, "end_line", 1),
                        NewContent = Str(args, "new_content"),
                        Options    = EditOpts(args)
                    };
                    var result = await _edit.ReplaceLinesAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("patch_file",
                category: "edit",
                description: "Applies a unified diff patch to a single file.",
                isDestructive: true, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("path",        "string",  "Relative file path.", required: true),
                    Prop("patch",       "string",  "Unified diff patch text.", required: true),
                    Prop("fuzz_factor", "integer", "Lines of context fuzz (default 2).", def: 2),
                    Prop("dry_run",     "boolean", "Simulate (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = new PatchFileRequest
                    {
                        Path        = Str(args, "path"),
                        Patch       = Str(args, "patch"),
                        FuzzFactor  = Int(args, "fuzz_factor", 2),
                        Options     = EditOpts(args)
                    };
                    var result = await _edit.PatchFileAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("apply_diff",
                category: "edit",
                description: "Applies a multi-file unified diff in a single atomic operation.",
                isDestructive: true, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("diff",     "string",  "Full multi-file unified diff text.", required: true),
                    Prop("dry_run",  "boolean", "Simulate (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = new ApplyDiffRequest
                    {
                        Diff    = Str(args, "diff"),
                        Options = EditOpts(args)
                    };
                    var result = await _edit.ApplyDiffAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("batch_edit",
                category: "edit",
                description: "Executes multiple heterogeneous edits atomically. Rolls back all changes if any operation fails.",
                isDestructive: true, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    PropWithItems("edits", "Array of edit items. Each item must have 'operation' and 'arguments'.", required: true,
                        items: new
                        {
                            type = "object",
                            properties = new
                            {
                                operation = new
                                {
                                    type = "string",
                                    description = "The edit operation to perform.",
                                    @enum = new[] { "write_file", "replace_text", "replace_lines", "delete_file" }
                                },
                                arguments = new
                                {
                                    type = "object",
                                    description = "Operation-specific arguments (same as the corresponding single-file tool)."
                                }
                            },
                            required = new[] { "operation", "arguments" },
                            additionalProperties = false
                        }),
                    Prop("dry_run", "boolean", "Simulate the whole batch (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var req = FromArgs<BatchEditRequest>(args);
                    var result = await _edit.ApplyBatchEditsAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("preview_changes",
                category: "edit",
                description: "Dry-runs a batch of edits and returns a preview diff plus an approval token.",
                isDestructive: false, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("proposed_edits", "object", "BatchEditRequest containing the edits to preview.", required: true)),
                handler: async (args, ct) =>
                {
                    var req = FromArgs<PreviewChangesRequest>(args);
                    var result = await _edit.PreviewChangesAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("rollback",
                category: "edit",
                description: "Restores a file from a backup identified by a prior operation's rollback token.",
                isDestructive: true, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("rollback_token", "string", "Token returned by a previous write/delete operation.", required: true)),
                handler: async (args, ct) =>
                {
                    var req    = new RollbackRequest { RollbackToken = Str(args, "rollback_token") };
                    var result = await _edit.RollbackAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });
        }

        // ── Git tools ────────────────────────────────────────────────────────────

        private void RegisterGitTools()
        {
            Add("get_git_status",
                category: "git",
                description: "Returns the current git status: branch, HEAD commit, and per-file staged/unstaged/untracked changes.",
                schema: Schema(
                    Prop("path", "string", "Restrict status to this sub-path (empty = whole repo).")),
                handler: async (args, ct) =>
                {
                    var req    = new GitStatusRequest { Path = Str(args, "path", "") };
                    var result = await _git.GetStatusAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("get_git_diff",
                category: "git",
                description: "Returns the unified diff for working-tree or staged changes. Scope: unstaged (default), staged, head, or commit:sha1..sha2.",
                schema: Schema(
                    Prop("path",          "string",  "Restrict to this file or directory (empty = all)."),
                    Prop("scope",         "string",  "Diff scope: unstaged (default), staged, head.", def: "unstaged", enums: new[] { "unstaged", "staged", "head" }),
                    Prop("context_lines", "integer", "Context lines around each change (default 3).", def: 3)),
                handler: async (args, ct) =>
                {
                    var req    = new GitDiffRequest
                    {
                        Path         = Str(args, "path", ""),
                        Scope        = Str(args, "scope", "unstaged"),
                        ContextLines = Int(args, "context_lines", 3)
                    };
                    var result = await _git.GetDiffAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("get_git_diff_file",
                category: "git",
                description: "Returns the unified diff for a single file.",
                schema: Schema(
                    Prop("path",  "string", "Relative file path.", required: true),
                    Prop("scope", "string", "Diff scope: unstaged (default), staged, head.", def: "unstaged", enums: new[] { "unstaged", "staged", "head" })),
                handler: async (args, ct) =>
                {
                    var result = await _git.GetDiffForFileAsync(
                        Str(args, "path"),
                        Str(args, "scope", "unstaged"),
                        ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("get_git_log",
                category: "git",
                description: "Returns the commit history for the repository or a sub-path.",
                schema: Schema(
                    Prop("path",      "string",  "Restrict log to this sub-path (empty = whole repo)."),
                    Prop("max_count", "integer", "Maximum commits to return (default 20).", def: 20),
                    Prop("ref",       "string",  "Branch or commit ref to start from (empty = HEAD)."),
                    Prop("author",    "string",  "Filter by author name or email."),
                    Prop("since",     "string",  "ISO-8601 date — return only commits after this date."),
                    Prop("until",     "string",  "ISO-8601 date — return only commits before this date.")),
                handler: async (args, ct) =>
                {
                    var req    = new GitLogRequest
                    {
                        Path     = Str(args, "path", ""),
                        MaxCount = Int(args, "max_count", 20),
                        Ref      = Str(args, "ref", ""),
                        Author   = Str(args, "author", ""),
                        Since    = Str(args, "since", ""),
                        Until    = Str(args, "until", "")
                    };
                    var result = await _git.GetLogAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("get_commit",
                category: "git",
                description: "Returns the full details of a single commit by SHA: author, committer, message, changed files, and optionally the diff patch.",
                schema: Schema(
                    Prop("sha",          "string",  "Full or abbreviated commit SHA (minimum 4 hex chars).", required: true),
                    Prop("include_diff", "boolean", "Include the unified diff patch in the response (default: true).", def: true)),
                handler: async (args, ct) =>
                {
                    var req    = new GitShowRequest
                    {
                        Sha         = Str(args, "sha", ""),
                        IncludeDiff = Bool(args, "include_diff", true)
                    };
                    var result = await _git.GetCommitAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });
        }

        // ── Command tools ────────────────────────────────────────────────────────

        private void RegisterCommandTools()
        {
            bool ro = _settings.ReadOnlyMode;
            var allowedLabel = string.Join(", ", _settings.AllowedCommands.Take(5)) +
                               (_settings.AllowedCommands.Count > 5 ? "..." : "");

            Add("run_build",
                category: "command",
                description: "Runs the configured build command (dotnet build or msbuild). Only allowed commands are accepted.",
                isDestructive: false, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("build_command",    "string",  $"Build command to run. Allowed prefixes: {allowedLabel}"),
                    Prop("arguments",        "string",  "Additional CLI arguments."),
                    Prop("configuration",    "string",  "Build configuration: Debug (default) or Release.", def: "Debug", enums: new[] { "Debug", "Release" }),
                    Prop("working_directory","string",  "Working directory relative to repo root (empty = root)."),
                    Prop("timeout_seconds",  "integer", "Timeout override in seconds.")),
                handler: async (args, ct) =>
                {
                    var req = new RunBuildRequest
                    {
                        BuildCommand     = Str(args, "build_command", "dotnet build"),
                        Arguments        = Str(args, "arguments", ""),
                        Configuration    = Str(args, "configuration", "Debug"),
                        WorkingDirectory = Str(args, "working_directory", ""),
                        TimeoutSeconds   = NullableInt(args, "timeout_seconds")
                    };
                    var result = await _command.RunBuildAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("run_tests",
                category: "command",
                description: "Runs the configured test command (dotnet test). Only allowed commands are accepted.",
                isDestructive: false, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("test_command",     "string",  "Test command. Default: dotnet test."),
                    Prop("arguments",        "string",  "Additional CLI arguments."),
                    Prop("filter",           "string",  "Test filter expression."),
                    Prop("configuration",    "string",  "Configuration: Debug (default) or Release.", def: "Debug"),
                    Prop("working_directory","string",  "Working directory relative to repo root."),
                    Prop("timeout_seconds",  "integer", "Timeout override in seconds.")),
                handler: async (args, ct) =>
                {
                    var req = new RunTestsRequest
                    {
                        TestCommand      = Str(args, "test_command", "dotnet test"),
                        Arguments        = Str(args, "arguments", ""),
                        Filter           = Str(args, "filter", ""),
                        Configuration    = Str(args, "configuration", "Debug"),
                        WorkingDirectory = Str(args, "working_directory", ""),
                        TimeoutSeconds   = NullableInt(args, "timeout_seconds")
                    };
                    var result = await _command.RunTestsAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("run_command",
                category: "command",
                description: $"Runs a custom command from the AllowedCommands whitelist. Allowed prefixes: {allowedLabel}.",
                isDestructive: false, isDisabled: ro, disabledReason: ro ? "Read-only mode" : null,
                schema: Schema(
                    Prop("command",          "string",  "Full command line. Must start with an allowed prefix.", required: true),
                    Prop("working_directory","string",  "Working directory relative to repo root."),
                    Prop("timeout_seconds",  "integer", "Timeout override in seconds.")),
                handler: async (args, ct) =>
                {
                    var req = new RunCustomCommandRequest
                    {
                        Command          = Str(args, "command"),
                        WorkingDirectory = Str(args, "working_directory", ""),
                        TimeoutSeconds   = NullableInt(args, "timeout_seconds")
                    };
                    var result = await _command.RunCustomCommandAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });
        }

        // ── Code Index tools ─────────────────────────────────────────────────────

        private void RegisterCodeIndexTools()
        {
            if (_indexer == null || _indexQuery == null) return;

            var indexer = _indexer;
            var query   = _indexQuery;

            Add("code_index.build",
                category: "code_index",
                description: "Builds or rebuilds the structural code index for the given repository root. " +
                             "Enumerates all eligible files, detects languages, and parses declarations. " +
                             "Returns the resulting snapshot ID and statistics.",
                schema: Schema(
                    Prop("root_path",    "string",  "Absolute path to the repository root to index.", required: true),
                    Prop("parse_mode",   "string",  "Depth of parsing.", def: "Declarations",
                        enums: new[] { "OutlineOnly", "Declarations", "DeclarationsAndReferences" }),
                    Prop("force_rebuild","boolean", "Discard existing index and rebuild from scratch (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var modeStr = Str(args, "parse_mode", "Declarations");
                    var mode = Enum.TryParse<StewardessMCPService.CodeIndexing.Model.Structural.ParseMode>(modeStr, true, out var m)
                        ? m : StewardessMCPService.CodeIndexing.Model.Structural.ParseMode.Declarations;

                    var req = new IndexBuildRequest
                    {
                        RootPath     = Str(args, "root_path"),
                        ParseMode    = mode,
                        ForceRebuild = Bool(args, "force_rebuild", false),
                    };
                    var result = await indexer.BuildAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("code_index.get_status",
                category: "code_index",
                description: "[Deprecated: use code_index.get_index_status instead] Returns the current indexing state for a repository root.",
                schema: Schema(
                    Prop("root_path", "string", "Absolute repository root path.", required: true)),
                handler: async (args, ct) =>
                {
                    var result = await indexer.GetStatusAsync(Str(args, "root_path"), ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("code_index.list_files",
                category: "code_index",
                description: "Returns a paginated list of indexed files with their language, parse status, and node counts. " +
                             "Optionally filter by language, parse status, or path prefix.",
                schema: Schema(
                    Prop("root_path",     "string",  "Repository root path (used to resolve the latest snapshot)."),
                    Prop("snapshot_id",   "string",  "Specific snapshot ID to query (overrides root_path)."),
                    Prop("language",      "string",  "Filter to a single language ID (e.g. csharp, python)."),
                    Prop("path_prefix",   "string",  "Only return files under this path prefix."),
                    Prop("page",          "integer", "Page number (1-based, default 1).", def: 1),
                    Prop("page_size",     "integer", "Results per page (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var lang = Str(args, "language", null);
                    var req = new StewardessMCPService.CodeIndexing.Query.ListFilesRequest
                    {
                        SnapshotId   = Str(args, "snapshot_id", null),
                        RootPath     = Str(args, "root_path", null),
                        LanguageFilter = lang != null ? new[] { lang } : null,
                        PathPrefix   = Str(args, "path_prefix", null),
                        Page         = Int(args, "page", 1),
                        PageSize     = Math.Min(Int(args, "page_size", 50), 200),
                    };
                    var result = await query.ListFilesAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("code_index.get_file_outline",
                category: "code_index",
                description: "Returns the structural outline of a single file: namespaces, classes, methods, functions, and other declarations. " +
                             "Include source line spans for each node.",
                schema: Schema(
                    Prop("file_path",       "string",  "Repository-relative path to the file.", required: true),
                    Prop("snapshot_id",     "string",  "Specific snapshot ID to query."),
                    Prop("max_depth",       "integer", "Maximum nesting depth to return (0 = unlimited).", def: 0),
                    Prop("include_spans",   "boolean", "Include source line spans (default true).", def: true),
                    Prop("include_confidence", "boolean", "Include parser confidence scores (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var maxDepth = Int(args, "max_depth", 0);
                    var req = new StewardessMCPService.CodeIndexing.Query.GetFileOutlineRequest
                    {
                        FilePath       = Str(args, "file_path"),
                        SnapshotId     = Str(args, "snapshot_id", null),
                        MaxDepth       = maxDepth == 0 ? null : maxDepth,
                        IncludeSourceSpans = Bool(args, "include_spans", true),
                        IncludeConfidence  = Bool(args, "include_confidence", false),
                    };
                    var result = await query.GetFileOutlineAsync(req, ct).ConfigureAwait(false);
                    if (result == null)
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.FileNotFound, "File not found in index", new { file_path = req.FilePath });
                    return ToResult(result);
                });

            Add("code_index.get_snapshot_info",
                category: "code_index",
                description: "Returns metadata for the latest snapshot of a repository root: file counts, language breakdown, and build timestamps.",
                schema: Schema(
                    Prop("root_path",   "string", "Repository root path to query the latest snapshot for."),
                    Prop("snapshot_id", "string", "Specific snapshot ID (overrides root_path).")),
                handler: async (args, ct) =>
                {
                    var result = await query.GetSnapshotInfoAsync(
                        Str(args, "snapshot_id", null),
                        Str(args, "root_path", null),
                        ct).ConfigureAwait(false);
                    if (result == null)
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.SnapshotNotFound, "No snapshot found for repository", new { root_path = Str(args, "root_path", null) });
                    return ToResult(result);
                });

            Add("code_index.list_roots",
                category: "code_index",
                description: "[Deprecated: use code_index.list_repositories instead] Returns the list of repository root paths that have a published code index snapshot.",
                schema: Schema(),
                handler: async (args, ct) =>
                {
                    var roots = await query.ListRootPathsAsync(ct).ConfigureAwait(false);
                    return ToResult(new { roots });
                });

            Add("code_index.get_language_capabilities",
                category: "code_index",
                description: "Returns the list of supported language IDs and their parser adapter capabilities.",
                schema: Schema(),
                handler: (args, ct) =>
                {
                    var adapters = new StewardessMCPService.CodeIndexing.Parsers.Abstractions.IParserAdapter[]
                    {
                        new StewardessMCPService.Parsers.CSharp.CSharpParserAdapter(),
                        new StewardessMCPService.CodeIndexing.Parsers.Python.PythonParserAdapter(),
                    };
                    var caps = adapters.Select(a => new
                    {
                        language_id       = a.LanguageId,
                        adapter_version   = a.Capabilities.AdapterVersion,
                        supports_outline  = a.Capabilities.SupportsOutline,
                        supports_declarations = a.Capabilities.SupportsDeclarations,
                        supports_callables   = a.Capabilities.SupportsCallableExtraction,
                        heuristic_fallback   = a.Capabilities.SupportsHeuristicFallback,
                        notes             = a.Capabilities.GuaranteeNotes,
                    }).ToList();
                    return Task.FromResult(ToResult(new { adapters = caps }));
                });

            // ── Phase 2: Logical Symbol tools ────────────────────────────────────

            Add("code_index.find_symbols",
                category: "code_index",
                description: "Searches the symbol index by name or qualified name. " +
                             "Supports exact, prefix (default), or contains match modes. " +
                             "Results can be filtered by language, symbol kind, or container namespace.",
                schema: Schema(
                    Prop("query_text",   "string",  "Text to match against symbol names or qualified names.", required: true),
                    Prop("snapshot_id",  "string",  "Specific snapshot ID to query (overrides root_path)."),
                    Prop("root_path",    "string",  "Repository root path; used to resolve the latest snapshot."),
                    Prop("match_mode",   "string",  "Symbol name match mode.", def: "prefix",
                        enums: new[] { "exact", "prefix", "contains" }),
                    Prop("language",     "string",  "Filter by language ID (e.g. csharp, python)."),
                    Prop("kind",         "string",  "Filter by symbol kind (Namespace, Class, Method, Function, etc.)."),
                    Prop("container",    "string",  "Restrict to symbols inside this container qualified name."),
                    Prop("include_occurrence_count", "boolean", "Include occurrence counts per symbol (default true).", def: true),
                    Prop("include_members_summary",  "boolean", "Include member summary for type symbols (default false).", def: false),
                    Prop("page",         "integer", "Page number (1-based, default 1).", def: 1),
                    Prop("page_size",    "integer", "Results per page (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var langStr = Str(args, "language", null);
                    var kindStr = Str(args, "kind", null);
                    var containerStr = Str(args, "container", null);
                    var req = new StewardessMCPService.CodeIndexing.Query.FindSymbolsRequest
                    {
                        QueryText             = Str(args, "query_text"),
                        SnapshotId            = Str(args, "snapshot_id", null) ?? (Str(args, "root_path", null) != null ? null : null),
                        MatchMode             = Str(args, "match_mode", "prefix"),
                        LanguageFilter        = langStr != null ? new[] { langStr } : null,
                        KindFilter            = kindStr != null && System.Enum.TryParse<StewardessMCPService.CodeIndexing.Model.Semantic.SymbolKind>(kindStr, true, out var k)
                                                    ? new[] { k } : null,
                        ContainerFilter       = containerStr != null ? new[] { containerStr } : null,
                        IncludeOccurrenceCount = Bool(args, "include_occurrence_count", true),
                        IncludeMembersSummary  = Bool(args, "include_members_summary", false),
                        Page                  = Int(args, "page", 1),
                        PageSize              = Math.Min(Int(args, "page_size", 50), 200),
                    };
                    var result = await query.FindSymbolsAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("code_index.get_symbol",
                category: "code_index",
                description: "Returns full details for a single logical symbol by its stable symbol ID, " +
                             "including kind, qualified name, parent, primary location, and optional members summary. " +
                             "Use mode='summary' for a compact response with only the key identity fields.",
                schema: Schema(
                    Prop("symbol_id",              "string",  "Stable symbol ID to retrieve.", required: true),
                    Prop("snapshot_id",            "string",  "Specific snapshot ID to query."),
                    Prop("include_primary_occurrence", "boolean", "Include primary declaration location (default true).", def: true),
                    Prop("include_members_summary",   "boolean", "Include members summary for type symbols (default true).", def: true),
                    Prop("mode",                   "string",  "Response mode.", def: "expanded",
                        enums: new[] { "summary", "expanded" })),
                handler: async (args, ct) =>
                {
                    var mode = Str(args, "mode", "expanded");
                    var req = new StewardessMCPService.CodeIndexing.Query.GetSymbolRequest
                    {
                        SymbolId                = Str(args, "symbol_id"),
                        SnapshotId              = Str(args, "snapshot_id", null),
                        IncludePrimaryOccurrence = Bool(args, "include_primary_occurrence", true),
                        IncludeMembersSummary   = Bool(args, "include_members_summary", true),
                    };
                    var result = await query.GetSymbolAsync(req, ct).ConfigureAwait(false);
                    if (result?.Symbol == null)
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.SymbolNotFound, "Symbol not found", new { symbol_id = req.SymbolId });
                    if (string.Equals(mode, "summary", StringComparison.OrdinalIgnoreCase))
                    {
                        var sym = result.Symbol;
                        var loc = result.PrimaryOccurrence;
                        return ToResult(new
                        {
                            symbolId = sym.SymbolId,
                            name = sym.Name,
                            kind = sym.Kind.ToString(),
                            qualifiedName = sym.QualifiedName,
                            filePath = loc?.FilePath,
                            line = loc?.SourceSpan?.StartLine,
                        });
                    }
                    return ToResult(result);
                });

            Add("code_index.get_symbol_occurrences",
                category: "code_index",
                description: "Returns all occurrences (declarations, references, definitions) of a symbol across the indexed files. " +
                             "Each occurrence carries a file path and source line/column.",
                schema: Schema(
                    Prop("symbol_id",  "string", "Stable symbol ID to look up occurrences for.", required: true),
                    Prop("snapshot_id","string", "Specific snapshot ID to query."),
                    Prop("role",       "string", "Filter to a specific occurrence role: Declaration, Reference, Definition, etc."),
                    Prop("page",       "integer", "Page number (1-based, default 1).", def: 1),
                    Prop("page_size",  "integer", "Items per page (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var roleStr = Str(args, "role", null);
                    StewardessMCPService.CodeIndexing.Model.Semantic.OccurrenceRole[]? roleFilter = null;
                    if (roleStr != null && System.Enum.TryParse<StewardessMCPService.CodeIndexing.Model.Semantic.OccurrenceRole>(roleStr, true, out var role))
                        roleFilter = new[] { role };
                    var req = new StewardessMCPService.CodeIndexing.Query.GetSymbolOccurrencesRequest
                    {
                        SymbolId   = Str(args, "symbol_id"),
                        SnapshotId = Str(args, "snapshot_id", null),
                        RoleFilter = roleFilter,
                    };
                    var page = Math.Max(1, Int(args, "page", 1));
                    var pageSize = Math.Min(200, Math.Max(1, Int(args, "page_size", 50)));
                    var result = await query.GetSymbolOccurrencesAsync(req, page, pageSize, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("code_index.get_symbol_children",
                category: "code_index",
                description: "Returns the direct child symbols of a parent symbol (e.g. members of a namespace, nested types in a class). " +
                             "Optionally filter by symbol kind.",
                schema: Schema(
                    Prop("symbol_id",          "string",  "Parent symbol ID.", required: true),
                    Prop("snapshot_id",        "string",  "Specific snapshot ID to query."),
                    Prop("kind",               "string",  "Filter children to this symbol kind."),
                    Prop("include_nested_types","boolean", "Include nested type symbols (default true).", def: true),
                    Prop("page",               "integer", "Page number (1-based, default 1).", def: 1),
                    Prop("page_size",          "integer", "Items per page (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var kindStr = Str(args, "kind", null);
                    var req = new StewardessMCPService.CodeIndexing.Query.GetSymbolChildrenRequest
                    {
                        SymbolId           = Str(args, "symbol_id"),
                        SnapshotId         = Str(args, "snapshot_id", null),
                        KindFilter         = kindStr != null && System.Enum.TryParse<StewardessMCPService.CodeIndexing.Model.Semantic.SymbolKind>(kindStr, true, out var k)
                                                 ? new[] { k } : null,
                        IncludeNestedTypes = Bool(args, "include_nested_types", true),
                    };
                    var page = Math.Max(1, Int(args, "page", 1));
                    var pageSize = Math.Min(200, Math.Max(1, Int(args, "page_size", 50)));
                    var result = await query.GetSymbolChildrenAsync(req, page, pageSize, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("code_index.get_type_members",
                category: "code_index",
                description: "Returns the members of a type symbol (class, struct, interface, etc.) " +
                             "grouped into constructors, methods, properties, fields, events, and nested types.",
                schema: Schema(
                    Prop("type_symbol_id",      "string",  "Symbol ID of the type to inspect.", required: true),
                    Prop("snapshot_id",         "string",  "Specific snapshot ID to query."),
                    Prop("include_accessors",   "boolean", "Include property accessor symbols (default true).", def: true),
                    Prop("include_nested_types","boolean", "Include nested type symbols (default true).", def: true)),
                handler: async (args, ct) =>
                {
                    var req = new StewardessMCPService.CodeIndexing.Query.GetTypeMembersRequest
                    {
                        TypeSymbolId       = Str(args, "type_symbol_id"),
                        SnapshotId         = Str(args, "snapshot_id", null),
                        IncludeAccessors   = Bool(args, "include_accessors", true),
                        IncludeNestedTypes = Bool(args, "include_nested_types", true),
                    };
                    var result = await query.GetTypeMembersAsync(req, ct).ConfigureAwait(false);
                    if (result == null)
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.SymbolNotFound, "Symbol not found or not a type", new { symbol_id = req.TypeSymbolId });
                    return ToResult(result);
                });

            Add("code_index.resolve_location",
                category: "code_index",
                description: "Resolves a symbol ID or occurrence ID to a concrete source location (file path, line, column). " +
                             "Provide exactly one of symbol_id or occurrence_id.",
                schema: Schema(
                    Prop("symbol_id",    "string", "Stable symbol ID to resolve to its primary declaration location."),
                    Prop("occurrence_id","string", "Occurrence ID to resolve to its exact source position."),
                    Prop("snapshot_id",  "string", "Specific snapshot ID to query.")),
                handler: async (args, ct) =>
                {
                    var req = new StewardessMCPService.CodeIndexing.Query.ResolveLocationRequest
                    {
                        SymbolId     = Str(args, "symbol_id", null),
                        OccurrenceId = Str(args, "occurrence_id", null),
                        SnapshotId   = Str(args, "snapshot_id", null),
                    };
                    var result = await query.ResolveLocationAsync(req, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(result.Error))
                        return ErrorResult(ClassifyError(result.Error), result.Error);
                    return ToResult(result);
                });

            Add("code_index.get_namespace_tree",
                category: "code_index",
                description: "Returns a hierarchical tree of namespaces and module containers extracted from the code index. " +
                             "Useful for navigating the top-level structure of a repository. " +
                             "Optionally filter by language or root container, and control depth.",
                schema: Schema(
                    Prop("snapshot_id",     "string",  "Specific snapshot ID to query."),
                    Prop("root_path",       "string",  "Repository root path; used to resolve the latest snapshot."),
                    Prop("language",        "string",  "Filter to a single language ID."),
                    Prop("root_container",  "string",  "Only return descendants of this container qualified name."),
                    Prop("include_counts",  "boolean", "Include symbol and file counts per node (default true).", def: true),
                    Prop("max_depth",       "integer", "Maximum tree depth (0 = unlimited).", def: 0)),
                handler: async (args, ct) =>
                {
                    var langStr = Str(args, "language", null);
                    var maxDepth = Int(args, "max_depth", 0);
                    var req = new StewardessMCPService.CodeIndexing.Query.GetNamespaceTreeRequest
                    {
                        SnapshotId     = Str(args, "snapshot_id", null),
                        LanguageFilter = langStr != null ? new[] { langStr } : null,
                        RootContainer  = Str(args, "root_container", null),
                        IncludeCounts  = Bool(args, "include_counts", true),
                        MaxDepth       = maxDepth == 0 ? null : maxDepth,
                    };
                    var result = await query.GetNamespaceTreeAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("code_index.get_imports",
                category: "code_index",
                description: "Returns import, using, and require directives extracted from a specific file. " +
                             "Shows all dependencies declared at the top of the file, including namespace " +
                             "imports (C#), module imports (Python), and their resolution status.",
                schema: Schema(
                    Prop("file_path",   "string",  "Relative file path to query imports for (required)."),
                    Prop("snapshot_id", "string",  "Specific snapshot ID to query."),
                    Prop("root_path",   "string",  "Repository root path; used to resolve the latest snapshot."),
                    Prop("page",        "integer", "Page number (1-based, default 1).", def: 1),
                    Prop("page_size",   "integer", "Items per page (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var filePath = Str(args, "file_path", null);
                    if (string.IsNullOrEmpty(filePath))
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.ValidationError, "file_path is required", new { parameter = "file_path" });
                    var req = new StewardessMCPService.CodeIndexing.Query.GetImportsRequest
                    {
                        FilePath   = filePath,
                        SnapshotId = Str(args, "snapshot_id", null),
                        RootPath   = Str(args, "root_path", null),
                    };
                    var page = Math.Max(1, Int(args, "page", 1));
                    var pageSize = Math.Min(200, Math.Max(1, Int(args, "page_size", 50)));
                    var result = await query.GetImportsAsync(req, page, pageSize, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(result.Error))
                        return ErrorResult(ClassifyError(result.Error), result.Error);
                    return ToResult(result);
                });

            Add("code_index.get_references",
                category: "code_index",
                description: "Returns reference edges for a specific logical symbol. " +
                             "Outgoing references show what other symbols this symbol depends on " +
                             "(e.g., base types, field types, method parameter types). " +
                             "Incoming references show what symbols depend on this symbol.",
                schema: Schema(
                    Prop("symbol_id",        "string",  "Symbol ID to query references for.", required: true),
                    Prop("snapshot_id",      "string",  "Specific snapshot ID to query."),
                    Prop("include_outgoing", "boolean", "Include outgoing references (default true).", def: true),
                    Prop("include_incoming", "boolean", "Include incoming references (default false).", def: false),
                    Prop("page",             "integer", "Page number for outgoing refs (1-based, default 1).", def: 1),
                    Prop("page_size",        "integer", "Items per page for outgoing refs (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var symbolId = Str(args, "symbol_id", null);
                    if (string.IsNullOrEmpty(symbolId))
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.ValidationError, "symbol_id is required", new { parameter = "symbol_id" });
                    var req = new StewardessMCPService.CodeIndexing.Query.GetReferencesRequest
                    {
                        SymbolId        = symbolId,
                        SnapshotId      = Str(args, "snapshot_id", null),
                        IncludeOutgoing = Bool(args, "include_outgoing", true),
                        IncludeIncoming = Bool(args, "include_incoming", false),
                    };
                    var page = Math.Max(1, Int(args, "page", 1));
                    var pageSize = Math.Min(200, Math.Max(1, Int(args, "page_size", 50)));
                    var result = await query.GetReferencesAsync(req, page, pageSize, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(result.Error))
                        return ErrorResult(ClassifyError(result.Error), result.Error);
                    return ToResult(result);
                });

            Add("code_index.get_file_references",
                category: "code_index",
                description: "Returns all reference edges originating from symbols declared in a specific file. " +
                             "Shows the complete dependency picture for a file: which types it inherits from, " +
                             "which types its members use, and which types it instantiates.",
                schema: Schema(
                    Prop("file_path",   "string",  "Relative file path to query references for (required)."),
                    Prop("snapshot_id", "string",  "Specific snapshot ID to query."),
                    Prop("page",        "integer", "Page number (1-based, default 1).", def: 1),
                    Prop("page_size",   "integer", "Items per page (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var filePath = Str(args, "file_path", null);
                    if (string.IsNullOrEmpty(filePath))
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.ValidationError, "file_path is required", new { parameter = "file_path" });
                    var req = new StewardessMCPService.CodeIndexing.Query.GetFileReferencesRequest
                    {
                        FilePath   = filePath,
                        SnapshotId = Str(args, "snapshot_id", null),
                    };
                    var page = Math.Max(1, Int(args, "page", 1));
                    var pageSize = Math.Min(200, Math.Max(1, Int(args, "page_size", 50)));
                    var result = await query.GetFileReferencesAsync(req, page, pageSize, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(result.Error))
                        return ErrorResult(ClassifyError(result.Error), result.Error);
                    return ToResult(result);
                });

            Add("code_index.get_dependencies",
                category: "code_index",
                description: "Returns outbound hard dependency projections for a logical symbol. " +
                             "Hard dependencies are edges resolved with high confidence: ExactBound, ScopedBound, ImportBound, or AliasBound. " +
                             "Use hard_only=false to include all edges regardless of resolution confidence.",
                schema: Schema(
                    Prop("symbol_id",          "string",  "Symbol ID to query dependencies for.", required: true),
                    Prop("snapshot_id",        "string",  "Specific snapshot ID to query."),
                    Prop("hard_only",          "boolean", "Only include hard-bound dependencies (default true).", def: true),
                    Prop("include_evidence",   "boolean", "Include evidence text in results (default true).", def: true),
                    Prop("include_confidence", "boolean", "Include confidence scores in results (default true).", def: true),
                    Prop("page",               "integer", "Page number (1-based, default 1).", def: 1),
                    Prop("page_size",          "integer", "Items per page (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var symbolId = Str(args, "symbol_id", null);
                    if (string.IsNullOrEmpty(symbolId))
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.ValidationError, "symbol_id is required", new { parameter = "symbol_id" });
                    var req = new StewardessMCPService.CodeIndexing.Query.GetDependenciesRequest
                    {
                        SymbolId          = symbolId,
                        SnapshotId        = Str(args, "snapshot_id", null),
                        HardOnly          = Bool(args, "hard_only", true),
                        IncludeEvidence   = Bool(args, "include_evidence", true),
                        IncludeConfidence = Bool(args, "include_confidence", true),
                    };
                    var page = Math.Max(1, Int(args, "page", 1));
                    var pageSize = Math.Min(200, Math.Max(1, Int(args, "page_size", 50)));
                    var result = await query.GetDependenciesAsync(req, page, pageSize, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(result.Error))
                        return ErrorResult(ClassifyError(result.Error), result.Error);
                    return ToResult(result);
                });

            Add("code_index.get_dependents",
                category: "code_index",
                description: "Returns inbound hard dependent projections for a logical symbol. " +
                             "Shows which other symbols in the repository depend on this symbol. " +
                             "Use hard_only=false to include weakly-resolved edges.",
                schema: Schema(
                    Prop("symbol_id",        "string",  "Symbol ID to query dependents for.", required: true),
                    Prop("snapshot_id",      "string",  "Specific snapshot ID to query."),
                    Prop("hard_only",        "boolean", "Only include hard-bound dependents (default true).", def: true),
                    Prop("include_evidence", "boolean", "Include evidence text in results (default true).", def: true),
                    Prop("page",             "integer", "Page number (1-based, default 1).", def: 1),
                    Prop("page_size",        "integer", "Items per page (default 50, max 200).", def: 50)),
                handler: async (args, ct) =>
                {
                    var symbolId = Str(args, "symbol_id", null);
                    if (string.IsNullOrEmpty(symbolId))
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.ValidationError, "symbol_id is required", new { parameter = "symbol_id" });
                    var req = new StewardessMCPService.CodeIndexing.Query.GetDependentsRequest
                    {
                        SymbolId        = symbolId,
                        SnapshotId      = Str(args, "snapshot_id", null),
                        HardOnly        = Bool(args, "hard_only", true),
                        IncludeEvidence = Bool(args, "include_evidence", true),
                    };
                    var page = Math.Max(1, Int(args, "page", 1));
                    var pageSize = Math.Min(200, Math.Max(1, Int(args, "page_size", 50)));
                    var result = await query.GetDependentsAsync(req, page, pageSize, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(result.Error))
                        return ErrorResult(ClassifyError(result.Error), result.Error);
                    return ToResult(result);
                });

            Add("code_index.get_symbol_relationships",
                category: "code_index",
                description: "Returns a consolidated relationship payload for a symbol, combining children, " +
                             "reference edges, outgoing dependencies, and inbound dependents in a single call. " +
                             "Use the include_* flags to control which sections are returned. " +
                             "Use mode='summary' to limit each section to 5 items.",
                schema: Schema(
                    Prop("symbol_id",             "string",  "Symbol ID to query relationships for (required)."),
                    Prop("snapshot_id",           "string",  "Specific snapshot ID to query."),
                    Prop("include_references",    "boolean", "Include raw reference edges (default true).", def: true),
                    Prop("include_dependencies",  "boolean", "Include outgoing hard dependency projections (default true).", def: true),
                    Prop("include_dependents",    "boolean", "Include inbound hard dependent projections (default true).", def: true),
                    Prop("include_children",      "boolean", "Include direct child symbols (default true).", def: true),
                    Prop("max_items_per_section", "integer", "Maximum items to return per section (0 = unlimited).", def: 0),
                    Prop("mode",                  "string",  "Response mode.", def: "expanded",
                        enums: new[] { "summary", "expanded" })),
                handler: async (args, ct) =>
                {
                    var symbolId = Str(args, "symbol_id", null);
                    if (string.IsNullOrEmpty(symbolId))
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.ValidationError, "symbol_id is required", new { parameter = "symbol_id" });
                    var mode = Str(args, "mode", "expanded");
                    var maxItems = Int(args, "max_items_per_section", 0);
                    if (string.Equals(mode, "summary", StringComparison.OrdinalIgnoreCase))
                        maxItems = 5;
                    var req = new StewardessMCPService.CodeIndexing.Query.GetSymbolRelationshipsRequest
                    {
                        SymbolId            = symbolId,
                        SnapshotId          = Str(args, "snapshot_id", null),
                        IncludeReferences   = Bool(args, "include_references", true),
                        IncludeDependencies = Bool(args, "include_dependencies", true),
                        IncludeDependents   = Bool(args, "include_dependents", true),
                        IncludeChildren     = Bool(args, "include_children", true),
                        MaxItemsPerSection  = maxItems == 0 ? null : maxItems,
                    };
                    var result = await query.GetSymbolRelationshipsAsync(req, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(result.Error))
                        return ErrorResult(ClassifyError(result.Error), result.Error);
                    return ToResult(result);
                });

            Add("code_index.get_file_dependencies",
                category: "code_index",
                description: "Returns a file-level dependency projection showing which other files this file depends on. " +
                             "Edges are grouped by target file, with relationship kind summaries and representative examples. " +
                             "Useful for understanding the import-level coupling of a file in the repository.",
                schema: Schema(
                    Prop("file_path",               "string",  "Relative file path to query dependencies for (required)."),
                    Prop("snapshot_id",             "string",  "Specific snapshot ID to query."),
                    Prop("hard_only",               "boolean", "Only include hard-bound edges (default false).", def: false),
                    Prop("collapse_by_target_file", "boolean", "Collapse all edges to same file into one entry (default true).", def: true)),
                handler: async (args, ct) =>
                {
                    var filePath = Str(args, "file_path", null);
                    if (string.IsNullOrEmpty(filePath))
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.ValidationError, "file_path is required", new { parameter = "file_path" });
                    var req = new StewardessMCPService.CodeIndexing.Query.GetFileDependenciesRequest
                    {
                        FilePath             = filePath,
                        SnapshotId           = Str(args, "snapshot_id", null),
                        HardOnly             = Bool(args, "hard_only", false),
                        CollapseByTargetFile = Bool(args, "collapse_by_target_file", true),
                    };
                    var result = await query.GetFileDependenciesAsync(req, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(result.Error))
                        return ErrorResult(ClassifyError(result.Error), result.Error);
                    return ToResult(result);
                });

            Add("code_index.update",
                category: "code_index",
                description: "Performs an incremental index update for a repository. " +
                             "Only re-parses files that have been added, modified, or deleted since the last index. " +
                             "Reference resolution is re-run for all files to ensure cross-file consistency. " +
                             "Falls back to a full build if no previous index exists.",
                schema: Schema(
                    Prop("root_path",     "string", "Absolute repository root path to update (required).", required: true),
                    PropWithItems("changed_files", "Optional array of changed relative file paths. If omitted, change detection runs automatically by comparing file hashes.",
                        items: new { type = "string" })),
                handler: async (args, ct) =>
                {
                    var rootPath = Str(args, "root_path", null);
                    if (string.IsNullOrEmpty(rootPath))
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.ValidationError, "root_path is required", new { parameter = "root_path" });

                    var changedFilesList = StrList(args, "changed_files");
                    IReadOnlyList<string> changedFiles = changedFilesList.Count > 0 ? changedFilesList : null;

                    var req = new StewardessMCPService.CodeIndexing.Indexing.IndexUpdateRequest
                    {
                        RootPath     = rootPath,
                        ChangedFiles = changedFiles,
                    };
                    var result = await indexer.UpdateAsync(req, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(result.Error))
                        return ErrorResult(ClassifyError(result.Error), result.Error);
                    return ToResult(result);
                });

            Add("code_index.get_index_status",
                category: "code_index",
                description: "Returns the current indexing status for a repository root, including state, latest snapshot, " +
                             "file/symbol/reference counts, and delta information from the most recent incremental update.",
                schema: Schema(
                    Prop("root_path", "string", "Absolute repository root path.", required: true)),
                handler: async (args, ct) =>
                {
                    var rootPath = Str(args, "root_path", null);
                    if (string.IsNullOrEmpty(rootPath))
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.ValidationError, "root_path is required", new { parameter = "root_path" });
                    var result = await indexer.GetStatusAsync(rootPath, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("code_index.list_repositories",
                category: "code_index",
                description: "Lists all repositories that have been indexed, along with their current state, " +
                             "latest snapshot ID, and summary counts. Useful for discovering what the service currently has indexed.",
                schema: Schema(),
                handler: async (args, ct) =>
                {
                    var roots = await query.ListRootPathsAsync(ct).ConfigureAwait(false);
                    var items = new System.Collections.Generic.List<object>();
                    foreach (var root in roots)
                    {
                        var status = await indexer.GetStatusAsync(root, ct).ConfigureAwait(false);
                        items.Add(new
                        {
                            rootPath         = status.RootPath,
                            state            = status.State.ToString(),
                            latestSnapshotId = status.LatestSnapshotId,
                            lastCompletedAt  = status.LastCompletedAt,
                            fileCount        = status.FileCount,
                            symbolCount      = status.SymbolCount,
                            referenceCount   = status.ReferenceCount,
                        });
                    }
                    return ToResult(new { repositories = items });
                });

            Add("code_index.clear_repository",
                category: "code_index",
                description: "Removes all stored index state for a repository root. " +
                             "The repository will need to be re-indexed before it can be queried. " +
                             "Use with caution — this cannot be undone.",
                schema: Schema(
                    Prop("root_path", "string", "Absolute repository root path to clear (required).", required: true)),
                isDestructive: true,
                handler: async (args, ct) =>
                {
                    var rootPath = Str(args, "root_path", null);
                    if (string.IsNullOrEmpty(rootPath))
                        return ErrorResult(StewardessMCPService.CodeIndexing.Query.McpErrorCode.ValidationError, "root_path is required", new { parameter = "root_path" });

                    var removedSnapshots = await indexer.ClearRepositoryAsync(rootPath, ct).ConfigureAwait(false);
                    return ToResult(new
                    {
                        rootPath,
                        removedSnapshots,
                        status = removedSnapshots > 0 ? "cleared" : "not_found",
                    });
                });

            Add("code_index.ping",
                category: "code_index",
                description: "Health check for the code index service. Returns service version, readiness, and available capabilities.",
                schema: Schema(),
                handler: (args, ct) =>
                {
                    var adapters = new StewardessMCPService.CodeIndexing.Parsers.Abstractions.IParserAdapter[]
                    {
                        new StewardessMCPService.Parsers.CSharp.CSharpParserAdapter(),
                        new StewardessMCPService.CodeIndexing.Parsers.Python.PythonParserAdapter(),
                    };
                    return Task.FromResult(ToResult(new
                    {
                        status = "ok",
                        service = "StewardessMCPService.CodeIndexing",
                        version = "1.0.0",
                        languages = adapters.Select(a => a.LanguageId).ToArray(),
                        tool_chains = new[]
                        {
                            "code_index.build → code_index.list_files → code_index.get_file_outline",
                            "code_index.find_symbols → code_index.get_symbol → code_index.get_dependencies",
                            "code_index.get_symbol → code_index.get_references → code_index.get_dependents",
                            "code_index.build → code_index.get_namespace_tree → code_index.find_symbols",
                            "code_index.update → code_index.get_index_status",
                            "code_index.list_repositories → code_index.get_snapshot_info",
                        },
                    }));
                });
        }

        // ── Repo browser tools ───────────────────────────────────────────────────

        private void RegisterRepoBrowserTools()
        {
            // ── repo_browser.print_tree ──────────────────────────────────────────
            Add("repo_browser.print_tree",
                category: "repo_browser",
                description: "Returns a flat list of files and directories in the repository. " +
                             "Use to inspect repository or directory structure when the exact file is not yet known. " +
                             "Use before repo_browser.read_file or repo_browser.grep when discovering candidate paths.",
                schema: Schema(
                    Prop("relative_path",       "string",  "Subdirectory to inspect (empty = repo root)."),
                    Prop("max_depth",           "integer", "Maximum directory depth to traverse (default 4).", def: 4),
                    Prop("include_files",       "boolean", "Include files in output (default true).", def: true),
                    Prop("include_directories", "boolean", "Include directories in output (default true).", def: true),
                    PropWithItems("glob_include", "Include only entries whose paths match these glob patterns."),
                    PropWithItems("glob_exclude", "Exclude entries whose paths match these glob patterns."),
                    Prop("max_entries",         "integer", "Maximum number of entries to return (default 1000).", def: 1000),
                    Prop("show_hidden",         "boolean", "Include hidden entries (names starting with '.') (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var relPath      = Str(args, "relative_path", "");
                    var maxDepth     = Int(args, "max_depth", 4);
                    var inclFiles    = Bool(args, "include_files", true);
                    var inclDirs     = Bool(args, "include_directories", true);
                    var globInclude  = StrList(args, "glob_include");
                    var globExclude  = StrList(args, "glob_exclude");
                    var maxEntries   = Int(args, "max_entries", 1000);
                    var showHidden   = Bool(args, "show_hidden", false);

                    var treeReq = new ListTreeRequest
                    {
                        Path            = relPath,
                        MaxDepth        = Math.Clamp(maxDepth, 1, 20),
                        DirectoriesOnly = !inclFiles,
                    };
                    var treeResp = await _files.ListTreeAsync(treeReq, ct).ConfigureAwait(false);

                    var items    = new List<RepoBrowserTreeItem>();
                    bool trunc   = treeResp.Truncated;

                    void WalkNode(TreeNode node, int depth)
                    {
                        if (items.Count >= maxEntries) { trunc = true; return; }
                        if (node == null) return;

                        var isDir = node.Type == "directory";
                        if (!showHidden && node.Name.StartsWith(".")) return;
                        if (isDir && !inclDirs)
                        {
                            if (node.Children != null)
                                foreach (var c in node.Children) WalkNode(c, depth + 1);
                            return;
                        }
                        if (!isDir && !inclFiles) return;

                        var path = node.RelativePath ?? "";
                        if (globInclude.Count > 0 && !globInclude.Any(g => MatchGlob(path, g))) goto recurse;
                        if (globExclude.Any(g => MatchGlob(path, g))) goto recurse;

                        items.Add(new RepoBrowserTreeItem
                        {
                            Path        = path,
                            Name        = node.Name,
                            Kind        = isDir ? "directory" : "file",
                            Depth       = depth,
                            HasChildren = isDir ? (bool?)(node.Children != null && node.Children.Count > 0) : null,
                            SizeBytes   = node.SizeBytes,
                        });

                        recurse:
                        if (isDir && node.Children != null)
                            foreach (var c in node.Children) WalkNode(c, depth + 1);
                    }

                    if (treeResp.Root?.Children != null)
                        foreach (var c in treeResp.Root.Children) WalkNode(c, 0);

                    return ToResult(new RepoBrowserTreeResponse
                    {
                        RootPath     = _settings.RepositoryRoot,
                        RelativePath = relPath,
                        MaxDepth     = maxDepth,
                        EntryCount   = items.Count,
                        Truncated    = trunc,
                        Items        = items,
                    });
                });

            // ── repo_browser.grep ────────────────────────────────────────────────
            Add("repo_browser.grep",
                category: "repo_browser",
                description: "Searches file contents for symbols, text, imports, or code fragments across the repository. " +
                             "Use when you know what text you are looking for but do not know which file contains it. " +
                             "Returns a flat match list with line numbers and surrounding context.",
                schema: Schema(
                    Prop("query",               "string",  "Text or pattern to search for.", required: true),
                    Prop("mode",                "string",  "Search mode: literal (default), regex, word, symbol_hint.",
                         def: "literal", enums: new[] { "literal", "regex", "word", "symbol_hint" }),
                    Prop("path_prefix",         "string",  "Restrict search to this subdirectory (relative path)."),
                    PropWithItems("glob_include", "Include only files whose paths match these glob patterns."),
                    PropWithItems("glob_exclude", "Exclude files whose paths match these glob patterns."),
                    Prop("case_sensitive",      "boolean", "Case-sensitive search (default false).", def: false),
                    Prop("max_results",         "integer", "Maximum total match lines to return (default 100).", def: 100),
                    Prop("max_matches_per_file","integer", "Maximum match lines per file (default 20).", def: 20),
                    Prop("context_lines",       "integer", "Context lines before and after each match (default 2).", def: 2)),
                handler: async (args, ct) =>
                {
                    var query       = Str(args, "query");
                    var mode        = (Str(args, "mode", "literal") ?? "literal").ToLowerInvariant();
                    var pathPrefix  = Str(args, "path_prefix", "");
                    var globInclude = StrList(args, "glob_include");
                    var globExclude = StrList(args, "glob_exclude");
                    var caseSens    = Bool(args, "case_sensitive", false);
                    var maxResults  = Int(args, "max_results", 100);
                    var maxPerFile  = Int(args, "max_matches_per_file", 20);
                    var ctxLines    = Int(args, "context_lines", 2);

                    // Derive extension list from simple "*.ext" globs for faster service-side filtering
                    List<string> extensions = null;
                    if (globInclude.Count > 0)
                    {
                        var exts = globInclude
                            .Where(g => g.StartsWith("*.") && !g.Contains('/') && !g.Contains('\\'))
                            .Select(g => g.Substring(1))
                            .ToList();
                        if (exts.Count == globInclude.Count) extensions = exts;
                    }

                    SearchResponse sr;
                    if (mode == "regex")
                    {
                        sr = await _search.SearchRegexAsync(new SearchRegexRequest
                        {
                            Pattern            = query,
                            SearchPath         = pathPrefix ?? "",
                            IgnoreCase         = !caseSens,
                            MaxResults         = maxResults * 2,
                            ContextLinesBefore = ctxLines,
                            ContextLinesAfter  = ctxLines,
                            Extensions         = extensions,
                        }, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        sr = await _search.SearchTextAsync(new SearchTextRequest
                        {
                            Query              = query,
                            SearchPath         = pathPrefix ?? "",
                            IgnoreCase         = !caseSens,
                            WholeWord          = mode == "word" || mode == "symbol_hint",
                            MaxResults         = maxResults * 2,
                            ContextLinesBefore = ctxLines,
                            ContextLinesAfter  = ctxLines,
                            Extensions         = extensions,
                        }, ct).ConfigureAwait(false);
                    }

                    var flat  = new List<RepoBrowserGrepMatch>();
                    bool trunc = sr.Truncated;

                    foreach (var file in sr.Files)
                    {
                        var rel = file.RelativePath;
                        if (globInclude.Count > 0 && extensions == null && !globInclude.Any(g => MatchGlob(rel, g))) continue;
                        if (globExclude.Any(g => MatchGlob(rel, g))) continue;

                        int fileHits = 0;
                        foreach (var m in file.Matches)
                        {
                            if (flat.Count >= maxResults) { trunc = true; break; }
                            if (fileHits >= maxPerFile)   { trunc = true; break; }
                            flat.Add(new RepoBrowserGrepMatch
                            {
                                FilePath      = rel,
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

                    return ToResult(new RepoBrowserGrepResponse
                    {
                        RootPath   = _settings.RepositoryRoot,
                        Query      = query,
                        Mode       = mode,
                        MatchCount = flat.Count,
                        Truncated  = trunc,
                        Items      = flat,
                    });
                });

            // ── repo_browser.read_file ───────────────────────────────────────────
            Add("repo_browser.read_file",
                category: "repo_browser",
                description: "Opens a specific file and returns its contents. " +
                             "Use only when you already know the exact file path, or after locating it with " +
                             "repo_browser.find_path, repo_browser.print_tree, or repo_browser.grep. " +
                             "Specify start_line and end_line to read a partial range for large files.",
                schema: Schema(
                    Prop("file_path",           "string",  "Repository-relative file path.", required: true),
                    Prop("start_line",          "integer", "First line to read, inclusive (1-based). Omit to read from the beginning."),
                    Prop("end_line",            "integer", "Last line to read, inclusive (1-based). -1 = end of file.", def: -1),
                    Prop("max_bytes",           "integer", "Maximum bytes to return for full-file reads (default 65536).", def: 65536),
                    Prop("include_line_numbers","boolean", "Prepend line numbers to each output line (default true).", def: true)),
                handler: async (args, ct) =>
                {
                    var filePath      = Str(args, "file_path");
                    var startLine     = NullableInt(args, "start_line");
                    var endLine       = NullableInt(args, "end_line");
                    var maxBytes      = Int(args, "max_bytes", 65536);
                    var lineNums      = Bool(args, "include_line_numbers", true);

                    var resp = new RepoBrowserReadFileResponse
                    {
                        RootPath = _settings.RepositoryRoot,
                        FilePath = filePath,
                    };

                    try
                    {
                        if (startLine.HasValue)
                        {
                            var r = await _files.ReadFileRangeAsync(new ReadFileRangeRequest
                            {
                                Path               = filePath,
                                StartLine          = startLine.Value,
                                EndLine            = endLine ?? -1,
                                IncludeLineNumbers = lineNums,
                            }, ct).ConfigureAwait(false);
                            resp.Exists    = true;
                            resp.StartLine = r.StartLine;
                            resp.EndLine   = r.EndLine;
                            resp.Content   = r.Content;
                        }
                        else
                        {
                            var r = await _files.ReadFileAsync(new ReadFileRequest
                            {
                                Path     = filePath,
                                MaxBytes = maxBytes,
                            }, ct).ConfigureAwait(false);
                            resp.Exists    = true;
                            resp.Encoding  = r.Encoding;
                            resp.SizeBytes = r.SizeBytes;
                            resp.Truncated = r.Truncated;
                            resp.Content   = lineNums ? AddLineNumbers(r.Content) : r.Content;
                        }
                    }
                    catch (Exception ex) when (
                        ex.Message.Contains("not found",    StringComparison.OrdinalIgnoreCase) ||
                        ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                        ex.Message.Contains("outside the repository", StringComparison.OrdinalIgnoreCase))
                    {
                        resp.Exists  = false;
                        resp.Content = string.Empty;
                    }

                    return ToResult(resp);
                });

            // ── repo_browser.find_path ───────────────────────────────────────────
            Add("repo_browser.find_path",
                category: "repo_browser",
                description: "Locates a file or directory when you know its name or part of its path but not its exact location. " +
                             "Use before repo_browser.read_file when the path is uncertain. " +
                             "Supports name match, path fragment, prefix, and exact path modes.",
                schema: Schema(
                    Prop("query",         "string",  "File name, directory name, or partial path to search for.", required: true),
                    Prop("match_mode",    "string",  "Matching mode: name (default), path_fragment, exact_path, prefix.",
                         def: "name", enums: new[] { "name", "path_fragment", "exact_path", "prefix" }),
                    Prop("target_kind",   "string",  "What to find: file, directory, or any (default).",
                         def: "any",  enums: new[] { "file", "directory", "any" }),
                    Prop("case_sensitive","boolean", "Case-sensitive matching (default false).", def: false),
                    PropWithItems("glob_include", "Include only results whose paths match these glob patterns."),
                    PropWithItems("glob_exclude", "Exclude results whose paths match these glob patterns."),
                    Prop("max_results",   "integer", "Maximum results to return (default 50).", def: 50)),
                handler: async (args, ct) =>
                {
                    var query       = Str(args, "query");
                    var matchMode   = (Str(args, "match_mode", "name") ?? "name").ToLowerInvariant();
                    var targetKind  = (Str(args, "target_kind", "any") ?? "any").ToLowerInvariant();
                    var caseSens    = Bool(args, "case_sensitive", false);
                    var globInclude = StrList(args, "glob_include");
                    var globExclude = StrList(args, "glob_exclude");
                    var maxResults  = Int(args, "max_results", 50);
                    var cmp         = caseSens ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    var items       = new List<RepoBrowserPathMatch>();
                    bool trunc      = false;

                    // ── Files ────────────────────────────────────────────────────
                    if (targetKind == "file" || targetKind == "any")
                    {
                        var fileResp = await _search.SearchFileNamesAsync(new SearchFileNamesRequest
                        {
                            Pattern       = matchMode == "name" ? query : "*",
                            MaxResults    = maxResults * 3,
                            IgnoreCase    = !caseSens,
                            MatchFullPath = matchMode != "name",
                        }, ct).ConfigureAwait(false);

                        foreach (var m in fileResp.Matches)
                        {
                            if (items.Count >= maxResults) { trunc = true; break; }
                            var rel  = m.RelativePath ?? "";
                            var norm = rel.Replace('\\', '/');
                            var qn   = query.Replace('\\', '/');

                            string reason = matchMode switch
                            {
                                "name"          => "file name match",
                                "path_fragment" => norm.Contains(qn, cmp) ? "path fragment match" : null,
                                "exact_path"    => string.Equals(norm, qn, cmp) ? "exact path match" : null,
                                "prefix"        => norm.StartsWith(qn, cmp) ? "path prefix match" : null,
                                _               => null,
                            };
                            if (reason == null) continue;
                            if (globInclude.Count > 0 && !globInclude.Any(g => MatchGlob(rel, g))) continue;
                            if (globExclude.Any(g => MatchGlob(rel, g))) continue;

                            items.Add(new RepoBrowserPathMatch
                            {
                                Path        = rel,
                                Name        = m.Name,
                                Kind        = "file",
                                MatchReason = reason,
                            });
                        }
                    }

                    // ── Directories ──────────────────────────────────────────────
                    if ((targetKind == "directory" || targetKind == "any") && items.Count < maxResults)
                    {
                        var treeResp = await _files.ListTreeAsync(new ListTreeRequest
                        {
                            Path            = "",
                            MaxDepth        = 15,
                            DirectoriesOnly = true,
                        }, ct).ConfigureAwait(false);

                        void WalkDirs(TreeNode node)
                        {
                            if (node == null || items.Count >= maxResults) return;
                            if (node.Type == "directory" && !string.IsNullOrEmpty(node.RelativePath))
                            {
                                var rel  = node.RelativePath;
                                var norm = rel.Replace('\\', '/');
                                var qn   = query.Replace('\\', '/');

                                string reason = matchMode switch
                                {
                                    "name"          => node.Name.Contains(query, cmp) ? "directory name match" : null,
                                    "path_fragment" => norm.Contains(qn, cmp) ? "path fragment match" : null,
                                    "exact_path"    => string.Equals(norm, qn, cmp) ? "exact path match" : null,
                                    "prefix"        => norm.StartsWith(qn, cmp) ? "path prefix match" : null,
                                    _               => null,
                                };

                                if (reason != null &&
                                    !(globInclude.Count > 0 && !globInclude.Any(g => MatchGlob(rel, g))) &&
                                    !globExclude.Any(g => MatchGlob(rel, g)))
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

                    var capped = items.Take(maxResults).ToList();
                    return ToResult(new RepoBrowserFindPathResponse
                    {
                        RootPath    = _settings.RepositoryRoot,
                        Query       = query,
                        MatchMode   = matchMode,
                        TargetKind  = targetKind,
                        ResultCount = capped.Count,
                        Truncated   = trunc,
                        Items       = capped,
                    });
                });
        }

        // ── Tool annotations ─────────────────────────────────────────────────────

        private void AnnotateAllTools()
        {
            // ── Repository / navigation ──────────────────────────────────────────
            Annotate("get_repository_info", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "navigation", "repository" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to get high-level repository metadata (name, root, file counts, policy).",
                    DoNotUseWhen = "Do not use to list files or search content.",
                    TypicalNextTools = new[] { "list_directory", "list_tree", "get_git_status" }
                };
                d.OutputSchema = new { type = "object", properties = new { name = new { type = "string" }, rootPath = new { type = "string" }, language = new { type = "string" }, framework = new { type = "string" } } };
            });

            Annotate("list_directory", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "navigation", "filesystem" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to enumerate files/subdirectories at a path. Best for exploring unfamiliar repository structure.",
                    DoNotUseWhen = "Do not use for searching file contents or symbols.",
                    TypicalNextTools = new[] { "read_file", "get_file_structure", "list_tree" }
                };
                d.OutputSchema = new { type = "object", properties = new { items = new { type = "array", items = new { type = "object", properties = new { name = new { type = "string" }, path = new { type = "string" }, type = new { type = "string", @enum = new[] { "file", "directory" } } } } }, totalItems = new { type = "integer" } } };
            });

            Annotate("list_tree", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "navigation", "filesystem" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to get a recursive directory tree. Useful for understanding project structure at a glance.",
                    DoNotUseWhen = "Do not use for very deep trees or large repositories where list_directory is more efficient.",
                    TypicalNextTools = new[] { "read_file", "get_file_structure" }
                };
                d.OutputSchema = new { type = "object", properties = new { tree = new { type = "object" }, totalNodes = new { type = "integer" } } };
            });

            Annotate("path_exists", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "navigation", "filesystem" };
                d.OutputSchema = new { type = "object", properties = new { exists = new { type = "boolean" }, path = new { type = "string" } } };
            });

            Annotate("get_metadata", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "navigation", "filesystem" };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, sizeBytes = new { type = "integer" }, lastModified = new { type = "string" }, encoding = new { type = "string" }, lineCount = new { type = "integer" } } };
            });

            Annotate("detect_encoding", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "navigation", "filesystem" };
                d.OutputSchema = new { type = "object", properties = new { encoding = new { type = "string" } } };
            });

            // ── Search tools ─────────────────────────────────────────────────────
            Annotate("search_text", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "search", "text" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to find literal strings across all repository files with context lines.",
                    DoNotUseWhen = "Do not use for symbol/semantic searches — prefer search_symbol or code_index.find_symbols.",
                    TypicalNextTools = new[] { "read_file_range", "read_file", "replace_text" }
                };
                d.OutputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        files = new
                        {
                            type  = "array",
                            items = new
                            {
                                type       = "object",
                                properties = new
                                {
                                    path    = new { type = "string" },
                                    matches = new
                                    {
                                        type  = "array",
                                        items = new
                                        {
                                            type       = "object",
                                            properties = new
                                            {
                                                line    = new { type = "integer" },
                                                column  = new { type = "integer" },
                                                text    = new { type = "string", description = "Matched line text" },
                                                excerpt = new { type = "string", description = "Surrounding context" }
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        totalMatchCount        = new { type = "integer" },
                        filesWithMatchesCount  = new { type = "integer" },
                        truncated              = new { type = "boolean" },
                        page                   = new { type = "integer" },
                        pageSize               = new { type = "integer" },
                        totalItems             = new { type = "integer" },
                        hasMore                = new { type = "boolean" }
                    }
                };
            });

            Annotate("search_regex", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "search", "regex" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to find text matching a .NET regular expression pattern.",
                    DoNotUseWhen = "Do not use for simple literal string searches — prefer search_text for better performance.",
                    TypicalNextTools = new[] { "read_file_range", "replace_text" }
                };
                d.OutputSchema = new { type = "object", properties = new { files = new { type = "array" }, totalMatchCount = new { type = "integer" }, filesWithMatchesCount = new { type = "integer" }, truncated = new { type = "boolean" }, page = new { type = "integer" }, pageSize = new { type = "integer" }, totalItems = new { type = "integer" }, hasMore = new { type = "boolean" } } };
            });

            Annotate("search_file_names", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "search", "filesystem" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to find files by name pattern (* and ? wildcards supported).",
                    DoNotUseWhen = "Do not use for content search — use search_text instead.",
                    TypicalNextTools = new[] { "read_file", "get_file_structure" }
                };
                d.OutputSchema = new { type = "object", properties = new { matches = new { type = "array" }, totalCount = new { type = "integer" }, truncated = new { type = "boolean" }, page = new { type = "integer" }, pageSize = new { type = "integer" }, hasMore = new { type = "boolean" } } };
            });

            Annotate("search_by_extension", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "search", "filesystem" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to list all files with specific extensions across the repository.",
                    DoNotUseWhen = "Do not use for content or symbol searches.",
                    TypicalNextTools = new[] { "read_file", "search_text" }
                };
                d.OutputSchema = new { type = "object", properties = new { matches = new { type = "array" }, totalCount = new { type = "integer" }, truncated = new { type = "boolean" }, page = new { type = "integer" }, pageSize = new { type = "integer" }, hasMore = new { type = "boolean" } } };
            });

            Annotate("search_symbol", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "search", "code-intelligence", "semantic" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to find class, method, property, or interface declarations by name using text heuristics.",
                    DoNotUseWhen = "Do not use when the code index is available — prefer code_index.find_symbols for semantic accuracy.",
                    TypicalNextTools = new[] { "read_file_range", "find_references", "code_index.get_symbol" }
                };
                d.OutputSchema = new { type = "object", properties = new { files = new { type = "array" }, totalMatchCount = new { type = "integer" }, filesWithMatchesCount = new { type = "integer" }, truncated = new { type = "boolean" } } };
            });

            Annotate("find_references", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "search", "code-intelligence", "semantic" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to find textual usages of an identifier across the repository.",
                    DoNotUseWhen = "Do not use when semantic code index is available — prefer code_index.get_references.",
                    TypicalNextTools = new[] { "read_file_range", "code_index.get_references" }
                };
                d.OutputSchema = new { type = "object", properties = new { files = new { type = "array" }, totalMatchCount = new { type = "integer" }, filesWithMatchesCount = new { type = "integer" }, truncated = new { type = "boolean" } } };
            });

            // ── File read tools ───────────────────────────────────────────────────
            Annotate("read_file", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "files" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to read the full content of a file.",
                    DoNotUseWhen = "Do not use for very large files — use read_file_range for targeted reads.",
                    TypicalNextTools = new[] { "replace_text", "search_text", "write_file" }
                };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, content = new { type = "string" }, encoding = new { type = "string" }, lines = new { type = "integer" }, sizeBytes = new { type = "integer" } } };
            });

            Annotate("read_file_range", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "files" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to read a specific range of lines from a file. More efficient than reading the entire file.",
                    DoNotUseWhen = "Do not use when you need the full file — use read_file instead.",
                    TypicalNextTools = new[] { "replace_lines", "replace_text" }
                };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, startLine = new { type = "integer" }, endLine = new { type = "integer" }, content = new { type = "string" }, totalLines = new { type = "integer" } } };
            });

            Annotate("read_multiple_files", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "files" };
                d.OutputSchema = new { type = "object", properties = new { results = new { type = "array" } } };
            });

            Annotate("get_file_hash", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "files", "integrity" };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, algorithm = new { type = "string" }, hash = new { type = "string" } } };
            });

            Annotate("get_file_structure", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "structure" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to get a structural outline (namespaces, types, members) of a code file.",
                    DoNotUseWhen = "Do not use for non-code files. For semantic accuracy prefer code_index.get_file_outline.",
                    TypicalNextTools = new[] { "read_file_range", "search_symbol" }
                };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, namespaces = new { type = "array" }, types = new { type = "array" }, methods = new { type = "array" } } };
            });

            // ── Edit tools ────────────────────────────────────────────────────────
            Annotate("write_file", d =>
            {
                d.SideEffectClass = "file-write"; d.RiskLevel = "medium";
                d.SupportsDryRun = true; d.SupportsRollback = true; d.SupportsAuditReason = true;
                d.Tags = new[] { "edit", "destructive" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to overwrite or create a file with new content. Supports dry_run and backup.",
                    DoNotUseWhen = "Do not use when you only need to change a few lines — prefer replace_text or replace_lines.",
                    TypicalNextTools = new[] { "read_file", "get_git_diff" }
                };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, success = new { type = "boolean" }, bytesWritten = new { type = "integer" }, backupPath = new { type = "string" } } };
            });

            Annotate("create_file", d =>
            {
                d.SideEffectClass = "file-write"; d.RiskLevel = "medium";
                d.SupportsDryRun = true; d.SupportsRollback = true;
                d.Tags = new[] { "edit" };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, success = new { type = "boolean" } } };
            });

            Annotate("create_directory", d =>
            {
                d.SideEffectClass = "file-write"; d.RiskLevel = "low";
                d.SupportsDryRun = true;
                d.Tags = new[] { "edit" };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, created = new { type = "boolean" } } };
            });

            Annotate("rename_path", d =>
            {
                d.SideEffectClass = "file-write"; d.RiskLevel = "medium";
                d.SupportsDryRun = true;
                d.Tags = new[] { "edit" };
                d.OutputSchema = new { type = "object", properties = new { oldPath = new { type = "string" }, newPath = new { type = "string" }, success = new { type = "boolean" } } };
            });

            Annotate("move_path", d =>
            {
                d.SideEffectClass = "file-write"; d.RiskLevel = "medium";
                d.SupportsDryRun = true;
                d.Tags = new[] { "edit" };
                d.OutputSchema = new { type = "object", properties = new { sourcePath = new { type = "string" }, destinationPath = new { type = "string" }, success = new { type = "boolean" } } };
            });

            Annotate("delete_file", d =>
            {
                d.SideEffectClass = "destructive"; d.RiskLevel = "high";
                d.SupportsDryRun = true; d.SupportsRollback = true;
                d.Tags = new[] { "edit", "destructive" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to permanently delete a file. A backup is created by default for rollback.",
                    DoNotUseWhen = "Do not use without reviewing the file contents first.",
                    TypicalNextTools = new[] { "rollback", "get_git_diff" }
                };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, success = new { type = "boolean" }, backupPath = new { type = "string" } } };
            });

            Annotate("delete_directory", d =>
            {
                d.SideEffectClass = "destructive"; d.RiskLevel = "high";
                d.SupportsDryRun = true;
                d.Tags = new[] { "edit", "destructive" };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, success = new { type = "boolean" } } };
            });

            Annotate("append_file", d =>
            {
                d.SideEffectClass = "file-write"; d.RiskLevel = "medium";
                d.SupportsDryRun = true;
                d.Tags = new[] { "edit" };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, success = new { type = "boolean" }, bytesAppended = new { type = "integer" } } };
            });

            Annotate("replace_text", d =>
            {
                d.SideEffectClass = "file-write"; d.RiskLevel = "medium";
                d.SupportsDryRun = true; d.SupportsRollback = true; d.SupportsAuditReason = true;
                d.Tags = new[] { "edit" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to replace occurrences of a literal string in a file.",
                    DoNotUseWhen = "Do not use for multi-file changes — use batch_edit instead.",
                    TypicalNextTools = new[] { "read_file", "get_git_diff" }
                };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, replacementsApplied = new { type = "integer" }, success = new { type = "boolean" } } };
            });

            Annotate("replace_lines", d =>
            {
                d.SideEffectClass = "file-write"; d.RiskLevel = "medium";
                d.SupportsDryRun = true; d.SupportsRollback = true; d.SupportsAuditReason = true;
                d.Tags = new[] { "edit" };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, success = new { type = "boolean" }, linesReplaced = new { type = "integer" } } };
            });

            Annotate("patch_file", d =>
            {
                d.SideEffectClass = "file-write"; d.RiskLevel = "medium";
                d.SupportsDryRun = true; d.SupportsRollback = true;
                d.Tags = new[] { "edit" };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, success = new { type = "boolean" }, hunksApplied = new { type = "integer" } } };
            });

            Annotate("apply_diff", d =>
            {
                d.SideEffectClass = "file-write"; d.RiskLevel = "medium";
                d.SupportsDryRun = true;
                d.Tags = new[] { "edit", "batch" };
                d.OutputSchema = new { type = "object", properties = new { success = new { type = "boolean" }, filesModified = new { type = "integer" } } };
            });

            Annotate("batch_edit", d =>
            {
                d.SideEffectClass = "file-write"; d.RiskLevel = "medium";
                d.SupportsDryRun = true; d.SupportsAuditReason = true;
                d.Tags = new[] { "edit", "batch" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to execute multiple heterogeneous edits atomically. All-or-nothing with automatic rollback on failure.",
                    DoNotUseWhen = "Do not use for a single file edit — use the specific tool instead.",
                    TypicalNextTools = new[] { "get_git_diff", "read_file" }
                };
                d.OutputSchema = new { type = "object", properties = new { success = new { type = "boolean" }, operationsApplied = new { type = "integer" }, rollbackToken = new { type = "string" } } };
            });

            Annotate("preview_changes", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.SupportsDryRun = true;
                d.Tags = new[] { "edit", "preview" };
                d.OutputSchema = new { type = "object", properties = new { diff = new { type = "string" }, approvalToken = new { type = "string" } } };
            });

            Annotate("rollback", d =>
            {
                d.SideEffectClass = "file-write"; d.RiskLevel = "medium";
                d.Tags = new[] { "edit", "rollback" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to restore a file from a backup created by a prior write/delete operation.",
                    DoNotUseWhen = "Do not use without a valid rollback token from a prior operation.",
                    TypicalNextTools = new[] { "read_file", "get_git_diff" }
                };
                d.OutputSchema = new { type = "object", properties = new { path = new { type = "string" }, success = new { type = "boolean" } } };
            });

            // ── Git tools ─────────────────────────────────────────────────────────
            Annotate("get_git_status", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "git" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to get the current git status: branch, HEAD, staged/unstaged changes.",
                    DoNotUseWhen = null,
                    TypicalNextTools = new[] { "get_git_diff", "get_git_log" }
                };
                d.OutputSchema = new { type = "object", properties = new { currentBranch = new { type = "string" }, headCommit = new { type = "string" }, files = new { type = "array" } } };
            });

            Annotate("get_git_diff", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "git" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to get a unified diff for working-tree or staged changes.",
                    DoNotUseWhen = null,
                    TypicalNextTools = new[] { "get_git_status", "write_file", "replace_text" }
                };
                d.OutputSchema = new { type = "object", properties = new { diff = new { type = "string" }, scope = new { type = "string" } } };
            });

            Annotate("get_git_diff_file", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "git" };
                d.OutputSchema = new { type = "object", properties = new { diff = new { type = "string" }, path = new { type = "string" } } };
            });

            Annotate("get_git_log", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "git" };
                d.OutputSchema = new { type = "object", properties = new { commits = new { type = "array" }, count = new { type = "integer" } } };
            });

            Annotate("get_commit", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "git" };
                d.OutputSchema = new { type = "object", properties = new { sha = new { type = "string" }, author = new { type = "string" }, message = new { type = "string" }, files = new { type = "array" }, diff = new { type = "string" } } };
            });

            // ── Command tools ─────────────────────────────────────────────────────
            Annotate("run_build", d =>
            {
                d.SideEffectClass = "process-execution"; d.RiskLevel = "medium";
                d.Tags = new[] { "command", "ci", "build" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to run the configured build command (dotnet build or msbuild).",
                    DoNotUseWhen = "Do not use with untrusted command strings — only allowed prefixes are accepted.",
                    TypicalNextTools = new[] { "run_tests", "get_git_diff" }
                };
                d.OutputSchema = new { type = "object", properties = new { exitCode = new { type = "integer" }, output = new { type = "string" }, success = new { type = "boolean" }, durationMs = new { type = "integer" } } };
            });

            Annotate("run_tests", d =>
            {
                d.SideEffectClass = "process-execution"; d.RiskLevel = "medium";
                d.Tags = new[] { "command", "ci", "test" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to run the configured test command (dotnet test).",
                    DoNotUseWhen = "Do not use with untrusted command strings.",
                    TypicalNextTools = new[] { "run_build", "read_file" }
                };
                d.OutputSchema = new { type = "object", properties = new { exitCode = new { type = "integer" }, output = new { type = "string" }, passed = new { type = "integer" }, failed = new { type = "integer" }, success = new { type = "boolean" } } };
            });

            Annotate("run_command", d =>
            {
                d.SideEffectClass = "process-execution"; d.RiskLevel = "high";
                d.Tags = new[] { "command", "dangerous" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to run a custom command from the AllowedCommands whitelist.",
                    DoNotUseWhen = "Do not use commands not in the AllowedCommands list — they will be rejected.",
                    TypicalNextTools = new[] { "run_build", "run_tests" }
                };
                d.OutputSchema = new { type = "object", properties = new { exitCode = new { type = "integer" }, output = new { type = "string" }, success = new { type = "boolean" } } };
            });

            // ── Code Index tools ──────────────────────────────────────────────────
            Annotate("code_index.build", d =>
            {
                d.SideEffectClass = "service-state-write"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "write" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to build or rebuild the structural code index for a repository.",
                    DoNotUseWhen = "Do not use if an up-to-date index already exists — prefer code_index.update.",
                    TypicalNextTools = new[] { "code_index.list_files", "code_index.find_symbols", "code_index.get_snapshot_info" }
                };
                d.OutputSchema = new { type = "object", properties = new { snapshotId = new { type = "string" }, fileCount = new { type = "integer" }, symbolCount = new { type = "integer" }, durationMs = new { type = "integer" } } };
            });

            Annotate("code_index.get_status", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen      = "Use to quickly check whether a repository has been indexed.",
                    DoNotUseWhen = "Prefer code_index.get_index_status which includes richer metadata (file/symbol/reference counts and delta information).",
                };
                d.OutputSchema = new { type = "object", properties = new { state = new { type = "string" }, latestSnapshotId = new { type = "string" }, lastCompletedAt = new { type = "string" } } };
            });

            Annotate("code_index.list_files", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing" };
                d.OutputSchema = new { type = "object", properties = new { snapshotId = new { type = "string" }, items = new { type = "array" }, page = new { type = "integer" }, pageSize = new { type = "integer" }, totalItems = new { type = "integer" }, hasMore = new { type = "boolean" } } };
            });

            Annotate("code_index.get_file_outline", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "structure" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to get a precise structural outline of a file from the semantic code index.",
                    DoNotUseWhen = "Do not use if the file has not been indexed — fall back to get_file_structure.",
                    TypicalNextTools = new[] { "read_file_range", "code_index.find_symbols" }
                };
                d.OutputSchema = new { type = "object", properties = new { filePath = new { type = "string" }, nodes = new { type = "array" } } };
            });

            Annotate("code_index.get_snapshot_info", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing" };
                d.OutputSchema = new { type = "object", properties = new { snapshotId = new { type = "string" }, rootPath = new { type = "string" }, fileCount = new { type = "integer" }, symbolCount = new { type = "integer" }, createdAt = new { type = "string" } } };
            });

            Annotate("code_index.list_roots", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen      = "Use to get a simple list of indexed root paths.",
                    DoNotUseWhen = "Prefer code_index.list_repositories which includes per-repo status (state, snapshot ID, file/symbol counts).",
                };
                d.OutputSchema = new { type = "object", properties = new { roots = new { type = "array", items = new { type = "string" } } } };
            });

            Annotate("code_index.get_language_capabilities", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing" };
                d.OutputSchema = new { type = "object", properties = new { adapters = new { type = "array" } } };
            });

            Annotate("code_index.find_symbols", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to search the symbol index by name or qualified name with semantic accuracy.",
                    DoNotUseWhen = "Do not use for plain text search — use search_text or search_symbol instead.",
                    TypicalNextTools = new[] { "code_index.get_symbol", "code_index.get_symbol_occurrences", "code_index.get_dependencies" }
                };
                d.OutputSchema = new
                {
                    type       = "object",
                    properties = new
                    {
                        snapshotId = new { type = "string" },
                        items      = new
                        {
                            type  = "array",
                            items = new
                            {
                                type       = "object",
                                properties = new
                                {
                                    symbolId      = new { type = "string" },
                                    name          = new { type = "string" },
                                    kind          = new { type = "string" },
                                    qualifiedName = new { type = "string" },
                                    filePath      = new { type = "string" },
                                    line          = new { type = "integer" }
                                }
                            }
                        },
                        page       = new { type = "integer" },
                        pageSize   = new { type = "integer" },
                        totalItems = new { type = "integer" },
                        hasMore    = new { type = "boolean" }
                    }
                };
            });

            Annotate("code_index.get_symbol", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to retrieve full details for a single logical symbol by its stable symbol ID.",
                    DoNotUseWhen = "Do not use without a valid symbol ID from code_index.find_symbols.",
                    TypicalNextTools = new[] { "code_index.get_symbol_occurrences", "code_index.get_dependencies", "code_index.get_references" }
                };
                d.OutputSchema = new { type = "object", properties = new { snapshotId = new { type = "string" }, symbol = new { type = "object" }, primaryOccurrence = new { type = "object" }, error = new { type = "object", properties = new { code = new { type = "string" }, message = new { type = "string" }, context = new { type = "object" } } } } };
            });

            Annotate("code_index.get_symbol_occurrences", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.OutputSchema = new { type = "object", properties = new { symbolId = new { type = "string" }, occurrences = new { type = "array" }, page = new { type = "integer" }, pageSize = new { type = "integer" }, totalItems = new { type = "integer" }, hasMore = new { type = "boolean" } } };
            });

            Annotate("code_index.get_symbol_children", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.OutputSchema = new { type = "object", properties = new { symbolId = new { type = "string" }, children = new { type = "array" }, page = new { type = "integer" }, pageSize = new { type = "integer" }, totalItems = new { type = "integer" }, hasMore = new { type = "boolean" } } };
            });

            Annotate("code_index.get_type_members", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.OutputSchema = new { type = "object", properties = new { typeSymbolId = new { type = "string" }, constructors = new { type = "array" }, methods = new { type = "array" }, properties = new { type = "array" }, fields = new { type = "array" }, events = new { type = "array" } } };
            });

            Annotate("code_index.resolve_location", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.OutputSchema = new { type = "object", properties = new { filePath = new { type = "string" }, line = new { type = "integer" }, column = new { type = "integer" } } };
            });

            Annotate("code_index.get_namespace_tree", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.OutputSchema = new { type = "object", properties = new { nodes = new { type = "array" } } };
            });

            Annotate("code_index.get_imports", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.OutputSchema = new { type = "object", properties = new { filePath = new { type = "string" }, imports = new { type = "array" }, page = new { type = "integer" }, pageSize = new { type = "integer" }, totalItems = new { type = "integer" }, hasMore = new { type = "boolean" } } };
            });

            Annotate("code_index.get_references", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to get outgoing and incoming reference edges for a logical symbol.",
                    DoNotUseWhen = "Do not use without a valid symbol ID.",
                    TypicalNextTools = new[] { "code_index.get_symbol", "code_index.get_dependencies" }
                };
                d.OutputSchema = new
                {
                    type       = "object",
                    properties = new
                    {
                        snapshotId   = new { type = "string" },
                        symbolId     = new { type = "string" },
                        outgoingRefs = new
                        {
                            type  = "array",
                            items = new
                            {
                                type       = "object",
                                properties = new
                                {
                                    targetSymbolId   = new { type = "string" },
                                    targetName       = new { type = "string" },
                                    kind             = new { type = "string" },
                                    confidence       = new { type = "number" },
                                    resolutionMethod = new { type = "string" }
                                }
                            }
                        },
                        incomingRefs = new
                        {
                            type  = "array",
                            items = new
                            {
                                type       = "object",
                                properties = new
                                {
                                    targetSymbolId   = new { type = "string" },
                                    targetName       = new { type = "string" },
                                    kind             = new { type = "string" },
                                    confidence       = new { type = "number" },
                                    resolutionMethod = new { type = "string" }
                                }
                            }
                        },
                        page         = new { type = "integer" },
                        pageSize     = new { type = "integer" },
                        totalOutgoing= new { type = "integer" },
                        totalIncoming= new { type = "integer" },
                        hasMore      = new { type = "boolean" }
                    }
                };
            });

            Annotate("code_index.get_file_references", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.OutputSchema = new { type = "object", properties = new { filePath = new { type = "string" }, references = new { type = "array" }, page = new { type = "integer" }, pageSize = new { type = "integer" }, totalItems = new { type = "integer" }, hasMore = new { type = "boolean" } } };
            });

            Annotate("code_index.get_dependencies", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to get outbound hard dependency projections for a logical symbol.",
                    DoNotUseWhen = "Do not use without a valid symbol ID.",
                    TypicalNextTools = new[] { "code_index.get_symbol", "code_index.get_dependents" }
                };
                d.OutputSchema = new { type = "object", properties = new { snapshotId = new { type = "string" }, symbolId = new { type = "string" }, dependencies = new { type = "array" }, page = new { type = "integer" }, pageSize = new { type = "integer" }, totalItems = new { type = "integer" }, hasMore = new { type = "boolean" } } };
            });

            Annotate("code_index.get_dependents", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.OutputSchema = new { type = "object", properties = new { snapshotId = new { type = "string" }, symbolId = new { type = "string" }, dependents = new { type = "array" }, page = new { type = "integer" }, pageSize = new { type = "integer" }, totalItems = new { type = "integer" }, hasMore = new { type = "boolean" } } };
            });

            Annotate("code_index.get_symbol_relationships", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.OutputSchema = new { type = "object", properties = new { symbolId = new { type = "string" }, children = new { type = "array" }, references = new { type = "array" }, dependencies = new { type = "array" }, dependents = new { type = "array" } } };
            });

            Annotate("code_index.get_file_dependencies", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "semantic" };
                d.OutputSchema = new { type = "object", properties = new { filePath = new { type = "string" }, dependencyFiles = new { type = "array" }, totalEdges = new { type = "integer" } } };
            });

            Annotate("code_index.update", d =>
            {
                d.SideEffectClass = "service-state-write"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing", "write" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to incrementally update the code index after file changes.",
                    DoNotUseWhen = "Do not use for an initial index build — use code_index.build instead.",
                    TypicalNextTools = new[] { "code_index.get_index_status", "code_index.find_symbols" }
                };
                d.OutputSchema = new { type = "object", properties = new { snapshotId = new { type = "string" }, filesProcessed = new { type = "integer" }, symbolsAdded = new { type = "integer" }, symbolsRemoved = new { type = "integer" } } };
            });

            Annotate("code_index.get_index_status", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing" };
                d.OutputSchema = new { type = "object", properties = new { rootPath = new { type = "string" }, state = new { type = "string" }, latestSnapshotId = new { type = "string" }, fileCount = new { type = "integer" }, symbolCount = new { type = "integer" } } };
            });

            Annotate("code_index.list_repositories", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "code-intelligence", "indexing" };
                d.OutputSchema = new { type = "object", properties = new { repositories = new { type = "array" } } };
            });

            Annotate("code_index.clear_repository", d =>
            {
                d.SideEffectClass = "destructive"; d.RiskLevel = "medium";
                d.Tags = new[] { "code-intelligence", "indexing", "destructive" };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen = "Use to remove all stored index state for a repository root. Cannot be undone.",
                    DoNotUseWhen = "Do not use unless you want to permanently clear the index for a repository.",
                    TypicalNextTools = new[] { "code_index.build" }
                };
                d.OutputSchema = new { type = "object", properties = new { rootPath = new { type = "string" }, removedSnapshots = new { type = "integer" }, status = new { type = "string" } } };
            });

            Annotate("code_index.ping", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "health" };
                d.OutputSchema = new { type = "object", properties = new { status = new { type = "string" }, service = new { type = "string" }, version = new { type = "string" }, languages = new { type = "array", items = new { type = "string" } }, tool_chains = new { type = "array", items = new { type = "string" } } } };
            });

            // ── Repo browser ─────────────────────────────────────────────────────
            Annotate("repo_browser.print_tree", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "repo_browser", "navigation", "filesystem" };
                d.OutputSchema = new { type = "object", properties = new { rootPath = new { type = "string" }, relativePath = new { type = "string" }, maxDepth = new { type = "integer" }, entryCount = new { type = "integer" }, truncated = new { type = "boolean" }, items = new { type = "array" } } };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen      = "Use to inspect repository or directory structure when the exact file is not yet known.",
                    DoNotUseWhen = "Do not use when you already know the exact file path or need file contents.",
                    TypicalNextTools = new[] { "repo_browser.find_path", "repo_browser.grep", "repo_browser.read_file" },
                };
            });

            Annotate("repo_browser.grep", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "repo_browser", "search", "content" };
                d.OutputSchema = new { type = "object", properties = new { rootPath = new { type = "string" }, query = new { type = "string" }, mode = new { type = "string" }, matchCount = new { type = "integer" }, truncated = new { type = "boolean" }, items = new { type = "array" } } };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen      = "Use when you know what text, symbol, or pattern to search for but do not know which file contains it.",
                    DoNotUseWhen = "Do not use when you need file contents for a known file path or only need directory structure.",
                    TypicalNextTools = new[] { "repo_browser.read_file", "repo_browser.find_path", "repo_browser.print_tree" },
                };
            });

            Annotate("repo_browser.read_file", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "repo_browser", "file", "content" };
                d.OutputSchema = new { type = "object", properties = new { rootPath = new { type = "string" }, filePath = new { type = "string" }, exists = new { type = "boolean" }, encoding = new { type = "string" }, sizeBytes = new { type = "integer" }, truncated = new { type = "boolean" }, startLine = new { type = "integer" }, endLine = new { type = "integer" }, content = new { type = "string" } } };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen      = "Use when you already know the exact file path and need to inspect its contents. Read partial ranges first for large files.",
                    DoNotUseWhen = "Do not use when the file path is still uncertain — use repo_browser.find_path or repo_browser.grep first.",
                    TypicalNextTools = new[] { "repo_browser.grep", "repo_browser.print_tree", "repo_browser.find_path" },
                };
            });

            Annotate("repo_browser.find_path", d =>
            {
                d.SideEffectClass = "read-only"; d.RiskLevel = "low";
                d.Tags = new[] { "repo_browser", "navigation", "filesystem" };
                d.OutputSchema = new { type = "object", properties = new { rootPath = new { type = "string" }, query = new { type = "string" }, matchMode = new { type = "string" }, targetKind = new { type = "string" }, resultCount = new { type = "integer" }, truncated = new { type = "boolean" }, items = new { type = "array" } } };
                d.UsageGuidance = new McpUsageGuidance
                {
                    UseWhen      = "Use when you know the file or directory name (or part of its path) but not its exact location in the repository.",
                    DoNotUseWhen = "Do not use when you need to search file contents or already know the exact path.",
                    TypicalNextTools = new[] { "repo_browser.read_file", "repo_browser.print_tree", "repo_browser.grep" },
                };
            });
        }

        // ── Registration helper ──────────────────────────────────────────────────
        private void Add(
            string name,
            string category,
            string description,
            McpInputSchema schema,
            Func<Dictionary<string, object>, CancellationToken, Task<McpToolCallResult>> handler,
            bool isDestructive  = false,
            bool isDisabled     = false,
            string disabledReason = null)
        {
            _tools[name] = new ToolEntry
            {
                Definition = new McpToolDefinition
                {
                    Name           = name,
                    Description    = description,
                    InputSchema    = schema,
                    Category       = category,
                    IsDestructive  = isDestructive,
                    IsDisabled     = isDisabled,
                    DisabledReason = disabledReason
                },
                Handler = handler
            };
        }

        // ── Schema builder helpers ───────────────────────────────────────────────

        private static McpInputSchema Schema(params (string Name, McpPropertySchema Schema, bool Required)[] props)
        {
            var schema = new McpInputSchema();
            foreach (var (name, propSchema, req) in props)
            {
                schema.Properties[name] = propSchema;
                if (req) schema.Required.Add(name);
            }
            return schema;
        }

        private static (string, McpPropertySchema, bool) Prop(
            string name,
            string type,
            string description,
            bool   required = false,
            object def      = null,
            string[] enums  = null)
        {
            var prop = new McpPropertySchema
            {
                Type        = type,
                Description = description,
                Default     = def,
                Enum        = enums != null ? new System.Collections.Generic.List<string>(enums) : null
            };
            return (name, prop, required);
        }

        private static (string, McpPropertySchema, bool) PropWithItems(
            string name,
            string description,
            bool   required = false,
            object items    = null)
        {
            var prop = new McpPropertySchema
            {
                Type        = "array",
                Description = description,
                Items       = items
            };
            return (name, prop, required);
        }

        // ── Argument extraction helpers ──────────────────────────────────────────

        private static string Str(Dictionary<string, object> args, string key, string def = null)
        {
            if (!args.TryGetValue(key, out var val) || val == null)
                return def;
            return val.ToString();
        }

        private static bool Bool(Dictionary<string, object> args, string key, bool def = false)
        {
            if (!args.TryGetValue(key, out var val) || val == null) return def;
            if (val is bool b) return b;
            if (bool.TryParse(val.ToString(), out var parsed)) return parsed;
            return def;
        }

        private static int Int(Dictionary<string, object> args, string key, int def = 0)
        {
            if (!args.TryGetValue(key, out var val) || val == null) return def;
            if (val is long l) return (int)l;
            if (val is int  i) return i;
            if (int.TryParse(val.ToString(), out var parsed)) return parsed;
            return def;
        }

        private static int? NullableInt(Dictionary<string, object> args, string key)
        {
            if (!args.TryGetValue(key, out var val) || val == null) return null;
            if (val is long l) return (int)l;
            if (val is int  i) return i;
            if (int.TryParse(val.ToString(), out var parsed)) return parsed;
            return null;
        }

        private static List<string> StrList(Dictionary<string, object> args, string key)
        {
            if (!args.TryGetValue(key, out var val) || val == null) return new List<string>();
            if (val is List<string> ls) return ls;
            if (val is Newtonsoft.Json.Linq.JArray ja) return ja.ToObject<List<string>>();
            if (val is IEnumerable<object> en) return en.Select(x => x?.ToString()).Where(x => x != null).ToList();
            return new List<string>();
        }

        private static EditOptions EditOpts(Dictionary<string, object> args) =>
            new EditOptions
            {
                DryRun       = Bool(args, "dry_run", false),
                CreateBackup = Bool(args, "create_backup", true),
                ChangeReason = Str(args, "change_reason", null),
                SessionId    = Str(args, "session_id", null)
            };

        /// <summary>
        /// Converts the arguments dictionary to the target type via JSON round-trip.
        /// Used for complex request types whose properties map directly to argument keys.
        /// </summary>
        private static T FromArgs<T>(Dictionary<string, object> args)
        {
            var json = JsonConvert.SerializeObject(args,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            return JsonConvert.DeserializeObject<T>(json);
        }

        /// <summary>Creates a structured error result with a machine-classifiable error code.</summary>
        private static McpToolCallResult ErrorResult(string code, string message, object? context = null)
            => ToResult(new { error = new StewardessMCPService.CodeIndexing.Query.McpError(code, message, context) });

        /// <summary>Classifies an error message into a machine-readable error code.</summary>
        private static string ClassifyError(string message) => message switch
        {
            var m when m.Contains("not found", StringComparison.OrdinalIgnoreCase) => StewardessMCPService.CodeIndexing.Query.McpErrorCode.SymbolNotFound,
            var m when m.Contains("not indexed", StringComparison.OrdinalIgnoreCase) => StewardessMCPService.CodeIndexing.Query.McpErrorCode.RepositoryNotIndexed,
            var m when m.Contains("snapshot", StringComparison.OrdinalIgnoreCase) => StewardessMCPService.CodeIndexing.Query.McpErrorCode.SnapshotNotFound,
            var m when m.Contains("not supported", StringComparison.OrdinalIgnoreCase) => StewardessMCPService.CodeIndexing.Query.McpErrorCode.CapabilityNotSupported,
            _ => StewardessMCPService.CodeIndexing.Query.McpErrorCode.InternalError,
        };

        // ── Pagination helpers ───────────────────────────────────────────────────

        private static void ApplyPagination(SearchResponse result, int page, int pageSize)
        {
            result.TotalItems = result.Files.Count;
            result.Page       = Math.Max(1, page);
            pageSize          = Math.Min(200, Math.Max(1, pageSize));
            result.PageSize   = pageSize;
            var skip          = (result.Page - 1) * pageSize;
            result.HasMore    = skip + pageSize < result.Files.Count;
            result.Files      = result.Files.Skip(skip).Take(pageSize).ToList();
        }

        private static void ApplyFileNamePagination(FileNameSearchResponse result, int page, int pageSize)
        {
            result.Page     = Math.Max(1, page);
            pageSize        = Math.Min(200, Math.Max(1, pageSize));
            result.PageSize = pageSize;
            var skip        = (result.Page - 1) * pageSize;
            result.HasMore  = skip + pageSize < result.Matches.Count;
            result.Matches  = result.Matches.Skip(skip).Take(pageSize).ToList();
        }

        // ── Annotation helper ─────────────────────────────────────────────────────

        private void Annotate(string name, Action<McpToolDefinition> configure)
        {
            if (_tools.TryGetValue(name, out var entry))
                configure(entry.Definition);
        }

        // ── Glob and line-number helpers ─────────────────────────────────────────

        /// <summary>
        /// Returns true when the repository-relative <paramref name="path"/>
        /// matches the glob <paramref name="pattern"/>.
        /// Supports <c>*</c> (any segment characters) and <c>?</c> (single character).
        /// Matching is case-insensitive.
        /// </summary>
        private static bool MatchGlob(string path, string pattern)
        {
            path    = path.Replace('\\', '/');
            pattern = pattern.Replace('\\', '/');
            return GlobMatch(path, pattern, 0, 0);
        }

        private static bool GlobMatch(string str, string pat, int si, int pi)
        {
            while (pi < pat.Length)
            {
                if (pat[pi] == '*')
                {
                    while (pi < pat.Length && pat[pi] == '*') pi++;
                    if (pi == pat.Length) return true;
                    for (int i = si; i <= str.Length; i++)
                        if (GlobMatch(str, pat, i, pi)) return true;
                    return false;
                }
                if (si >= str.Length) return false;
                if (pat[pi] != '?' && char.ToLowerInvariant(pat[pi]) != char.ToLowerInvariant(str[si])) return false;
                si++; pi++;
            }
            return si == str.Length;
        }

        /// <summary>Prepends "N: " line numbers to each line of <paramref name="content"/>.</summary>
        private static string AddLineNumbers(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;
            var lines = content.Split('\n');
            var sb = new System.Text.StringBuilder(content.Length + lines.Length * 6);
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append(i + 1).Append(": ").Append(lines[i]);
                if (i < lines.Length - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        // ── Result factory ───────────────────────────────────────────────────────

        private static McpToolCallResult ToResult(object data)
        {
            var settings = new JsonSerializerSettings
            {
                Formatting        = Formatting.Indented,
                Converters        = { new Newtonsoft.Json.Converters.StringEnumConverter() },
                ContractResolver  = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            };
            var json = JsonConvert.SerializeObject(data, settings);
            return new McpToolCallResult
            {
                Content = new List<McpContent> { McpContent.FromText(json) }
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StewardessMCPServive.CodeIndexing.Indexing;
using StewardessMCPServive.CodeIndexing.Query;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Models;
using StewardessMCPServive.Services;

namespace StewardessMCPServive.Mcp
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
                    Prop("max_results", "integer", "Max results to return (default 100).", def: 100)),
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
                    Prop("max_results", "integer", "Max results (default 100).", def: 100)),
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
                    return ToResult(result);
                });

            Add("search_file_names",
                category: "search",
                description: "Finds files whose names match a substring or wildcard pattern.",
                schema: Schema(
                    Prop("pattern",     "string", "Filename pattern (substring or glob).", required: true),
                    Prop("search_path", "string", "Restrict to subdirectory."),
                    Prop("max_results", "integer", "Max results (default 100).", def: 100)),
                handler: async (args, ct) =>
                {
                    var req = new SearchFileNamesRequest
                    {
                        Pattern    = Str(args, "pattern"),
                        SearchPath = Str(args, "search_path", ""),
                        MaxResults = Int(args, "max_results", 100)
                    };
                    var result = await _search.SearchFileNamesAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("search_by_extension",
                category: "search",
                description: "Returns all files matching one or more file extensions.",
                schema: Schema(
                    Prop("extensions",  "array",  "Extensions to find, e.g. [\".cs\",\".js\"].", required: true),
                    Prop("search_path", "string", "Restrict to subdirectory."),
                    Prop("max_results", "integer","Max results (default 200).", def: 200)),
                handler: async (args, ct) =>
                {
                    var req = new SearchByExtensionRequest
                    {
                        Extensions = StrList(args, "extensions"),
                        SearchPath = Str(args, "search_path", ""),
                        MaxResults = Int(args, "max_results", 200)
                    };
                    var result = await _search.SearchByExtensionAsync(req, ct).ConfigureAwait(false);
                    return ToResult(result);
                });

            Add("search_symbol",
                category: "search",
                description: "Best-effort symbol search using text heuristics (class, method, interface declarations).",
                schema: Schema(
                    Prop("symbol_name", "string",  "Symbol name to search for.", required: true),
                    Prop("search_path", "string",  "Restrict to subdirectory."),
                    Prop("extensions",  "array",   "File extensions."),
                    Prop("max_results", "integer", "Max results (default 50).", def: 50)),
                handler: async (args, ct) =>
                {
                    var req = new SearchSymbolRequest
                    {
                        SymbolName = Str(args, "symbol_name"),
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
                    Prop("algorithm", "string", "Hash algorithm: SHA256 (default), MD5, SHA1.")),
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
                category: "files",
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
                    Prop("encoding",     "string",  "Encoding: utf-8 (default), utf-8-bom, utf-16, ascii."),
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
                    Prop("edits",   "array",   "Array of edit items. Each item must have 'operation' and 'path'.", required: true),
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
                    Prop("scope",         "string",  "Diff scope: unstaged (default), staged, head.", def: "unstaged"),
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
                    Prop("scope", "string", "Diff scope: unstaged (default), staged, head.", def: "unstaged")),
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
                    Prop("sha",          "string",  "Full or abbreviated commit SHA (required, minimum 4 hex chars)."),
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
                    Prop("configuration",    "string",  "Build configuration: Debug (default) or Release.", def: "Debug"),
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
                    Prop("parse_mode",   "string",  "Depth of parsing: OutlineOnly, Declarations (default), DeclarationsAndReferences.", def: "Declarations"),
                    Prop("force_rebuild","boolean", "Discard existing index and rebuild from scratch (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var modeStr = Str(args, "parse_mode", "Declarations");
                    var mode = Enum.TryParse<StewardessMCPServive.CodeIndexing.Model.Structural.ParseMode>(modeStr, true, out var m)
                        ? m : StewardessMCPServive.CodeIndexing.Model.Structural.ParseMode.Declarations;

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
                description: "Returns the current indexing state for a repository root: state, latest snapshot ID, and last completed timestamp.",
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
                    var req = new StewardessMCPServive.CodeIndexing.Query.ListFilesRequest
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
                    var req = new StewardessMCPServive.CodeIndexing.Query.GetFileOutlineRequest
                    {
                        FilePath       = Str(args, "file_path"),
                        SnapshotId     = Str(args, "snapshot_id", null),
                        MaxDepth       = maxDepth == 0 ? null : maxDepth,
                        IncludeSourceSpans = Bool(args, "include_spans", true),
                        IncludeConfidence  = Bool(args, "include_confidence", false),
                    };
                    var result = await query.GetFileOutlineAsync(req, ct).ConfigureAwait(false);
                    if (result == null)
                        return ToResult(new { error = "File not found in index", file_path = req.FilePath });
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
                        return ToResult(new { error = "No snapshot found" });
                    return ToResult(result);
                });

            Add("code_index.list_roots",
                category: "code_index",
                description: "Returns the list of repository root paths that have a published code index snapshot.",
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
                    var adapters = new StewardessMCPServive.CodeIndexing.Parsers.Abstractions.IParserAdapter[]
                    {
                        new StewardessMCPServive.Parsers.CSharp.CSharpParserAdapter(),
                        new StewardessMCPServive.CodeIndexing.Parsers.Python.PythonParserAdapter(),
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
                    Prop("match_mode",   "string",  "Match mode: exact, prefix (default), or contains.", def: "prefix"),
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
                    var req = new StewardessMCPServive.CodeIndexing.Query.FindSymbolsRequest
                    {
                        QueryText             = Str(args, "query_text"),
                        SnapshotId            = Str(args, "snapshot_id", null) ?? (Str(args, "root_path", null) != null ? null : null),
                        MatchMode             = Str(args, "match_mode", "prefix"),
                        LanguageFilter        = langStr != null ? new[] { langStr } : null,
                        KindFilter            = kindStr != null && System.Enum.TryParse<StewardessMCPServive.CodeIndexing.Model.Semantic.SymbolKind>(kindStr, true, out var k)
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
                             "including kind, qualified name, parent, primary location, and optional members summary.",
                schema: Schema(
                    Prop("symbol_id",              "string",  "Stable symbol ID to retrieve.", required: true),
                    Prop("snapshot_id",            "string",  "Specific snapshot ID to query."),
                    Prop("include_primary_occurrence", "boolean", "Include primary declaration location (default true).", def: true),
                    Prop("include_members_summary",   "boolean", "Include members summary for type symbols (default true).", def: true)),
                handler: async (args, ct) =>
                {
                    var req = new StewardessMCPServive.CodeIndexing.Query.GetSymbolRequest
                    {
                        SymbolId                = Str(args, "symbol_id"),
                        SnapshotId              = Str(args, "snapshot_id", null),
                        IncludePrimaryOccurrence = Bool(args, "include_primary_occurrence", true),
                        IncludeMembersSummary   = Bool(args, "include_members_summary", true),
                    };
                    var result = await query.GetSymbolAsync(req, ct).ConfigureAwait(false);
                    if (result?.Symbol == null)
                        return ToResult(new { error = "Symbol not found", symbol_id = req.SymbolId });
                    return ToResult(result);
                });

            Add("code_index.get_symbol_occurrences",
                category: "code_index",
                description: "Returns all occurrences (declarations, references, definitions) of a symbol across the indexed files. " +
                             "Each occurrence carries a file path and source line/column.",
                schema: Schema(
                    Prop("symbol_id",  "string", "Stable symbol ID to look up occurrences for.", required: true),
                    Prop("snapshot_id","string", "Specific snapshot ID to query."),
                    Prop("role",       "string", "Filter to a specific occurrence role: Declaration, Reference, Definition, etc.")),
                handler: async (args, ct) =>
                {
                    var roleStr = Str(args, "role", null);
                    StewardessMCPServive.CodeIndexing.Model.Semantic.OccurrenceRole[]? roleFilter = null;
                    if (roleStr != null && System.Enum.TryParse<StewardessMCPServive.CodeIndexing.Model.Semantic.OccurrenceRole>(roleStr, true, out var role))
                        roleFilter = new[] { role };
                    var req = new StewardessMCPServive.CodeIndexing.Query.GetSymbolOccurrencesRequest
                    {
                        SymbolId   = Str(args, "symbol_id"),
                        SnapshotId = Str(args, "snapshot_id", null),
                        RoleFilter = roleFilter,
                    };
                    var result = await query.GetSymbolOccurrencesAsync(req, ct).ConfigureAwait(false);
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
                    Prop("include_nested_types","boolean", "Include nested type symbols (default true).", def: true)),
                handler: async (args, ct) =>
                {
                    var kindStr = Str(args, "kind", null);
                    var req = new StewardessMCPServive.CodeIndexing.Query.GetSymbolChildrenRequest
                    {
                        SymbolId           = Str(args, "symbol_id"),
                        SnapshotId         = Str(args, "snapshot_id", null),
                        KindFilter         = kindStr != null && System.Enum.TryParse<StewardessMCPServive.CodeIndexing.Model.Semantic.SymbolKind>(kindStr, true, out var k)
                                                 ? new[] { k } : null,
                        IncludeNestedTypes = Bool(args, "include_nested_types", true),
                    };
                    var result = await query.GetSymbolChildrenAsync(req, ct).ConfigureAwait(false);
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
                    var req = new StewardessMCPServive.CodeIndexing.Query.GetTypeMembersRequest
                    {
                        TypeSymbolId       = Str(args, "type_symbol_id"),
                        SnapshotId         = Str(args, "snapshot_id", null),
                        IncludeAccessors   = Bool(args, "include_accessors", true),
                        IncludeNestedTypes = Bool(args, "include_nested_types", true),
                    };
                    var result = await query.GetTypeMembersAsync(req, ct).ConfigureAwait(false);
                    if (result == null)
                        return ToResult(new { error = "Type symbol not found or not a type", symbol_id = req.TypeSymbolId });
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
                    var req = new StewardessMCPServive.CodeIndexing.Query.ResolveLocationRequest
                    {
                        SymbolId     = Str(args, "symbol_id", null),
                        OccurrenceId = Str(args, "occurrence_id", null),
                        SnapshotId   = Str(args, "snapshot_id", null),
                    };
                    var result = await query.ResolveLocationAsync(req, ct).ConfigureAwait(false);
                    if (result.Error != null)
                        return ToResult(new { error = result.Error });
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
                    var req = new StewardessMCPServive.CodeIndexing.Query.GetNamespaceTreeRequest
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
                    Prop("root_path",   "string",  "Repository root path; used to resolve the latest snapshot.")),
                handler: async (args, ct) =>
                {
                    var filePath = Str(args, "file_path", null);
                    if (string.IsNullOrEmpty(filePath))
                        return ToResult(new { error = "file_path is required." });
                    var req = new StewardessMCPServive.CodeIndexing.Query.GetImportsRequest
                    {
                        FilePath   = filePath,
                        SnapshotId = Str(args, "snapshot_id", null),
                        RootPath   = Str(args, "root_path", null),
                    };
                    var result = await query.GetImportsAsync(req, ct).ConfigureAwait(false);
                    if (result.Error != null)
                        return ToResult(new { error = result.Error });
                    return ToResult(result);
                });

            Add("code_index.get_references",
                category: "code_index",
                description: "Returns reference edges for a specific logical symbol. " +
                             "Outgoing references show what other symbols this symbol depends on " +
                             "(e.g., base types, field types, method parameter types). " +
                             "Incoming references show what symbols depend on this symbol.",
                schema: Schema(
                    Prop("symbol_id",        "string",  "Symbol ID to query references for (required)."),
                    Prop("snapshot_id",      "string",  "Specific snapshot ID to query."),
                    Prop("include_outgoing", "boolean", "Include outgoing references (default true).", def: true),
                    Prop("include_incoming", "boolean", "Include incoming references (default false).", def: false)),
                handler: async (args, ct) =>
                {
                    var symbolId = Str(args, "symbol_id", null);
                    if (string.IsNullOrEmpty(symbolId))
                        return ToResult(new { error = "symbol_id is required." });
                    var req = new StewardessMCPServive.CodeIndexing.Query.GetReferencesRequest
                    {
                        SymbolId        = symbolId,
                        SnapshotId      = Str(args, "snapshot_id", null),
                        IncludeOutgoing = Bool(args, "include_outgoing", true),
                        IncludeIncoming = Bool(args, "include_incoming", false),
                    };
                    var result = await query.GetReferencesAsync(req, ct).ConfigureAwait(false);
                    if (result.Error != null)
                        return ToResult(new { error = result.Error });
                    return ToResult(result);
                });

            Add("code_index.get_file_references",
                category: "code_index",
                description: "Returns all reference edges originating from symbols declared in a specific file. " +
                             "Shows the complete dependency picture for a file: which types it inherits from, " +
                             "which types its members use, and which types it instantiates.",
                schema: Schema(
                    Prop("file_path",   "string",  "Relative file path to query references for (required)."),
                    Prop("snapshot_id", "string",  "Specific snapshot ID to query.")),
                handler: async (args, ct) =>
                {
                    var filePath = Str(args, "file_path", null);
                    if (string.IsNullOrEmpty(filePath))
                        return ToResult(new { error = "file_path is required." });
                    var req = new StewardessMCPServive.CodeIndexing.Query.GetFileReferencesRequest
                    {
                        FilePath   = filePath,
                        SnapshotId = Str(args, "snapshot_id", null),
                    };
                    var result = await query.GetFileReferencesAsync(req, ct).ConfigureAwait(false);
                    if (result.Error != null)
                        return ToResult(new { error = result.Error });
                    return ToResult(result);
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

        private static McpInputSchema Schema(params (string Name, McpPropertySchema Schema)[] props)
        {
            var schema = new McpInputSchema();
            foreach (var (name, propSchema) in props)
                schema.Properties[name] = propSchema;
            return schema;
        }

        private static (string, McpPropertySchema) Prop(
            string name,
            string type,
            string description,
            bool   required = false,
            object def      = null)
        {
            var prop = new McpPropertySchema
            {
                Type        = type,
                Description = description,
                Default     = def
            };
            return (name, prop);
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

        // ── Result factory ───────────────────────────────────────────────────────

        private static McpToolCallResult ToResult(object data)
        {
            var settings = new JsonSerializerSettings
            {
                Formatting  = Formatting.Indented,
                Converters  = { new Newtonsoft.Json.Converters.StringEnumConverter() },
            };
            var json = JsonConvert.SerializeObject(data, settings);
            return new McpToolCallResult
            {
                Content = new List<McpContent> { McpContent.FromText(json) }
            };
        }
    }
}

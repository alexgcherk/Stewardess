using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Models;

namespace StewardessMCPServive.Controllers
{
    /// <summary>
    /// Exposes the service capability manifest so an agent can discover all tools
    /// without issuing an MCP RPC call.
    ///
    /// GET /api/capabilities  — full manifest (JSON)
    /// GET /api/tools         — flat list of tool definitions
    /// </summary>
    [Route("api")]
    public sealed class CapabilitiesController : BaseController
    {
        /// <summary>Returns the full capability manifest including all tool schemas.</summary>
        [HttpGet, Route("capabilities"), AllowAnonymous]
        public IActionResult GetCapabilities() => Ok(BuildManifest());

        /// <summary>Returns only the flat list of tool definitions.</summary>
        [HttpGet, Route("tools"), AllowAnonymous]
        public IActionResult GetTools() => Ok(BuildManifest().Tools);

        // ────────────────────────────────────────────────────────────────────────
        //  Manifest builder
        // ────────────────────────────────────────────────────────────────────────

        internal static McpCapabilitiesManifest BuildManifest()
        {
            var settings = McpServiceSettings.Instance;
            var canWrite = !settings.ReadOnlyMode;
            var canCmd   = settings.AllowedCommands.Count > 0;

            return new McpCapabilitiesManifest
            {
                ServiceVersion    = settings.ServiceVersion,
                GeneratedAt       = DateTimeOffset.UtcNow,
                Capabilities      = new McpServerCapabilities
                {
                    CanRead               = true,
                    CanWrite              = canWrite,
                    CanSearch             = true,
                    CanExecuteCommands    = canCmd,
                    CanAccessGit          = true,
                    SupportsDryRun        = true,
                    SupportsRollback      = true,
                    SupportsAuditLog      = settings.EnableAuditLog,
                    SupportsBatchEdits    = true
                },
                Constraints = new McpServerConstraints
                {
                    MaxFileReadBytes           = settings.MaxFileReadBytes,
                    MaxSearchResults           = settings.MaxSearchResults,
                    MaxDirectoryDepth          = settings.MaxDirectoryDepth,
                    MaxCommandExecutionSeconds = settings.MaxCommandExecutionSeconds,
                    AllowedCommands            = settings.AllowedCommands,
                    BlockedFolders             = settings.BlockedFolders.ToList(),
                    BlockedExtensions          = settings.BlockedExtensions.ToList()
                },
                RepositoryContext = new McpRepositoryContext
                {
                    RepositoryName = System.IO.Path.GetFileName(settings.RepositoryRoot),
                    RepositoryRoot = settings.RepositoryRoot
                },
                Tools = BuildAllTools(canWrite, canCmd)
            };
        }

        // ────────────────────────────────────────────────────────────────────────
        //  Tool definitions
        // ────────────────────────────────────────────────────────────────────────

        internal static List<McpToolDefinition> BuildAllTools(bool canWrite, bool canCmd)
        {
            var t = new ToolListBuilder(canWrite, canCmd);

            // ── Navigation ──────────────────────────────────────────────────────
            t.Add("get_repository_info",   "navigation", false,
                  "Returns high-level information about the served repository.");

            t.Add("list_directory",        "navigation", false,
                  "Lists the immediate contents of a directory.",
                  t.P("path",           "string",  "Relative path from repo root. Empty = root."),
                  t.P("includeBlocked", "boolean", "Include normally-blocked folders.",          false),
                  t.P("namePattern",    "string",  "Wildcard filter (* and ?) for entry names."));

            t.Add("list_tree",             "navigation", false,
                  "Returns a recursive directory tree up to a configurable depth.",
                  t.P("path",            "string",  "Root of the subtree. Empty = repo root."),
                  t.P("maxDepth",        "integer", "Max traversal depth.",  3),
                  t.P("directoriesOnly", "boolean", "Return directory nodes only.", false));

            t.Add("file_exists",           "navigation", false,
                  "Checks whether a file or directory exists.",
                  t.PR("path", "string", "Relative path."));

            t.Add("get_file_metadata",     "navigation", false,
                  "Returns detailed metadata for a file or directory.",
                  t.PR("path", "string", "Relative path."));

            // ── Search ──────────────────────────────────────────────────────────
            t.Add("search_text",           "search", false,
                  "Searches for a literal string across the repository.",
                  t.PR("query",          "string",  "Text to search for."),
                  t.P ("searchPath",     "string",  "Restrict to sub-path."),
                  t.P ("extensions",     "array",   "File extension filter."),
                  t.P ("ignoreCase",     "boolean", "Case-insensitive.", true),
                  t.P ("wholeWord",      "boolean", "Whole-word only.",  false),
                  t.P ("maxResults",     "integer", "Max results.",      100),
                  t.P ("contextLinesBefore", "integer", "Context lines before.", 2),
                  t.P ("contextLinesAfter",  "integer", "Context lines after.",  2));

            t.Add("search_regex",          "search", false,
                  "Searches using a .NET regular expression.",
                  t.PR("pattern",    "string",  ".NET regex pattern."),
                  t.P ("searchPath", "string",  "Restrict to sub-path."),
                  t.P ("extensions", "array",   "File extension filter."),
                  t.P ("ignoreCase", "boolean", "Case-insensitive.", true),
                  t.P ("maxResults", "integer", "Max results.",      100));

            t.Add("search_file_names",     "search", false,
                  "Finds files whose names match a wildcard or substring pattern.",
                  t.PR("pattern",       "string",  "Name pattern (* and ? wildcards)."),
                  t.P ("searchPath",    "string",  "Restrict search root."),
                  t.P ("ignoreCase",    "boolean", "Case-insensitive.", true),
                  t.P ("matchFullPath", "boolean", "Match against full relative path.", false),
                  t.P ("maxResults",    "integer", "Max results.", 100));

            t.Add("search_by_extension",   "search", false,
                  "Returns all files with the given extensions.",
                  t.PR("extensions", "array",  "Extensions to find (e.g. [\".cs\",\".json\"])."),
                  t.P ("searchPath", "string", "Restrict root."),
                  t.P ("maxResults", "integer","Max results.", 200));

            t.Add("search_symbol",         "search", false,
                  "Best-effort symbol search (class/method/interface names) via text heuristics.",
                  t.PR("symbolName", "string",  "Symbol name or partial name."),
                  t.P ("symbolKind", "string",  "class|interface|method|property|enum|''.", ""),
                  t.P ("searchPath", "string",  "Restrict root."),
                  t.P ("ignoreCase", "boolean", "Case-insensitive.", true));

            t.Add("find_references",       "search", false,
                  "Finds textual usages of an identifier across the repository.",
                  t.PR("identifierName", "string",  "Identifier to find."),
                  t.P ("searchPath",     "string",  "Restrict root."),
                  t.P ("ignoreCase",     "boolean", "Case-insensitive.", false));

            // ── File reading ────────────────────────────────────────────────────
            t.Add("read_file",             "files", false,
                  "Reads the content of a file. Large files are truncated at the server limit.",
                  t.PR("path",         "string",  "Relative file path."),
                  t.P ("maxBytes",     "integer", "Override the byte limit."),
                  t.P ("returnBase64", "boolean", "Return base64-encoded bytes.", false));

            t.Add("read_file_range",       "files", false,
                  "Reads a contiguous range of lines from a file.",
                  t.PR("path",               "string",  "Relative file path."),
                  t.P ("startLine",          "integer", "1-based start line.", 1),
                  t.P ("endLine",            "integer", "1-based end line. -1 = end of file.", -1),
                  t.P ("includeLineNumbers", "boolean", "Prepend line numbers.", true));

            t.Add("read_multiple_files",   "files", false,
                  "Reads several files in a single call.",
                  t.PR("paths",          "array",   "List of relative paths."),
                  t.P ("maxBytesPerFile","integer",  "Per-file byte limit."));

            t.Add("get_file_hash",         "files", false,
                  "Computes a hash digest of a file.",
                  t.PR("path",      "string", "Relative file path."),
                  t.P ("algorithm", "string", "MD5 | SHA1 | SHA256.", "SHA256"));

            t.Add("get_file_structure",    "files", false,
                  "Returns a structural summary (namespaces, types, members) parsed from a code file.",
                  t.PR("path", "string", "Relative file path."));

            // ── Write operations ────────────────────────────────────────────────
            t.Add("write_file",            "edit",  true,
                  "Overwrites (or creates) a file with new content.",
                  t.PR("path",         "string", "Relative file path."),
                  t.PR("content",      "string", "New file content."),
                  t.P ("encoding",     "string", "utf-8 | utf-8-bom | utf-16 | ascii.", "utf-8"),
                  t.P ("lineEnding",   "string", "lf | crlf | cr | auto.", "auto"),
                  t.P ("dryRun",       "boolean","Simulate only.", false),
                  t.P ("createBackup", "boolean","Create backup before write.", true),
                  t.P ("changeReason", "string", "Reason logged in audit trail."));

            t.Add("create_file",           "edit",  true,
                  "Creates a new file. Fails if it already exists unless overwrite=true.",
                  t.PR("path",      "string",  "Relative path."),
                  t.P ("content",   "string",  "Initial content.", ""),
                  t.P ("overwrite", "boolean", "Allow overwrite.", false),
                  t.P ("dryRun",    "boolean", "Simulate only.",   false));

            t.Add("create_directory",      "edit",  true,
                  "Creates a directory, including any missing parent directories.",
                  t.PR("path",          "string",  "Relative path."),
                  t.P ("createParents", "boolean", "Create parent dirs.", true));

            t.Add("rename_path",           "edit",  true,
                  "Renames a file or directory in-place.",
                  t.PR("path",    "string", "Relative path."),
                  t.PR("newName", "string", "New name only (no directory change)."));

            t.Add("move_path",             "edit",  true,
                  "Moves a file or directory to a new location within the repository.",
                  t.PR("sourcePath",      "string",  "Source relative path."),
                  t.PR("destinationPath", "string",  "Destination relative path."),
                  t.P ("overwrite",       "boolean", "Allow overwrite.", false));

            t.Add("delete_file",           "edit",  true,
                  "Deletes a file. Creates a backup unless dryRun or createBackup=false.",
                  t.PR("path",         "string",  "Relative path."),
                  t.P ("dryRun",       "boolean", "Simulate only.", false),
                  t.P ("createBackup", "boolean", "Backup first.", true),
                  t.P ("approvalToken","string",  "Required when ApprovalRequiredForDestructive is enabled."));

            t.Add("delete_directory",      "edit",  true,
                  "Deletes a directory. Use recursive=true for non-empty directories.",
                  t.PR("path",         "string",  "Relative path."),
                  t.P ("recursive",    "boolean", "Delete recursively.", false),
                  t.P ("dryRun",       "boolean", "Simulate only.",      false),
                  t.P ("approvalToken","string",  "Approval token."));

            t.Add("append_file",           "edit",  true,
                  "Appends content to the end of a file.",
                  t.PR("path",          "string",  "Relative path."),
                  t.PR("content",       "string",  "Content to append."),
                  t.P ("ensureNewLine", "boolean", "Ensure content starts on a new line.", true));

            t.Add("replace_text",          "edit",  true,
                  "Replaces occurrences of a literal string in a file.",
                  t.PR("path",            "string",  "Relative path."),
                  t.PR("oldText",         "string",  "Text to replace."),
                  t.PR("newText",         "string",  "Replacement text."),
                  t.P ("ignoreCase",      "boolean", "Case-insensitive.", false),
                  t.P ("maxReplacements", "integer", "0 = unlimited.", 0),
                  t.P ("dryRun",         "boolean",  "Simulate only.", false));

            t.Add("replace_lines",         "edit",  true,
                  "Replaces a contiguous range of lines with new content.",
                  t.PR("path",       "string",  "Relative path."),
                  t.PR("startLine",  "integer", "1-based start line."),
                  t.PR("endLine",    "integer", "1-based end line."),
                  t.PR("newContent", "string",  "Replacement content."),
                  t.P ("dryRun",     "boolean", "Simulate only.", false));

            t.Add("patch_file",            "edit",  true,
                  "Applies a unified diff patch to a single file.",
                  t.PR("path",       "string",  "Relative path."),
                  t.PR("patch",      "string",  "Unified diff text."),
                  t.P ("fuzzFactor", "integer", "Context fuzz factor.", 3),
                  t.P ("dryRun",     "boolean", "Simulate only.",       false));

            t.Add("apply_batch_edits",     "edit",  true,
                  "Executes multiple heterogeneous edit operations atomically.",
                  t.PR("edits",        "array",   "Array of edit items."),
                  t.P ("dryRun",       "boolean", "Simulate all edits.", false),
                  t.P ("changeReason", "string",  "Reason for the batch."));

            t.Add("rollback_last_change",  "edit",  true,
                  "Restores a file from the backup created by a prior write operation.",
                  t.PR("rollbackToken", "string", "Token from the prior operation's result."));

            // ── Git ─────────────────────────────────────────────────────────────
            t.Add("get_git_status",        "git", false,
                  "Returns the git status of the repository.",
                  t.P("path", "string", "Restrict to sub-path."));

            t.Add("get_git_diff",          "git", false,
                  "Returns the unified diff for working-tree or staged changes.",
                  t.P("path",         "string",  "Restrict diff to this path."),
                  t.P("scope",        "string",  "unstaged | staged | head.", "unstaged"),
                  t.P("contextLines", "integer", "Context lines.", 3));

            t.Add("get_git_log",           "git", false,
                  "Returns the commit history.",
                  t.P("path",     "string",  "Restrict to path."),
                  t.P("maxCount", "integer", "Max commits.", 20),
                  t.P("ref",      "string",  "Branch or ref."),
                  t.P("author",   "string",  "Filter by author."),
                  t.P("since",    "string",  "ISO-8601 start date."),
                  t.P("until",    "string",  "ISO-8601 end date."));

            // ── Build / test / commands ─────────────────────────────────────────
            t.AddCmd("run_build",          "commands",
                  "Runs the configured build command (dotnet build / msbuild).",
                  t.P("workingDirectory", "string",  "Relative working directory."),
                  t.P("buildCommand",     "string",  "Override build command.", "dotnet build"),
                  t.P("arguments",        "string",  "Extra CLI arguments."),
                  t.P("configuration",    "string",  "Debug | Release.", "Debug"),
                  t.P("timeoutSeconds",   "integer", "Override timeout."));

            t.AddCmd("run_tests",          "commands",
                  "Runs the configured test command (dotnet test).",
                  t.P("workingDirectory", "string",  "Relative working directory."),
                  t.P("testCommand",      "string",  "Override test command.", "dotnet test"),
                  t.P("arguments",        "string",  "Extra CLI arguments."),
                  t.P("filter",           "string",  "Test name filter."),
                  t.P("timeoutSeconds",   "integer", "Override timeout."));

            t.AddCmd("run_custom_command", "commands",
                  "Runs a command that must appear in the AllowedCommands allowlist.",
                  t.PR("command",          "string",  "Full command line."),
                  t.P ("workingDirectory", "string",  "Relative working directory."),
                  t.P ("timeoutSeconds",   "integer", "Override timeout."));

            // ── Project helpers ─────────────────────────────────────────────────
            t.Add("get_solution_info",     "project", false,
                  "Returns solution files, projects, and project metadata.");

            t.Add("find_config_files",     "project", false,
                  "Locates common configuration files (*.config, appsettings.json, .env, etc.).");

            return t.Build();
        }

        // ────────────────────────────────────────────────────────────────────────
        //  Fluent builder helpers
        // ────────────────────────────────────────────────────────────────────────

        private sealed class ToolListBuilder
        {
            private readonly bool _canWrite;
            private readonly bool _canCmd;
            private readonly List<McpToolDefinition> _tools = new List<McpToolDefinition>();

            public ToolListBuilder(bool canWrite, bool canCmd)
            {
                _canWrite = canWrite;
                _canCmd   = canCmd;
            }

            // Ordinary tool (disabled when read-only and destructive)
            public void Add(string name, string category, bool destructive, string description,
                            params (string name, bool req, McpPropertySchema prop)[] props)
            {
                bool enabled = !destructive || _canWrite;
                _tools.Add(MakeTool(name, category, destructive, enabled, description, props));
            }

            // Command tool (disabled when no allowed commands)
            public void AddCmd(string name, string category, string description,
                               params (string name, bool req, McpPropertySchema prop)[] props)
            {
                _tools.Add(MakeTool(name, category, false, _canCmd, description, props));
            }

            public List<McpToolDefinition> Build() => _tools;

            // ── Property helpers ──────────────────────────────────────────────────

            /// <summary>Optional property.</summary>
            public (string, bool, McpPropertySchema) P(string name, string type, string description, object? def = null)
                => (name, false, new McpPropertySchema { Type = type, Description = description, Default = def,
                       Items = type == "array" ? new McpPropertySchema { Type = "string" } : null });

            /// <summary>Required property.</summary>
            public (string, bool, McpPropertySchema) PR(string name, string type, string description)
                => (name, true, new McpPropertySchema { Type = type, Description = description,
                       Items = type == "array" ? new McpPropertySchema { Type = "string" } : null });

            // ── Tool construction ─────────────────────────────────────────────────

            private static McpToolDefinition MakeTool(
                string name, string category, bool destructive, bool enabled,
                string description, (string name, bool req, McpPropertySchema prop)[] props)
            {
                var schema = new McpInputSchema();
                foreach (var (pName, req, pSchema) in props)
                {
                    schema.Properties[pName] = pSchema;
                    if (req) schema.Required.Add(pName);
                }

                return new McpToolDefinition
                {
                    Name           = name,
                    Category       = category,
                    Description    = description,
                    IsDestructive  = destructive,
                    IsDisabled     = !enabled,
                    DisabledReason = !enabled ? "Disabled in read-only mode or by configuration." : null,
                    InputSchema    = schema
                };
            }
        }
    }
}

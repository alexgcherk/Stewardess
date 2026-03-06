using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace StewardessMCPServive.Configuration
{
    /// <summary>
    /// Strongly-typed, lazily-loaded service settings read from Web.config &lt;appSettings&gt;.
    /// All keys use the "Mcp:" prefix.  Reload() can be called after a config change.
    /// </summary>
    public sealed class McpServiceSettings
    {
        // ── Singleton ────────────────────────────────────────────────────────────
        private static volatile McpServiceSettings _instance;
        private static readonly object _lock = new object();

        /// <summary>Gets the current settings instance.</summary>
        public static McpServiceSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new McpServiceSettings();
                    }
                }
                return _instance;
            }
        }

        /// <summary>Forces a reload from the current ConfigurationManager values.</summary>
        public static void Reload()
        {
            lock (_lock) { _instance = new McpServiceSettings(); }
        }

        // ── Repository ───────────────────────────────────────────────────────────

        /// <summary>Absolute path to the root of the repository being served.</summary>
        public string RepositoryRoot { get; private set; }

        /// <summary>When true all write/edit/delete/command operations are rejected.</summary>
        public bool ReadOnlyMode { get; private set; }

        // ── Security ─────────────────────────────────────────────────────────────

        /// <summary>
        /// API key expected in the X-API-Key header (or Authorization: Bearer).
        /// Empty string disables key enforcement.
        /// </summary>
        public string ApiKey { get; private set; }

        /// <summary>True when <see cref="ApiKey"/> is non-empty.</summary>
        public bool RequireApiKey { get; private set; }

        /// <summary>
        /// Comma-separated list of client IP addresses permitted to call the service.
        /// Empty list allows all IPs.
        /// </summary>
        public IReadOnlyList<string> AllowedIPs { get; private set; }

        /// <summary>When true, destructive operations require an explicit confirmation token.</summary>
        public bool RequireApprovalForDestructive { get; private set; }

        // ── Size / depth limits ──────────────────────────────────────────────────

        /// <summary>Maximum bytes read from a single file (default 5 MB).</summary>
        public long MaxFileReadBytes { get; private set; }

        /// <summary>Maximum bytes written in a single write operation (default 10 MB).</summary>
        public long MaxFileSizeForWrite { get; private set; }

        /// <summary>Maximum number of search results returned per query (default 200).</summary>
        public int MaxSearchResults { get; private set; }

        /// <summary>Maximum recursive depth for list_tree (default 10).</summary>
        public int MaxDirectoryDepth { get; private set; }

        /// <summary>Timeout in seconds for build/test/command execution (default 60).</summary>
        public int MaxCommandExecutionSeconds { get; private set; }

        /// <summary>Maximum number of files returned in a single directory listing (default 500).</summary>
        public int MaxDirectoryEntries { get; private set; }

        // ── Filtering ────────────────────────────────────────────────────────────

        /// <summary>
        /// Folder names (not full paths) that are always excluded from listings and
        /// searches.  Case-insensitive.
        /// </summary>
        public IReadOnlyCollection<string> BlockedFolders { get; private set; }

        /// <summary>
        /// File extensions (including leading dot, e.g. ".cs") that may be read/written.
        /// Empty set means all extensions are allowed (subject to <see cref="BlockedExtensions"/>).
        /// </summary>
        public IReadOnlyCollection<string> AllowedExtensions { get; private set; }

        /// <summary>File extensions that are always refused, overriding <see cref="AllowedExtensions"/>.</summary>
        public IReadOnlyCollection<string> BlockedExtensions { get; private set; }

        // ── Commands ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Command prefixes that are permitted to execute.
        /// Each entry is matched as a prefix (case-insensitive) against the full command string.
        /// </summary>
        public IReadOnlyList<string> AllowedCommands { get; private set; }

        // ── Audit / Backup ───────────────────────────────────────────────────────

        /// <summary>When true, every modifying operation is written to the audit log.</summary>
        public bool EnableAuditLog { get; private set; }

        /// <summary>
        /// Path to the audit log file.  When empty, defaults to
        /// &lt;RepositoryRoot&gt;\.mcp\audit.log.
        /// </summary>
        public string AuditLogPath { get; private set; }

        /// <summary>When true, a backup copy of each file is created before it is modified.</summary>
        public bool EnableBackups { get; private set; }

        /// <summary>
        /// Directory where backup copies are stored.  When empty, defaults to
        /// &lt;RepositoryRoot&gt;\.mcp\backups.
        /// </summary>
        public string BackupDirectory { get; private set; }

        /// <summary>Maximum number of backups kept per file (oldest are pruned, default 10).</summary>
        public int MaxBackupsPerFile { get; private set; }

        // ── Logging ──────────────────────────────────────────────────────────────

        /// <summary>Minimum log level (Trace | Debug | Info | Warn | Error, default Info).</summary>
        public string LogLevel { get; private set; }

        // ── Service version ──────────────────────────────────────────────────────

        /// <summary>Service version string included in health/capabilities responses.</summary>
        public string ServiceVersion { get; private set; }

        // ── Constructor ──────────────────────────────────────────────────────────

        private McpServiceSettings() { Load(); }

        private void Load()
        {
            RepositoryRoot          = GetString ("Mcp:RepositoryRoot",              string.Empty);
            ReadOnlyMode            = GetBool   ("Mcp:ReadOnlyMode",                false);

            ApiKey                  = GetString ("Mcp:ApiKey",                      string.Empty);
            RequireApiKey           = !string.IsNullOrWhiteSpace(ApiKey);
            AllowedIPs              = GetList   ("Mcp:AllowedIPs");
            RequireApprovalForDestructive = GetBool("Mcp:RequireApprovalForDestructive", false);

            MaxFileReadBytes        = GetLong   ("Mcp:MaxFileReadBytes",            5L  * 1024 * 1024);
            MaxFileSizeForWrite     = GetLong   ("Mcp:MaxFileSizeForWrite",         10L * 1024 * 1024);
            MaxSearchResults        = GetInt    ("Mcp:MaxSearchResults",            200);
            MaxDirectoryDepth       = GetInt    ("Mcp:MaxDirectoryDepth",           10);
            MaxCommandExecutionSeconds = GetInt ("Mcp:MaxCommandExecutionSeconds",  60);
            MaxDirectoryEntries     = GetInt    ("Mcp:MaxDirectoryEntries",         500);

            BlockedFolders          = GetSet    ("Mcp:BlockedFolders",
                                        ".git,bin,obj,.vs,packages,node_modules,.idea,.vscode,TestResults,.nuget,.mcp_backups,.mcp");
            AllowedExtensions       = GetSet    ("Mcp:AllowedExtensions",           string.Empty);
            BlockedExtensions       = GetSet    ("Mcp:BlockedExtensions",
                                        ".exe,.dll,.pdb,.msi,.zip,.7z,.rar,.tar,.gz,.bz2,.bin,.cache,.suo,.user");

            AllowedCommands         = GetList   ("Mcp:AllowedCommands",
                                        "dotnet build,dotnet test,dotnet restore,msbuild,git status,git diff,git log,git show,git stash");

            EnableAuditLog          = GetBool   ("Mcp:EnableAuditLog",             true);
            AuditLogPath            = GetString ("Mcp:AuditLogPath",               string.Empty);
            EnableBackups           = GetBool   ("Mcp:EnableBackups",              true);
            BackupDirectory         = GetString ("Mcp:BackupDirectory",            string.Empty);
            MaxBackupsPerFile       = GetInt    ("Mcp:MaxBackupsPerFile",          10);

            LogLevel                = GetString ("Mcp:LogLevel",                   "Info");
            ServiceVersion          = GetString ("Mcp:ServiceVersion",             "1.0.0");
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static string GetString(string key, string def) =>
            ConfigurationManager.AppSettings[key] ?? def;

        private static bool GetBool(string key, bool def)
        {
            var v = ConfigurationManager.AppSettings[key];
            return v != null ? bool.Parse(v) : def;
        }

        private static int GetInt(string key, int def)
        {
            var v = ConfigurationManager.AppSettings[key];
            return v != null ? int.Parse(v) : def;
        }

        private static long GetLong(string key, long def)
        {
            var v = ConfigurationManager.AppSettings[key];
            return v != null ? long.Parse(v) : def;
        }

        private static List<string> GetList(string key, string def = "")
        {
            var raw = ConfigurationManager.AppSettings[key] ?? def;
            return string.IsNullOrWhiteSpace(raw)
                ? new List<string>()
                : raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(s => s.Trim())
                     .Where(s => s.Length > 0)
                     .ToList();
        }

        private static HashSet<string> GetSet(string key, string def = "")
        {
            return new HashSet<string>(GetList(key, def), StringComparer.OrdinalIgnoreCase);
        }

        // ── Test factory ─────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a settings instance with explicit values, bypassing Web.config.
        /// Intended for unit tests only.
        /// </summary>
        public static McpServiceSettings CreateForTesting(
            string   repositoryRoot     = "",
            bool     readOnly           = false,
            string[] blockedFolders     = null,
            string[] blockedExtensions  = null,
            string[] allowedExtensions  = null,
            string[] allowedIps         = null,
            long     maxFileReadBytes   = 5 * 1024 * 1024,
            int      maxSearchResults   = 200,
            int      maxDirectoryDepth  = 10,
            string   apiKey             = "",
            string[] allowedCommands    = null)
        {
            var s = new McpServiceSettings();
            s.RepositoryRoot      = repositoryRoot;
            s.ReadOnlyMode        = readOnly;
            s.BlockedFolders      = blockedFolders  != null
                ? new HashSet<string>(blockedFolders,  StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            s.BlockedExtensions   = blockedExtensions != null
                ? new HashSet<string>(blockedExtensions, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            s.AllowedExtensions   = allowedExtensions != null
                ? new HashSet<string>(allowedExtensions, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            s.AllowedIPs          = allowedIps != null
                ? new List<string>(allowedIps)
                : new List<string>();
            s.MaxFileReadBytes    = maxFileReadBytes;
            s.MaxSearchResults    = maxSearchResults;
            s.MaxDirectoryDepth   = maxDirectoryDepth;
            s.ApiKey              = apiKey;
            s.RequireApiKey       = !string.IsNullOrWhiteSpace(apiKey);
            s.AllowedCommands     = allowedCommands != null
                ? new List<string>(allowedCommands)
                : new List<string> { "dotnet build", "dotnet test", "dotnet restore", "msbuild",
                                     "git status", "git diff", "git log", "git show", "git stash" };
            s.EnableAuditLog      = false;
            s.AuditLogPath        = "";
            s.EnableBackups       = false;
            s.BackupDirectory     = "";
            s.MaxBackupsPerFile   = 10;
            s.MaxFileSizeForWrite = 10L * 1024 * 1024;
            s.MaxCommandExecutionSeconds = 60;
            s.MaxDirectoryEntries = 500;
            s.LogLevel            = "Info";
            s.ServiceVersion      = "test";
            s.RequireApprovalForDestructive = false;
            return s;
        }

        // Convenience alias kept for backward compat with existing test helpers.
        internal static McpServiceSettings CreateForTestingWithIps(
            string   repositoryRoot,
            bool     readOnly,
            string   apiKey,
            string[] allowedIps) =>
            CreateForTesting(repositoryRoot: repositoryRoot, readOnly: readOnly,
                             apiKey: apiKey, allowedIps: allowedIps);
    }
}

// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.Configuration;

/// <summary>
///     Strongly-typed settings read from appsettings.json section "Mcp".
///     Constructed with an <see cref="IConfiguration" /> and registered as a singleton in DI.
///     All property types are identical to the .NET 4.7.2 version so services need no changes.
/// </summary>
public sealed class McpServiceSettings
{
    // ── Singleton accessor (preserved for legacy code paths) ─────────────────
    private static McpServiceSettings? _instance;

    // ── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Loads settings from the "Mcp" section of the supplied configuration.</summary>
    public McpServiceSettings(IConfiguration configuration)
    {
        Load(configuration);
    }

    /// <summary>Private constructor used by the test factory only.</summary>
    private McpServiceSettings()
    {
    }

    /// <summary>Gets the current settings instance (populated during startup).</summary>
    public static McpServiceSettings Instance =>
        _instance ?? throw new InvalidOperationException(
            "McpServiceSettings.Instance has not been initialised. " +
            "Call McpServiceSettings.SetInstance() in Program.cs before first use.");

    // ── Repository ───────────────────────────────────────────────────────────

    /// <summary>Absolute path to the root of the repository being served.</summary>
    public string RepositoryRoot { get; private set; } = string.Empty;

    /// <summary>When true all write/edit/delete/command operations are rejected.</summary>
    public bool ReadOnlyMode { get; private set; }

    // ── Security ─────────────────────────────────────────────────────────────

    /// <summary>API key expected in the X-API-Key header (or Authorization: Bearer).</summary>
    public string ApiKey { get; private set; } = string.Empty;

    /// <summary>True when <see cref="ApiKey" /> is non-empty.</summary>
    public bool RequireApiKey { get; private set; }

    /// <summary>List of client IP addresses permitted to call the service (empty = all).</summary>
    public IReadOnlyList<string> AllowedIPs { get; private set; } = new List<string>();

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

    /// <summary>Folder names that are always excluded (case-insensitive).</summary>
    public IReadOnlyCollection<string> BlockedFolders { get; private set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>File extensions that may be read/written (empty = all allowed).</summary>
    public IReadOnlyCollection<string> AllowedExtensions { get; private set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>File extensions that are always refused.</summary>
    public IReadOnlyCollection<string> BlockedExtensions { get; private set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Command prefixes that are permitted to execute (case-insensitive prefix match).</summary>
    public IReadOnlyList<string> AllowedCommands { get; private set; } = new List<string>();

    // ── Audit / Backup ───────────────────────────────────────────────────────

    /// <summary>When true, every modifying operation is written to the audit log.</summary>
    public bool EnableAuditLog { get; private set; }

    /// <summary>Path to the audit log file (empty = default).</summary>
    public string AuditLogPath { get; private set; } = string.Empty;

    /// <summary>When true, a backup copy of each file is created before modification.</summary>
    public bool EnableBackups { get; private set; }

    /// <summary>Directory for backup copies (empty = default).</summary>
    public string BackupDirectory { get; private set; } = string.Empty;

    /// <summary>Maximum number of backups kept per file (default 10).</summary>
    public int MaxBackupsPerFile { get; private set; }

    // ── Logging / version ────────────────────────────────────────────────────

    /// <summary>Minimum log level (Trace | Debug | Info | Warn | Error, default Info).</summary>
    public string LogLevel { get; private set; } = "Info";

    /// <summary>Service version string included in health/capabilities responses.</summary>
    public string ServiceVersion { get; private set; } = "2.0.0";

    /// <summary>Called once from Program.cs after the DI container is built.</summary>
    public static void SetInstance(McpServiceSettings settings)
    {
        _instance = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    private void Load(IConfiguration config)
    {
        RepositoryRoot = GetString(config, "Mcp:RepositoryRoot", string.Empty);

        if (string.IsNullOrWhiteSpace(RepositoryRoot))
            throw new InvalidOperationException(
                "Mcp:RepositoryRoot is not configured. " +
                "Set the MCP__REPOSITORYROOT environment variable or add \"RepositoryRoot\" to appsettings.json.");

        ReadOnlyMode = GetBool(config, "Mcp:ReadOnlyMode", false);

        ApiKey = GetString(config, "Mcp:ApiKey", string.Empty);
        RequireApiKey = !string.IsNullOrWhiteSpace(ApiKey);
        AllowedIPs = GetList(config, "Mcp:AllowedIPs");
        RequireApprovalForDestructive = GetBool(config, "Mcp:RequireApprovalForDestructive", false);

        MaxFileReadBytes = GetLong(config, "Mcp:MaxFileReadBytes", 5L * 1024 * 1024);
        MaxFileSizeForWrite = GetLong(config, "Mcp:MaxFileSizeForWrite", 10L * 1024 * 1024);
        MaxSearchResults = GetInt(config, "Mcp:MaxSearchResults", 200);
        MaxDirectoryDepth = GetInt(config, "Mcp:MaxDirectoryDepth", 10);
        MaxCommandExecutionSeconds = GetInt(config, "Mcp:MaxCommandExecutionSeconds", 60);
        MaxDirectoryEntries = GetInt(config, "Mcp:MaxDirectoryEntries", 500);

        BlockedFolders = GetSet(config, "Mcp:BlockedFolders",
            ".git,bin,obj,.vs,packages,node_modules,.idea,.vscode,TestResults,.nuget,.mcp_backups,.mcp");
        AllowedExtensions = GetSet(config, "Mcp:AllowedExtensions", string.Empty);
        BlockedExtensions = GetSet(config, "Mcp:BlockedExtensions",
            ".exe,.dll,.pdb,.msi,.zip,.7z,.rar,.tar,.gz,.bz2,.bin,.cache,.suo,.user");

        AllowedCommands = GetList(config, "Mcp:AllowedCommands",
            "dotnet build,dotnet test,dotnet restore,msbuild,git status,git diff,git log,git show,git stash");

        EnableAuditLog = GetBool(config, "Mcp:EnableAuditLog", true);
        AuditLogPath = GetString(config, "Mcp:AuditLogPath", string.Empty);
        EnableBackups = GetBool(config, "Mcp:EnableBackups", true);
        BackupDirectory = GetString(config, "Mcp:BackupDirectory", string.Empty);
        MaxBackupsPerFile = GetInt(config, "Mcp:MaxBackupsPerFile", 10);

        LogLevel = GetString(config, "Mcp:LogLevel", "Info");
        ServiceVersion = GetString(config, "Mcp:ServiceVersion", "2.0.0");
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string GetString(IConfiguration c, string key, string def)
    {
        return c[key] ?? def;
    }

    private static bool GetBool(IConfiguration c, string key, bool def)
    {
        var v = c[key];
        return v != null ? bool.Parse(v) : def;
    }

    private static int GetInt(IConfiguration c, string key, int def)
    {
        var v = c[key];
        return v != null ? int.Parse(v) : def;
    }

    private static long GetLong(IConfiguration c, string key, long def)
    {
        var v = c[key];
        return v != null ? long.Parse(v) : def;
    }

    private static List<string> GetList(IConfiguration c, string key, string def = "")
    {
        var raw = c[key] ?? def;
        return string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
    }

    private static HashSet<string> GetSet(IConfiguration c, string key, string def = "")
    {
        return new HashSet<string>(GetList(c, key, def), StringComparer.OrdinalIgnoreCase);
    }

    // ── Test factory ─────────────────────────────────────────────────────────

    /// <summary>
    ///     Creates a settings instance with explicit values, bypassing appsettings.json.
    ///     Intended for unit tests only.
    /// </summary>
    public static McpServiceSettings CreateForTesting(
        string repositoryRoot = "",
        bool readOnly = false,
        string[] blockedFolders = null!,
        string[] blockedExtensions = null!,
        string[] allowedExtensions = null!,
        string[] allowedIps = null!,
        long maxFileReadBytes = 5 * 1024 * 1024,
        int maxSearchResults = 200,
        int maxDirectoryDepth = 10,
        string apiKey = "",
        string[] allowedCommands = null!)
    {
        var s = new McpServiceSettings();
        s.RepositoryRoot = repositoryRoot;
        s.ReadOnlyMode = readOnly;
        s.BlockedFolders = blockedFolders != null
            ? new HashSet<string>(blockedFolders, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        s.BlockedExtensions = blockedExtensions != null
            ? new HashSet<string>(blockedExtensions, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        s.AllowedExtensions = allowedExtensions != null
            ? new HashSet<string>(allowedExtensions, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        s.AllowedIPs = allowedIps != null ? new List<string>(allowedIps) : new List<string>();
        s.MaxFileReadBytes = maxFileReadBytes;
        s.MaxSearchResults = maxSearchResults;
        s.MaxDirectoryDepth = maxDirectoryDepth;
        s.ApiKey = apiKey;
        s.RequireApiKey = !string.IsNullOrWhiteSpace(apiKey);
        s.AllowedCommands = allowedCommands != null
            ? new List<string>(allowedCommands)
            : new List<string>
            {
                "dotnet build", "dotnet test", "dotnet restore", "msbuild",
                "git status", "git diff", "git log", "git show", "git stash"
            };
        s.EnableAuditLog = false;
        s.AuditLogPath = string.Empty;
        s.EnableBackups = false;
        s.BackupDirectory = string.Empty;
        s.MaxBackupsPerFile = 10;
        s.MaxFileSizeForWrite = 10L * 1024 * 1024;
        s.MaxCommandExecutionSeconds = 60;
        s.MaxDirectoryEntries = 500;
        s.LogLevel = "Info";
        s.ServiceVersion = "test";
        s.RequireApprovalForDestructive = false;
        return s;
    }

    internal static McpServiceSettings CreateForTestingWithIps(
        string repositoryRoot,
        bool readOnly,
        string apiKey,
        string[] allowedIps)
    {
        return CreateForTesting(repositoryRoot, readOnly,
            apiKey: apiKey, allowedIps: allowedIps);
    }
}
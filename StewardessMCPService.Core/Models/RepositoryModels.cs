using System;
using System.Collections.Generic;

namespace StewardessMCPService.Models
{
    // ────────────────────────────────────────────────────────────────────────────
    //  Repository navigation and discovery models
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>High-level information about the served repository.</summary>
    public sealed class RepositoryInfoResponse
    {
        /// <summary>Absolute path of the repository root on the server.</summary>
        public string RepositoryRoot { get; set; } = null!;

        /// <summary>Name of the root directory (last path segment).</summary>
        public string RepositoryName { get; set; } = null!;

        /// <summary>True when the service is in read-only mode.</summary>
        public bool ReadOnlyMode { get; set; }

        /// <summary>Service version string.</summary>
        public string ServiceVersion { get; set; } = null!;

        /// <summary>UTC timestamp of the server clock (allows agent clock sync).</summary>
        public DateTimeOffset ServerTime { get; set; }

        /// <summary>Active security policy summary.</summary>
        public RepositoryPolicyInfo Policy { get; set; } = null!;

        /// <summary>Git information if the repository is a Git repo.</summary>
        public GitRepoSummary? GitInfo { get; set; }
    }

    /// <summary>Summary of the active security / filtering policy.</summary>
    public sealed class RepositoryPolicyInfo
    {
        /// <summary>True when an API key is required for all requests.</summary>
        public bool ApiKeyRequired { get; set; }
        /// <summary>True when an IP allowlist is active.</summary>
        public bool IpAllowlistActive { get; set; }
        /// <summary>True when the service is in read-only mode.</summary>
        public bool ReadOnlyMode { get; set; }
        /// <summary>True when destructive operations require an approval token.</summary>
        public bool ApprovalRequiredForDestructive { get; set; }
        /// <summary>Folder names that are blocked from access.</summary>
        public IReadOnlyList<string> BlockedFolders { get; set; } = null!;
        /// <summary>File extensions that are blocked from access.</summary>
        public IReadOnlyList<string> BlockedExtensions { get; set; } = null!;
        /// <summary>When non-empty, only these file extensions are accessible.</summary>
        public IReadOnlyList<string> AllowedExtensions { get; set; } = null!;
        /// <summary>Maximum number of bytes that can be read from a single file.</summary>
        public long MaxFileReadBytes { get; set; }
        /// <summary>Maximum number of search results returned per query.</summary>
        public int MaxSearchResults { get; set; }
        /// <summary>Maximum directory traversal depth.</summary>
        public int MaxDirectoryDepth { get; set; }
    }

    /// <summary>Lightweight git metadata shown in the repository info summary.</summary>
    public sealed class GitRepoSummary
    {
        /// <summary>True when the repository root is a valid git repository.</summary>
        public bool IsGitRepository { get; set; }
        /// <summary>Name of the currently checked-out branch.</summary>
        public string? CurrentBranch { get; set; }
        /// <summary>Full SHA of the HEAD commit.</summary>
        public string? HeadCommitSha { get; set; }
        /// <summary>Subject line of the HEAD commit message.</summary>
        public string? HeadCommitMessage { get; set; }
        /// <summary>True when staged or unstaged changes are present.</summary>
        public bool HasUncommittedChanges { get; set; }
    }

    // ── list_directory ───────────────────────────────────────────────────────────

    /// <summary>Request to list the immediate contents of a directory.</summary>
    public sealed class ListDirectoryRequest
    {
        /// <summary>Path relative to the repository root, or empty for the root.</summary>
        public string Path { get; set; } = "";

        /// <summary>When true, blocked folders are included in the listing.</summary>
        public bool IncludeBlocked { get; set; } = false;

        /// <summary>Filter entries by name pattern using simple wildcard (* and ?).</summary>
        public string? NamePattern { get; set; }

        /// <summary>
        /// Sort order for entries: "name" (default, alphabetical) or "size" (largest files first).
        /// </summary>
        public string SortBy { get; set; } = "name";
    }

    /// <summary>Result of a directory listing.</summary>
    public sealed class ListDirectoryResponse
    {
        /// <summary>Relative path of the listed directory.</summary>
        public string Path { get; set; } = null!;

        /// <summary>Absolute path (server-side; informational).</summary>
        public string AbsolutePath { get; set; } = null!;

        /// <summary>Directory entries (files and subdirectories).</summary>
        public List<DirectoryEntry> Entries { get; set; } = new List<DirectoryEntry>();

        /// <summary>True when the entry count was limited by MaxDirectoryEntries.</summary>
        public bool Truncated { get; set; }

        /// <summary>Total unfiltered entry count.</summary>
        public int TotalCount { get; set; }
    }

    /// <summary>A single entry (file or directory) inside a listed directory.</summary>
    public sealed class DirectoryEntry
    {
        /// <summary>Entry name (file or directory name only, not a full path).</summary>
        public string Name { get; set; } = null!;

        /// <summary>Path relative to the repository root.</summary>
        public string RelativePath { get; set; } = null!;

        /// <summary>"file" or "directory".</summary>
        public string Type { get; set; } = null!;

        /// <summary>File size in bytes; null for directories.</summary>
        public long? SizeBytes { get; set; }

        /// <summary>File extension including the leading dot; null for directories.</summary>
        public string? Extension { get; set; }

        /// <summary>UTC last-write time.</summary>
        public DateTimeOffset LastModified { get; set; }

        /// <summary>True when this entry is in the blocked-folders list.</summary>
        public bool IsBlocked { get; set; }
    }

    // ── list_tree ────────────────────────────────────────────────────────────────

    /// <summary>Request for a recursive directory tree.</summary>
    public sealed class ListTreeRequest
    {
        /// <summary>Root path relative to repository root; empty for the full repo.</summary>
        public string Path { get; set; } = "";

        /// <summary>Maximum traversal depth (1 = immediate children only).  Capped at MaxDirectoryDepth.</summary>
        public int MaxDepth { get; set; } = 3;

        /// <summary>When true, blocked folders appear in the tree (with IsBlocked flag) but are not expanded.</summary>
        public bool IncludeBlocked { get; set; } = false;

        /// <summary>When true, only directories are returned (no files).</summary>
        public bool DirectoriesOnly { get; set; } = false;

        /// <summary>Restrict files to these extensions (empty = all).</summary>
        public List<string>? ExtensionFilter { get; set; }
    }

    /// <summary>Result of a recursive tree listing.</summary>
    public sealed class ListTreeResponse
    {
        /// <summary>Repository-relative path of the tree root.</summary>
        public string Path { get; set; } = null!;
        /// <summary>Root node of the tree.</summary>
        public TreeNode Root { get; set; } = null!;
        /// <summary>Total number of files in the tree.</summary>
        public int TotalFiles { get; set; }
        /// <summary>Total number of directories in the tree.</summary>
        public int TotalDirectories { get; set; }
        /// <summary>True when the tree was truncated at the depth or node limit.</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>A node in a recursive directory tree.</summary>
    public sealed class TreeNode
    {
        /// <summary>Entry name (file or directory name without path).</summary>
        public string Name { get; set; } = null!;
        /// <summary>Path relative to repository root.</summary>
        public string RelativePath { get; set; } = null!;
        /// <summary>"file" or "directory".</summary>
        public string Type { get; set; } = null!;
        /// <summary>File size in bytes; null for directories.</summary>
        public long? SizeBytes { get; set; }
        /// <summary>File extension including the leading dot; null for directories.</summary>
        public string? Extension { get; set; }
        /// <summary>UTC last-write time.</summary>
        public DateTimeOffset LastModified { get; set; }
        /// <summary>True when this entry is in the blocked-folders list.</summary>
        public bool IsBlocked { get; set; }

        /// <summary>Child nodes; null for files or unexpanded/blocked directories.</summary>
        public List<TreeNode>? Children { get; set; }
    }

    // ── file_exists / directory_exists ──────────────────────────────────────────

    /// <summary>Request to check whether a path exists in the repository.</summary>
    public sealed class PathExistsRequest
    {
        /// <summary>Path to check, relative to repository root.</summary>
        public string Path { get; set; } = null!;
    }

    /// <summary>Response indicating whether a given path exists and its type.</summary>
    public sealed class PathExistsResponse
    {
        /// <summary>The path that was checked, relative to repository root.</summary>
        public string Path { get; set; } = null!;
        /// <summary>True when the path exists on disk.</summary>
        public bool Exists { get; set; }
        /// <summary>"file", "directory", or "none".</summary>
        public string Type { get; set; } = null!;  // "file" | "directory" | "none"
    }

    // ── get_file_metadata ────────────────────────────────────────────────────────

    /// <summary>Request to retrieve metadata for a file or directory.</summary>
    public sealed class FileMetadataRequest
    {
        /// <summary>Path relative to repository root.</summary>
        public string Path { get; set; } = null!;
    }

    /// <summary>Detailed metadata for a single file or directory.</summary>
    public sealed class FileMetadataResponse
    {
        /// <summary>Repository-relative path.</summary>
        public string RelativePath { get; set; } = null!;
        /// <summary>Absolute path on disk (server-side; informational).</summary>
        public string AbsolutePath { get; set; } = null!;
        /// <summary>Entry name without path.</summary>
        public string Name { get; set; } = null!;
        /// <summary>File extension including the leading dot.</summary>
        public string? Extension { get; set; }
        /// <summary>"file" or "directory".</summary>
        public string Type { get; set; } = null!;
        /// <summary>Size in bytes (0 for directories).</summary>
        public long SizeBytes { get; set; }
        /// <summary>UTC creation timestamp.</summary>
        public DateTimeOffset CreatedAt { get; set; }
        /// <summary>UTC last-write timestamp.</summary>
        public DateTimeOffset LastModified { get; set; }
        /// <summary>UTC last-access timestamp.</summary>
        public DateTimeOffset LastAccessed { get; set; }
        /// <summary>True when the file has the read-only attribute set.</summary>
        public bool IsReadOnly { get; set; }
        /// <summary>Detected file encoding (e.g. "utf-8"); null for directories.</summary>
        public string? Encoding { get; set; }
        /// <summary>Detected line ending style ("LF", "CRLF", "CR", "Mixed"); null for directories.</summary>
        public string? LineEnding { get; set; }
        /// <summary>Number of lines; null for binary files and directories.</summary>
        public int? LineCount { get; set; }
    }
}

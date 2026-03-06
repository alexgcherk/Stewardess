using System;
using System.Collections.Generic;

namespace StewardessMCPServive.Models
{
    // ────────────────────────────────────────────────────────────────────────────
    //  Git models
    // ────────────────────────────────────────────────────────────────────────────

    // ── get_git_status ───────────────────────────────────────────────────────────

    /// <summary>Request to retrieve the current git working-tree status.</summary>
    public sealed class GitStatusRequest
    {
        /// <summary>Restrict output to this sub-path (relative to repo root; empty = whole repo).</summary>
        public string Path { get; set; } = "";
    }

    /// <summary>Response containing the current git repository status.</summary>
    public sealed class GitStatusResponse
    {
        /// <summary>True when the repository root is a valid git repository.</summary>
        public bool IsGitRepository { get; set; }
        /// <summary>Name of the currently checked-out branch.</summary>
        public string CurrentBranch { get; set; }
        /// <summary>Full SHA of the HEAD commit.</summary>
        public string HeadCommitSha { get; set; }
        /// <summary>Subject line of the HEAD commit message.</summary>
        public string HeadCommitMessage { get; set; }
        /// <summary>Remote URL for the origin remote, if configured.</summary>
        public string RemoteUrl { get; set; }
        /// <summary>True when staged or unstaged changes are present.</summary>
        public bool HasUncommittedChanges { get; set; }
        /// <summary>Per-file status entries.</summary>
        public List<GitStatusEntry> Files { get; set; } = new List<GitStatusEntry>();
        /// <summary>Number of files with staged changes.</summary>
        public int StagedCount { get; set; }
        /// <summary>Number of files with unstaged changes.</summary>
        public int UnstagedCount { get; set; }
        /// <summary>Number of untracked files.</summary>
        public int UntrackedCount { get; set; }
    }

    /// <summary>Git status information for a single file.</summary>
    public sealed class GitStatusEntry
    {
        /// <summary>Repository-relative path of the file.</summary>
        public string RelativePath { get; set; }

        /// <summary>
        /// Status code(s): "M" modified, "A" added, "D" deleted, "R" renamed,
        /// "C" copied, "U" unmerged, "?" untracked.
        /// </summary>
        public string IndexStatus { get; set; }

        /// <summary>Working-tree status code for this entry (e.g. "M", "D", "?").</summary>
        public string WorkTreeStatus { get; set; }

        /// <summary>Original path when IndexStatus is "R" (renamed).</summary>
        public string OldPath { get; set; }

        /// <summary>True when the file has staged changes.</summary>
        public bool IsStaged { get; set; }
        /// <summary>True when the file has unstaged changes.</summary>
        public bool IsUnstaged { get; set; }
        /// <summary>True when the file is untracked by git.</summary>
        public bool IsUntracked { get; set; }
    }

    // ── get_git_diff ─────────────────────────────────────────────────────────────

    /// <summary>Request to retrieve a git diff.</summary>
    public sealed class GitDiffRequest
    {
        /// <summary>
        /// Restrict diff to this file or directory.  Empty = whole repo.
        /// </summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Diff scope: "unstaged" (default), "staged", "head", "commit:sha1..sha2".
        /// </summary>
        public string Scope { get; set; } = "unstaged";

        /// <summary>Number of context lines around each change (default 3).</summary>
        public int ContextLines { get; set; } = 3;
    }

    /// <summary>Response containing parsed diff information.</summary>
    public sealed class GitDiffResponse
    {
        /// <summary>Effective diff scope (e.g. "unstaged", "staged", "head").</summary>
        public string Scope { get; set; }
        /// <summary>Per-file diff entries.</summary>
        public List<GitFileDiff> Files { get; set; } = new List<GitFileDiff>();
        /// <summary>Total lines added across all files.</summary>
        public int TotalLinesAdded { get; set; }
        /// <summary>Total lines removed across all files.</summary>
        public int TotalLinesRemoved { get; set; }
        /// <summary>True when the diff was truncated due to size limits.</summary>
        public bool Truncated { get; set; }

        /// <summary>Full raw unified diff text.</summary>
        public string RawDiff { get; set; }
    }

    /// <summary>Diff information for a single file.</summary>
    public sealed class GitFileDiff
    {
        /// <summary>Repository-relative path of the file.</summary>
        public string RelativePath { get; set; }
        /// <summary>Previous path (populated for renamed/copied files).</summary>
        public string OldPath { get; set; }

        /// <summary>"modified", "added", "deleted", "renamed", "copied".</summary>
        public string ChangeType { get; set; }

        /// <summary>Number of lines added in this file.</summary>
        public int LinesAdded { get; set; }
        /// <summary>Number of lines removed in this file.</summary>
        public int LinesRemoved { get; set; }
        /// <summary>Diff hunks for this file.</summary>
        public List<GitDiffHunk> Hunks { get; set; } = new List<GitDiffHunk>();
    }

    /// <summary>A single diff hunk (contiguous block of changes).</summary>
    public sealed class GitDiffHunk
    {
        /// <summary>Starting line number in the old file.</summary>
        public int OldStart { get; set; }
        /// <summary>Number of lines from the old file covered by this hunk.</summary>
        public int OldCount { get; set; }
        /// <summary>Starting line number in the new file.</summary>
        public int NewStart { get; set; }
        /// <summary>Number of lines from the new file covered by this hunk.</summary>
        public int NewCount { get; set; }
        /// <summary>The @@ header line from the unified diff.</summary>
        public string Header { get; set; }
        /// <summary>Individual diff lines within this hunk.</summary>
        public List<GitDiffLine> Lines { get; set; } = new List<GitDiffLine>();
    }

    /// <summary>A single line within a diff hunk.</summary>
    public sealed class GitDiffLine
    {
        /// <summary>"context" | "added" | "removed".</summary>
        public string Type { get; set; }
        /// <summary>Text content of the line (without leading +/-/ prefix).</summary>
        public string Text { get; set; }
        /// <summary>1-based line number in the old file; null for added lines.</summary>
        public int? OldLineNumber { get; set; }
        /// <summary>1-based line number in the new file; null for removed lines.</summary>
        public int? NewLineNumber { get; set; }
    }

    // ── get_git_log ──────────────────────────────────────────────────────────────

    /// <summary>Request to retrieve the commit log.</summary>
    public sealed class GitLogRequest
    {
        /// <summary>Restrict log to commits that touch this path (empty = all).</summary>
        public string Path { get; set; } = "";

        /// <summary>Maximum number of commits to return (default 20).</summary>
        public int MaxCount { get; set; } = 20;

        /// <summary>Branch or commit ref to start from; empty = HEAD.</summary>
        public string Ref { get; set; } = "";

        /// <summary>Restrict to commits by this author name or email.</summary>
        public string Author { get; set; } = "";

        /// <summary>ISO-8601 date; return only commits after this date.</summary>
        public string Since { get; set; } = "";

        /// <summary>ISO-8601 date; return only commits before this date.</summary>
        public string Until { get; set; } = "";
    }

    /// <summary>Response containing a list of git log entries.</summary>
    public sealed class GitLogResponse
    {
        /// <summary>Commit entries in reverse-chronological order.</summary>
        public List<GitLogEntry> Commits { get; set; } = new List<GitLogEntry>();
        /// <summary>True when results were capped at MaxCount.</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>Metadata for a single git commit.</summary>
    public sealed class GitLogEntry
    {
        /// <summary>Full 40-character commit SHA.</summary>
        public string Sha { get; set; }
        /// <summary>Abbreviated (7-character) commit SHA.</summary>
        public string ShortSha { get; set; }
        /// <summary>Author display name.</summary>
        public string AuthorName { get; set; }
        /// <summary>Author email address.</summary>
        public string AuthorEmail { get; set; }
        /// <summary>Author date (when the commit was originally authored).</summary>
        public DateTimeOffset AuthorDate { get; set; }
        /// <summary>Committer display name.</summary>
        public string CommitterName { get; set; }
        /// <summary>Committer date (when the commit was applied to the repo).</summary>
        public DateTimeOffset CommitterDate { get; set; }
        /// <summary>First line of the commit message.</summary>
        public string Subject { get; set; }
        /// <summary>Remainder of the commit message after the subject line.</summary>
        public string Body { get; set; }
        /// <summary>SHAs of parent commits (two entries for a merge commit).</summary>
        public List<string> ParentShas { get; set; } = new List<string>();
        /// <summary>Repository-relative paths of files changed in this commit.</summary>
        public List<string> ChangedFiles { get; set; } = new List<string>();
    }

    // ── get_commit ───────────────────────────────────────────────────────────────

    /// <summary>Request to retrieve a single commit by its SHA.</summary>
    public sealed class GitShowRequest
    {
        /// <summary>Full or abbreviated commit SHA (required).</summary>
        public string Sha { get; set; } = "";

        /// <summary>When true, the full patch diff is included in the response.</summary>
        public bool IncludeDiff { get; set; } = true;
    }

    /// <summary>Details of a single git commit.</summary>
    public sealed class GitShowResponse
    {
        /// <summary>Full 40-character commit SHA.</summary>
        public string Sha { get; set; }
        /// <summary>Abbreviated (7-character) commit SHA.</summary>
        public string ShortSha { get; set; }
        /// <summary>Author display name.</summary>
        public string AuthorName { get; set; }
        /// <summary>Author email address.</summary>
        public string AuthorEmail { get; set; }
        /// <summary>Author date (when the commit was originally authored).</summary>
        public DateTimeOffset AuthorDate { get; set; }
        /// <summary>Committer display name.</summary>
        public string CommitterName { get; set; }
        /// <summary>Committer email address.</summary>
        public string CommitterEmail { get; set; }
        /// <summary>Committer date (when the commit was applied to the repo).</summary>
        public DateTimeOffset CommitterDate { get; set; }
        /// <summary>First line of the commit message.</summary>
        public string Subject { get; set; }
        /// <summary>Remainder of the commit message after the subject line.</summary>
        public string Body { get; set; }
        /// <summary>SHAs of parent commits (two entries for a merge commit).</summary>
        public List<string> ParentShas { get; set; } = new List<string>();
        /// <summary>Repository-relative paths of files changed in this commit.</summary>
        public List<string> ChangedFiles { get; set; } = new List<string>();

        /// <summary>Unified diff patch; populated when <see cref="GitShowRequest.IncludeDiff"/> is true.</summary>
        public string Diff { get; set; }

        /// <summary>True when no commit with the given SHA was found.</summary>
        public bool NotFound { get; set; }
    }

    // ── Project-aware helpers ────────────────────────────────────────────────────

    /// <summary>Response listing solution and project files found in the repository.</summary>
    public sealed class SolutionInfoResponse
    {
        /// <summary>Relative paths of all .sln files found.</summary>
        public List<string> SolutionFiles { get; set; } = new List<string>();
        /// <summary>Parsed project information for all project files found.</summary>
        public List<ProjectInfo> Projects { get; set; } = new List<ProjectInfo>();
    }

    /// <summary>Metadata about a single project file.</summary>
    public sealed class ProjectInfo
    {
        /// <summary>Repository-relative path of the project file.</summary>
        public string RelativePath { get; set; }
        /// <summary>Project name (derived from file name).</summary>
        public string Name { get; set; }

        /// <summary>"csproj", "vbproj", "fsproj", "sqlproj", etc.</summary>
        public string ProjectType { get; set; }

        /// <summary>Target framework moniker (e.g. "net472", "net8.0").</summary>
        public string TargetFramework { get; set; }
        /// <summary>MSBuild output type (e.g. "Library", "Exe").</summary>
        public string OutputType { get; set; }

        /// <summary>True when the project name suggests it is a test project.</summary>
        public bool IsTestProject { get; set; }

        /// <summary>NuGet package IDs referenced by this project.</summary>
        public List<string> NuGetReferences { get; set; } = new List<string>();
        /// <summary>Repository-relative paths of referenced projects.</summary>
        public List<string> ProjectReferences { get; set; } = new List<string>();
    }

    /// <summary>Response listing configuration files found in the repository.</summary>
    public sealed class ConfigFilesResponse
    {
        /// <summary>Paths of app.config / web.config files.</summary>
        public List<string> AppSettings { get; set; } = new List<string>();
        /// <summary>Paths of files that contain connection string configuration.</summary>
        public List<string> ConnectionStrings { get; set; } = new List<string>();
        /// <summary>Paths of .json configuration files (e.g. appsettings.json).</summary>
        public List<string> JsonConfigs { get; set; } = new List<string>();
        /// <summary>Paths of .yaml / .yml configuration files.</summary>
        public List<string> YamlConfigs { get; set; } = new List<string>();
        /// <summary>Paths of .ini configuration files.</summary>
        public List<string> IniFiles { get; set; } = new List<string>();
        /// <summary>Paths of other detected configuration files.</summary>
        public List<string> OtherConfigs { get; set; } = new List<string>();
    }
}

// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPService.Models;

namespace StewardessMCPService.Services
{
    /// <summary>
    /// Git operations executed against the repository.
    /// All operations are read-only (status, diff, log); no commits or pushes.
    /// </summary>
    public interface IGitService
    {
        /// <summary>Returns the current git status of the repository or a sub-path.</summary>
        Task<GitStatusResponse> GetStatusAsync(GitStatusRequest request, CancellationToken ct = default);

        /// <summary>Returns the unified diff for working-tree or staged changes.</summary>
        Task<GitDiffResponse> GetDiffAsync(GitDiffRequest request, CancellationToken ct = default);

        /// <summary>Returns the unified diff for a single file.</summary>
        Task<GitDiffResponse> GetDiffForFileAsync(string relativePath, string scope = "unstaged", CancellationToken ct = default);

        /// <summary>Returns the commit history for the repository or a sub-path.</summary>
        Task<GitLogResponse> GetLogAsync(GitLogRequest request, CancellationToken ct = default);

        /// <summary>Returns details of a single commit by SHA (full or abbreviated).</summary>
        Task<GitShowResponse> GetCommitAsync(GitShowRequest request, CancellationToken ct = default);

        /// <summary>Returns true when the repository root is a valid git repository.</summary>
        Task<bool> IsGitRepositoryAsync(CancellationToken ct = default);

        // ── Spec extensions ──────────────────────────────────────────────────────────

        /// <summary>Lists all local and remote branches.</summary>
        Task<List<ApiBranch>> ListBranchesAsync(CancellationToken ct = default);

        /// <summary>Creates a new branch from sourceBranch without checking it out.</summary>
        Task<ApiBranch> CreateBranchAsync(string name, string sourceBranch, CancellationToken ct = default);

        /// <summary>Returns per-file diffs between two commit SHAs.</summary>
        Task<List<ApiDiff>> DiffBetweenCommitsAsync(string baseSha, string targetSha, CancellationToken ct = default);

        /// <summary>Stages the given files (or all if empty) and creates a commit.</summary>
        Task<ApiCommit> CreateCommitAsync(string message, string authorName, string authorEmail, IReadOnlyList<string> files, CancellationToken ct = default);

        /// <summary>Merges sourceBranch into targetBranch using the specified strategy.</summary>
        Task<ApiCommit> MergeBranchAsync(string sourceBranch, string targetBranch, string strategy, CancellationToken ct = default);

        /// <summary>Pushes current branch to origin.</summary>
        Task<string> PushAsync(CancellationToken ct = default);

        /// <summary>Pulls changes from origin into the current branch.</summary>
        Task<string> PullAsync(CancellationToken ct = default);
    }
}

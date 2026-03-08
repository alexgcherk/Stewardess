using System.Threading;
using System.Threading.Tasks;
using StewardessMCPServive.Models;

namespace StewardessMCPServive.Services
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
    }
}

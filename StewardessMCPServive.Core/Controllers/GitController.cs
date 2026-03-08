using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using StewardessMCPServive.Models;
using StewardessMCPServive.Services;

namespace StewardessMCPServive.Controllers
{
    /// <summary>
    /// Read-only git operations: status, diff, log.
    /// No commits, pushes, or any repository mutations are exposed.
    /// </summary>
    [Route("api/git")]
    public sealed class GitController : BaseController
    {
        private IGitService GitService => GetService<IGitService>();

        // ── GET /api/git/repo ───────────────────────────────────────────────────

        /// <summary>Returns whether the repository root is a valid git repository.</summary>
        [HttpGet, Route("repo")]
        public async Task<IActionResult> IsGitRepository(CancellationToken ct)
        {
            try
            {
                var isGit = await GitService.IsGitRepositoryAsync(ct).ConfigureAwait(false);
                return Ok(new { IsGitRepository = isGit });
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

        // ── GET /api/git/status ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the current git status of the repository.
        /// Optionally restrict to a sub-path via <c>?path=src/MyLib</c>.
        /// </summary>
        [HttpGet, Route("status")]
        public async Task<IActionResult> GetStatus(
            [FromQuery] string path = "", CancellationToken ct = default)
        {
            try
            {
                var response = await GitService
                    .GetStatusAsync(new GitStatusRequest { Path = path ?? "" }, ct)
                    .ConfigureAwait(false);

                return Ok(response);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

        // ── POST /api/git/diff ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the unified diff for working-tree or staged changes.
        /// </summary>
        [HttpPost, Route("diff")]
        public async Task<IActionResult> GetDiff(
            [FromBody] GitDiffRequest request, CancellationToken ct)
        {
            try
            {
                var response = await GitService
                    .GetDiffAsync(request ?? new GitDiffRequest(), ct)
                    .ConfigureAwait(false);

                return Ok(response);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

        // ── GET /api/git/diff/file ──────────────────────────────────────────────

        /// <summary>
        /// Returns the unified diff for a single file.
        /// Query params: <c>path</c> (required), <c>scope</c> (default: "unstaged").
        /// </summary>
        [HttpGet, Route("diff/file")]
        public async Task<IActionResult> GetFileDiff(
            [FromQuery] string path,
            [FromQuery] string scope = "unstaged",
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(ErrorCodes.InvalidRequest, "path is required.");

            try
            {
                var response = await GitService
                    .GetDiffForFileAsync(path, scope ?? "unstaged", ct)
                    .ConfigureAwait(false);

                return Ok(response);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

        // ── GET /api/git/commits/{sha} ──────────────────────────────────────────

        /// <summary>
        /// Returns the full details of a single commit, including metadata, changed files,
        /// and optionally the diff patch.
        /// Query params: <c>includeDiff</c> (default: true).
        /// </summary>
        [HttpGet, Route("commits/{sha}")]
        public async Task<IActionResult> GetCommit(
            string sha,
            [FromQuery] bool includeDiff = true,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sha))
                return BadRequest(ErrorCodes.InvalidRequest, "sha is required.");

            try
            {
                var response = await GitService
                    .GetCommitAsync(new GitShowRequest { Sha = sha, IncludeDiff = includeDiff }, ct)
                    .ConfigureAwait(false);

                if (response.NotFound)
                    return NotFound(ErrorCodes.PathNotFound, $"Commit '{sha}' not found.");

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ErrorCodes.InvalidRequest, ex.Message);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

        // ── POST /api/git/log ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the commit history for the repository or a sub-path.
        /// </summary>
        [HttpPost, Route("log")]
        public async Task<IActionResult> GetLog(
            [FromBody] GitLogRequest request, CancellationToken ct)
        {
            try
            {
                var response = await GitService
                    .GetLogAsync(request ?? new GitLogRequest(), ct)
                    .ConfigureAwait(false);

                return Ok(response);
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }
    }
}

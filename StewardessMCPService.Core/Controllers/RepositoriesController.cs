using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using StewardessMCPService.Configuration;
using StewardessMCPService.Models;
using StewardessMCPService.Services;

namespace StewardessMCPService.Controllers
{
    [ApiController]
    [Route("repositories")]
    public sealed class RepositoriesController : ControllerBase
    {
        // ── Service resolution ───────────────────────────────────────────────────

        private McpServiceSettings Settings =>
            HttpContext.RequestServices.GetService(typeof(McpServiceSettings)) as McpServiceSettings;

        private IFileSystemService FileService =>
            HttpContext.RequestServices.GetService(typeof(IFileSystemService)) as IFileSystemService;

        private IEditService EditService =>
            HttpContext.RequestServices.GetService(typeof(IEditService)) as IEditService;

        private IGitService GitService =>
            HttpContext.RequestServices.GetService(typeof(IGitService)) as IGitService;

        private ISearchService SearchService =>
            HttpContext.RequestServices.GetService(typeof(ISearchService)) as ISearchService;

        // ── Exception handler ────────────────────────────────────────────────────

        private IActionResult HandleException(Exception ex)
        {
            return ex switch
            {
                ArgumentException ae            => BadRequest(new { message = ae.Message }),
                FileNotFoundException           => NotFound(new { message = ex.Message }),
                DirectoryNotFoundException      => NotFound(new { message = ex.Message }),
                UnauthorizedAccessException     => StatusCode(403, new { message = ex.Message }),
                OperationCanceledException      => StatusCode(408, new { message = "The operation timed out." }),
                NotSupportedException           => StatusCode(501, new { message = ex.Message }),
                InvalidOperationException ioe   => StatusCode(409, new { message = ioe.Message }),
                _                               => StatusCode(500, new { message = "An unexpected error occurred." })
            };
        }

        private ApiRepository BuildRepoInfo()
        {
            var root = Settings.RepositoryRoot;
            var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var dirInfo = new DirectoryInfo(root);
            return new ApiRepository
            {
                Id          = name,
                Name        = name,
                Description = null,
                Private     = false,
                Owner       = new ApiUser { Id = "local", Username = Environment.UserName, Email = "" },
                CreatedAt   = dirInfo.Exists ? (DateTimeOffset?)new DateTimeOffset(dirInfo.CreationTimeUtc) : null,
                UpdatedAt   = dirInfo.Exists ? (DateTimeOffset?)new DateTimeOffset(dirInfo.LastWriteTimeUtc) : null
            };
        }

        // ── /repositories ────────────────────────────────────────────────────────

        [HttpGet("")]
        public IActionResult ListRepositories()
        {
            try
            {
                var repo = BuildRepoInfo();
                return Ok(new PaginatedList<ApiRepository>
                {
                    Items = new List<ApiRepository> { repo },
                    Total = 1,
                    Page  = 1,
                    Size  = 1
                });
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        [HttpPost("")]
        public IActionResult CreateRepository([FromBody] CreateRepositoryRequest request)
        {
            return StatusCode(501, new { message = "Repository creation is not supported by this local repository tool." });
        }

        // ── /repositories/{repoId} ───────────────────────────────────────────────

        [HttpGet("{repoId}")]
        public IActionResult GetRepository(string repoId)
        {
            try
            {
                return Ok(BuildRepoInfo());
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        [HttpDelete("{repoId}")]
        public IActionResult DeleteRepository(string repoId)
        {
            return StatusCode(501, new { message = "Repository deletion is not supported by this local repository tool." });
        }

        // ── /repositories/{repoId}/branches ─────────────────────────────────────

        [HttpGet("{repoId}/branches")]
        public async Task<IActionResult> ListBranches(string repoId, CancellationToken ct)
        {
            try
            {
                var branches = await GitService.ListBranchesAsync(ct).ConfigureAwait(false);
                return Ok(new PaginatedList<ApiBranch>
                {
                    Items = branches,
                    Total = branches.Count,
                    Page  = 1,
                    Size  = branches.Count
                });
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        [HttpPost("{repoId}/branches")]
        public async Task<IActionResult> CreateBranch(
            string repoId, [FromBody] CreateBranchRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request?.Name))
                return BadRequest(new { message = "'name' is required." });
            if (string.IsNullOrWhiteSpace(request.SourceBranch))
                return BadRequest(new { message = "'sourceBranch' is required." });
            try
            {
                var branch = await GitService.CreateBranchAsync(request.Name, request.SourceBranch, ct)
                    .ConfigureAwait(false);
                return StatusCode(201, branch);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── /repositories/{repoId}/files/find ────────────────────────────────────
        // NOTE: This fixed-path POST route is declared before the wildcard file routes.

        [HttpPost("{repoId}/files/find")]
        public async Task<IActionResult> FindFiles(
            string repoId, [FromBody] FindFilesRequest request, CancellationToken ct)
        {
            try
            {
                var pattern = request?.Pattern ?? "*";
                var searchRequest = new SearchFileNamesRequest
                {
                    Pattern    = pattern,
                    MaxResults = 200
                };

                if (!string.IsNullOrWhiteSpace(request?.Path))
                    searchRequest.SearchPath = request.Path;

                var result = await SearchService.SearchFileNamesAsync(searchRequest, ct).ConfigureAwait(false);

                var files = result.Matches.Select(m => new ApiFile
                {
                    Path         = m.RelativePath,
                    Content      = null,
                    Encoding     = "utf-8",
                    LastModified = null
                }).ToList();

                return Ok(new PaginatedList<ApiFile>
                {
                    Items = files,
                    Total = files.Count,
                    Page  = 1,
                    Size  = files.Count
                });
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── /repositories/{repoId}/files/{*filePath} ─────────────────────────────

        [HttpGet("{repoId}/files/{*filePath}")]
        public async Task<IActionResult> GetFile(string repoId, string filePath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return BadRequest(new { message = "'filePath' is required." });
            try
            {
                var readRequest = new ReadFileRequest { Path = filePath };
                var result      = await FileService.ReadFileAsync(readRequest, ct).ConfigureAwait(false);

                var absPath  = System.IO.Path.Combine(Settings.RepositoryRoot,
                    filePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                var fileInfo = new FileInfo(absPath);
                DateTimeOffset? lastModified = fileInfo.Exists
                    ? (DateTimeOffset?)new DateTimeOffset(fileInfo.LastWriteTimeUtc)
                    : null;

                return Ok(new ApiFile
                {
                    Path         = filePath,
                    Content      = result.Content,
                    Encoding     = result.Encoding ?? "utf-8",
                    LastModified = lastModified
                });
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        [HttpPut("{repoId}/files/{*filePath}")]
        public async Task<IActionResult> UpdateFile(
            string repoId, string filePath, [FromBody] FileUpdateRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return BadRequest(new { message = "'filePath' is required." });
            try
            {
                var absPath = System.IO.Path.Combine(Settings.RepositoryRoot,
                    filePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                var existed = System.IO.File.Exists(absPath);

                var writeRequest = new WriteFileRequest
                {
                    Path    = filePath,
                    Content = request?.Content ?? string.Empty
                };
                await EditService.WriteFileAsync(writeRequest, ct).ConfigureAwait(false);

                var fileInfo = new FileInfo(absPath);
                var apiFile  = new ApiFile
                {
                    Path         = filePath,
                    Content      = request?.Content ?? string.Empty,
                    Encoding     = request?.Encoding ?? "utf-8",
                    LastModified = fileInfo.Exists
                        ? (DateTimeOffset?)new DateTimeOffset(fileInfo.LastWriteTimeUtc)
                        : null
                };

                return existed ? Ok(apiFile) : StatusCode(201, apiFile);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        [HttpDelete("{repoId}/files/{*filePath}")]
        public async Task<IActionResult> DeleteFile(string repoId, string filePath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return BadRequest(new { message = "'filePath' is required." });
            try
            {
                var deleteRequest = new DeleteFileRequest { Path = filePath };
                await EditService.DeleteFileAsync(deleteRequest, ct).ConfigureAwait(false);
                return NoContent();
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── /repositories/{repoId}/diff ──────────────────────────────────────────

        [HttpPost("{repoId}/diff")]
        public async Task<IActionResult> DiffBetweenCommits(
            string repoId, [FromBody] DiffRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request?.BaseSha))
                return BadRequest(new { message = "'baseSha' is required." });
            if (string.IsNullOrWhiteSpace(request.TargetSha))
                return BadRequest(new { message = "'targetSha' is required." });
            try
            {
                var diffs = await GitService.DiffBetweenCommitsAsync(request.BaseSha, request.TargetSha, ct)
                    .ConfigureAwait(false);
                return Ok(diffs);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── /repositories/{repoId}/commits ───────────────────────────────────────

        [HttpPost("{repoId}/commits")]
        public async Task<IActionResult> CreateCommit(
            string repoId, [FromBody] CommitRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request?.Message))
                return BadRequest(new { message = "'message' is required." });
            try
            {
                if (request.Changes != null)
                {
                    foreach (var change in request.Changes)
                    {
                        if (string.IsNullOrWhiteSpace(change.Path)) continue;
                        await EditService.WriteFileAsync(new WriteFileRequest
                        {
                            Path    = change.Path,
                            Content = change.Content ?? string.Empty
                        }, ct).ConfigureAwait(false);
                    }
                }

                var files = request.Changes?
                    .Select(c => c.Path)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList()
                    ?? new List<string>();

                var commit = await GitService.CreateCommitAsync(
                    request.Message,
                    request.Author?.Username,
                    request.Author?.Email,
                    files,
                    ct).ConfigureAwait(false);

                return StatusCode(201, commit);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── /repositories/{repoId}/merge ─────────────────────────────────────────

        [HttpPost("{repoId}/merge")]
        public async Task<IActionResult> MergeBranches(
            string repoId, [FromBody] MergeRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request?.SourceBranch))
                return BadRequest(new { message = "'sourceBranch' is required." });
            if (string.IsNullOrWhiteSpace(request.TargetBranch))
                return BadRequest(new { message = "'targetBranch' is required." });
            try
            {
                var commit = await GitService.MergeBranchAsync(
                    request.SourceBranch, request.TargetBranch, request.Strategy ?? "recursive", ct)
                    .ConfigureAwait(false);
                return Ok(commit);
            }
            catch (InvalidOperationException ioe) when (ioe.Message.Contains("Merge conflict"))
            {
                return StatusCode(409, new { message = ioe.Message });
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── /repositories/{repoId}/pulls ─────────────────────────────────────────

        [HttpGet("{repoId}/pulls")]
        public IActionResult ListPullRequests(string repoId)
        {
            return StatusCode(501, new { message = "Pull requests require a hosted git service (GitHub/GitLab/Bitbucket) and are not supported by this local repository tool." });
        }

        [HttpPost("{repoId}/pulls")]
        public IActionResult CreatePullRequest(string repoId, [FromBody] PullRequestRequest request)
        {
            return StatusCode(501, new { message = "Pull requests require a hosted git service (GitHub/GitLab/Bitbucket) and are not supported by this local repository tool." });
        }

        [HttpGet("{repoId}/pulls/{prId}")]
        public IActionResult GetPullRequest(string repoId, string prId)
        {
            return StatusCode(501, new { message = "Pull requests require a hosted git service (GitHub/GitLab/Bitbucket) and are not supported by this local repository tool." });
        }

        [HttpPatch("{repoId}/pulls/{prId}")]
        public IActionResult UpdatePullRequest(string repoId, string prId, [FromBody] object request)
        {
            return StatusCode(501, new { message = "Pull requests require a hosted git service (GitHub/GitLab/Bitbucket) and are not supported by this local repository tool." });
        }

        // ── /repositories/{repoId}/push ──────────────────────────────────────────

        [HttpPost("{repoId}/push")]
        public async Task<IActionResult> PushRepository(string repoId, CancellationToken ct)
        {
            try
            {
                var output = await GitService.PushAsync(ct).ConfigureAwait(false);
                return Ok(new { message = output.Length > 0 ? output : "Pushed successfully." });
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── /repositories/{repoId}/pull ──────────────────────────────────────────

        [HttpPost("{repoId}/pull")]
        public async Task<IActionResult> PullRepository(string repoId, CancellationToken ct)
        {
            try
            {
                var output = await GitService.PullAsync(ct).ConfigureAwait(false);
                return Ok(new { message = output.Length > 0 ? output : "Pulled successfully." });
            }
            catch (Exception ex) { return HandleException(ex); }
        }
    }
}

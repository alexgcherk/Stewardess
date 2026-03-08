using System;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using StewardessMCPServive.Models;
using StewardessMCPServive.Services;

namespace StewardessMCPServive.Controllers
{
    /// <summary>
    /// Repository navigation endpoints.
    ///
    /// GET  /api/repository                  — get_repository_info
    /// GET  /api/repository/directory        — list_directory
    /// GET  /api/repository/tree             — list_tree
    /// GET  /api/repository/exists           — file_exists / directory_exists
    /// GET  /api/repository/metadata         — get_file_metadata
    /// GET  /api/repository/solution         — get_solution_info
    /// GET  /api/repository/configs          — find_config_files
    /// </summary>
    [Route("api/repository")]
    public sealed class RepositoryController : BaseController
    {
        private IFileSystemService    FileService    => GetService<IFileSystemService>();
        private IProjectDetectionService ProjService => GetService<IProjectDetectionService>();

        // ── get_repository_info ──────────────────────────────────────────────────

        /// <summary>Returns high-level repository information and active policy.</summary>
        [HttpGet, Route("")]
        public IActionResult GetRepositoryInfo()
        {
            try
            {
                var result = FileService.GetRepositoryInfoAsync(CancellationToken.None).GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── list_directory ───────────────────────────────────────────────────────

        /// <summary>Lists the immediate contents of a directory.</summary>
        [HttpGet, Route("directory")]
        public IActionResult ListDirectory(
            [FromQuery] string path = "",
            [FromQuery] bool includeBlocked = false,
            [FromQuery] string? namePattern = null)
        {
            try
            {
                var request = new ListDirectoryRequest
                {
                    Path           = path,
                    IncludeBlocked = includeBlocked,
                    NamePattern    = namePattern
                };
                var result = FileService.ListDirectoryAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── list_tree ────────────────────────────────────────────────────────────

        /// <summary>Returns a recursive directory tree.</summary>
        [HttpGet, Route("tree")]
        public IActionResult ListTree(
            [FromQuery] string path = "",
            [FromQuery] int maxDepth = 3,
            [FromQuery] bool includeBlocked = false,
            [FromQuery] bool directoriesOnly = false)
        {
            try
            {
                var request = new ListTreeRequest
                {
                    Path           = path,
                    MaxDepth       = maxDepth,
                    IncludeBlocked = includeBlocked,
                    DirectoriesOnly = directoriesOnly
                };
                var result = FileService.ListTreeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── file_exists ──────────────────────────────────────────────────────────

        /// <summary>Checks whether a path exists inside the repository.</summary>
        [HttpGet, Route("exists")]
        public IActionResult PathExists([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(ErrorCodes.MissingParameter, "'path' is required.");
            try
            {
                var result = FileService.PathExistsAsync(path, CancellationToken.None).GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── get_file_metadata ────────────────────────────────────────────────────

        /// <summary>Returns detailed metadata for a file or directory.</summary>
        [HttpGet, Route("metadata")]
        public IActionResult GetMetadata([FromQuery] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(ErrorCodes.MissingParameter, "'path' is required.");
            try
            {
                var request = new FileMetadataRequest { Path = path };
                var result  = FileService.GetMetadataAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── get_solution_info ────────────────────────────────────────────────────

        /// <summary>Returns solution / project info discovered in the repository.</summary>
        [HttpGet, Route("solution")]
        public IActionResult GetSolutionInfo()
        {
            try
            {
                var result = ProjService.GetSolutionInfoAsync(CancellationToken.None).GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── find_config_files ────────────────────────────────────────────────────

        /// <summary>Locates common configuration files in the repository.</summary>
        [HttpGet, Route("configs")]
        public IActionResult FindConfigFiles()
        {
            try
            {
                var result = ProjService.FindConfigFilesAsync(CancellationToken.None).GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }
    }
}

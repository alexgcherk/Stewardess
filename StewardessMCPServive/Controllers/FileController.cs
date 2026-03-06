using System;
using System.Net.Http;
using System.Threading;
using System.Web.Http;
using StewardessMCPServive.Models;
using StewardessMCPServive.Services;

namespace StewardessMCPServive.Controllers
{
    /// <summary>
    /// File reading and metadata endpoints.
    ///
    /// GET  /api/files/read          — read_file
    /// GET  /api/files/range         — read_file_range
    /// POST /api/files/read-multiple — read_multiple_files
    /// GET  /api/files/hash          — get_file_hash
    /// GET  /api/files/structure     — get_file_structure
    /// GET  /api/files/encoding      — detect encoding
    /// GET  /api/files/line-ending   — detect line endings
    /// </summary>
    [RoutePrefix("api/files")]
    public sealed class FileController : BaseController
    {
        private IFileSystemService FileService => GetService<IFileSystemService>();

        // ── read_file ────────────────────────────────────────────────────────────

        /// <summary>Reads the full (or partial) content of a file.</summary>
        [HttpGet, Route("read")]
        public HttpResponseMessage ReadFile(
            [FromUri] string path,
            [FromUri] long? maxBytes = null,
            [FromUri] bool returnBase64 = false)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(ErrorCodes.MissingParameter, "'path' is required.");
            try
            {
                var request = new ReadFileRequest { Path = path, MaxBytes = maxBytes, ReturnBase64 = returnBase64 };
                var result  = FileService.ReadFileAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── read_file_range ──────────────────────────────────────────────────────

        /// <summary>Reads a contiguous range of lines from a file.</summary>
        [HttpGet, Route("range")]
        public HttpResponseMessage ReadRange(
            [FromUri] string path,
            [FromUri] int startLine = 1,
            [FromUri] int endLine   = -1,
            [FromUri] bool includeLineNumbers = true)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(ErrorCodes.MissingParameter, "'path' is required.");
            try
            {
                var request = new ReadFileRangeRequest
                {
                    Path               = path,
                    StartLine          = startLine,
                    EndLine            = endLine,
                    IncludeLineNumbers = includeLineNumbers
                };
                var result = FileService.ReadFileRangeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── read_multiple_files ──────────────────────────────────────────────────

        /// <summary>Reads several files in a single POST call.</summary>
        [HttpPost, Route("read-multiple")]
        public HttpResponseMessage ReadMultiple([FromBody] ReadMultipleFilesRequest request)
        {
            if (request?.Paths == null || request.Paths.Count == 0)
                return BadRequest(ErrorCodes.MissingParameter, "'paths' must be a non-empty array.");
            try
            {
                var result = FileService.ReadMultipleFilesAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── get_file_hash ────────────────────────────────────────────────────────

        /// <summary>Computes a hash digest of a file.</summary>
        [HttpGet, Route("hash")]
        public HttpResponseMessage GetHash(
            [FromUri] string path,
            [FromUri] string algorithm = "SHA256")
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(ErrorCodes.MissingParameter, "'path' is required.");
            try
            {
                var request = new FileHashRequest { Path = path, Algorithm = algorithm };
                var result  = FileService.GetFileHashAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── get_file_structure ───────────────────────────────────────────────────

        /// <summary>Returns a best-effort structural summary of a code file.</summary>
        [HttpGet, Route("structure")]
        public HttpResponseMessage GetStructure([FromUri] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(ErrorCodes.MissingParameter, "'path' is required.");
            try
            {
                var request = new FileStructureSummaryRequest { Path = path };
                var result  = FileService.GetFileStructureSummaryAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── detect encoding ──────────────────────────────────────────────────────

        /// <summary>Detects the character encoding of a file.</summary>
        [HttpGet, Route("encoding")]
        public HttpResponseMessage DetectEncoding([FromUri] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(ErrorCodes.MissingParameter, "'path' is required.");
            try
            {
                var enc = FileService.DetectEncodingAsync(path, CancellationToken.None).GetAwaiter().GetResult();
                return Ok(new { path, encoding = enc });
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── detect line endings ──────────────────────────────────────────────────

        /// <summary>Detects the dominant line-ending style of a file.</summary>
        [HttpGet, Route("line-ending")]
        public HttpResponseMessage DetectLineEnding([FromUri] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest(ErrorCodes.MissingParameter, "'path' is required.");
            try
            {
                var le = FileService.DetectLineEndingAsync(path, CancellationToken.None).GetAwaiter().GetResult();
                return Ok(new { path, lineEnding = le });
            }
            catch (Exception ex) { return HandleException(ex); }
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using StewardessMCPServive.Models;
using StewardessMCPServive.Services;

namespace StewardessMCPServive.Controllers
{
    /// <summary>
    /// REST endpoints for all file-write, edit, patch, and rollback operations.
    /// Every modifying endpoint requires write access and records an audit entry.
    /// </summary>
    [Route("api/edit")]
    public sealed class EditController : BaseController
    {
        // ── write_file ───────────────────────────────────────────────────────────

        /// <summary>Overwrites (or creates) a file with the supplied content.</summary>
        [HttpPost, Route("write")]
        public async Task<IActionResult> WriteFile(
            [FromBody] WriteFileRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().WriteFileAsync(request, ct);
                return result.WasDryRun ? Ok(result) : Created(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── create_file ──────────────────────────────────────────────────────────

        /// <summary>Creates a new file; optionally fails if the file already exists.</summary>
        [HttpPost, Route("create-file")]
        public async Task<IActionResult> CreateFile(
            [FromBody] CreateFileRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().CreateFileAsync(request, ct);
                return result.WasDryRun ? Ok(result) : Created(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── create_directory ─────────────────────────────────────────────────────

        /// <summary>Creates a directory (with optional parent creation).</summary>
        [HttpPost, Route("create-directory")]
        public async Task<IActionResult> CreateDirectory(
            [FromBody] CreateDirectoryRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().CreateDirectoryAsync(request, ct);
                return result.WasDryRun ? Ok(result) : Created(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── rename_path ──────────────────────────────────────────────────────────

        /// <summary>Renames a file or directory in place (new name only, same parent).</summary>
        [HttpPost, Route("rename")]
        public async Task<IActionResult> RenamePath(
            [FromBody] RenamePathRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().RenamePathAsync(request, ct);
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── move_path ────────────────────────────────────────────────────────────

        /// <summary>Moves a file or directory to a new location within the repository.</summary>
        [HttpPost, Route("move")]
        public async Task<IActionResult> MovePath(
            [FromBody] MovePathRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().MovePathAsync(request, ct);
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── delete_file ──────────────────────────────────────────────────────────

        /// <summary>
        /// Deletes a single file. A backup is created (and a rollback token returned)
        /// unless <c>options.createBackup</c> is false.
        /// When <c>ApprovalRequiredForDestructive</c> is enabled an approval token is required.
        /// </summary>
        [HttpPost, Route("delete-file")]
        public async Task<IActionResult> DeleteFile(
            [FromBody] DeleteFileRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().DeleteFileAsync(request, ct);
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── delete_directory ─────────────────────────────────────────────────────

        /// <summary>Deletes a directory. Requires <c>recursive=true</c> for non-empty directories.</summary>
        [HttpPost, Route("delete-directory")]
        public async Task<IActionResult> DeleteDirectory(
            [FromBody] DeleteDirectoryRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().DeleteDirectoryAsync(request, ct);
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── append_file ──────────────────────────────────────────────────────────

        /// <summary>Appends content to the end of an existing file.</summary>
        [HttpPost, Route("append")]
        public async Task<IActionResult> AppendFile(
            [FromBody] AppendFileRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().AppendFileAsync(request, ct);
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── replace_text ─────────────────────────────────────────────────────────

        /// <summary>Replaces occurrences of a literal string within a file.</summary>
        [HttpPost, Route("replace-text")]
        public async Task<IActionResult> ReplaceText(
            [FromBody] ReplaceTextRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().ReplaceTextAsync(request, ct);
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── replace_lines ────────────────────────────────────────────────────────

        /// <summary>Replaces a range of lines in a file with new content.</summary>
        [HttpPost, Route("replace-lines")]
        public async Task<IActionResult> ReplaceLines(
            [FromBody] ReplaceLinesRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().ReplaceLinesAsync(request, ct);
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── patch_file ───────────────────────────────────────────────────────────

        /// <summary>Applies a unified diff patch to a single file.</summary>
        [HttpPost, Route("patch")]
        public async Task<IActionResult> PatchFile(
            [FromBody] PatchFileRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().PatchFileAsync(request, ct);
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── apply_diff ───────────────────────────────────────────────────────────

        /// <summary>Applies a multi-file unified diff in one operation.</summary>
        [HttpPost, Route("apply-diff")]
        public async Task<IActionResult> ApplyDiff(
            [FromBody] ApplyDiffRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().ApplyDiffAsync(request, ct);
                var payload = ApiResponse<BatchEditResult>.Ok(result, RequestId);
                return StatusCode(result.Success ? 200 : 422, payload);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── apply_batch_edits ────────────────────────────────────────────────────

        /// <summary>
        /// Executes multiple heterogeneous edits atomically.
        /// On first failure all previous operations in the batch are rolled back.
        /// </summary>
        [HttpPost, Route("batch")]
        public async Task<IActionResult> ApplyBatchEdits(
            [FromBody] BatchEditRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().ApplyBatchEditsAsync(request, ct);
                var payload = ApiResponse<BatchEditResult>.Ok(result, RequestId);
                return StatusCode(result.Success ? 200 : 422, payload);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── preview_changes ──────────────────────────────────────────────────────

        /// <summary>
        /// Dry-runs a batch of edits and returns a per-file diff preview plus a
        /// one-time approval token (valid 15 minutes) for the proposed changes.
        /// </summary>
        [HttpPost, Route("preview")]
        public async Task<IActionResult> PreviewChanges(
            [FromBody] PreviewChangesRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().PreviewChangesAsync(request, ct);
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }

        // ── rollback ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Restores a file from the backup identified by the rollback token returned by
        /// a prior write/edit/delete operation. Tokens are single-use.
        /// </summary>
        [HttpPost, Route("rollback")]
        public async Task<IActionResult> Rollback(
            [FromBody] RollbackRequest request, CancellationToken ct = default)
        {
            if (request == null)
                return BadRequest(ErrorCodes.InvalidRequest, "Request body is required.");
            try
            {
                var result = await GetService<IEditService>().RollbackAsync(request, ct);
                return Ok(result);
            }
            catch (Exception ex) { return HandleException(ex); }
        }
    }
}

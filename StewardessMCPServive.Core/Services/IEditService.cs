using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPServive.Models;

namespace StewardessMCPServive.Services
{
    /// <summary>
    /// All write, patch, and structural-change operations on the repository.
    /// Every mutating method accepts <see cref="EditOptions"/> which supports
    /// dry-run simulation and automatic backup creation.
    /// </summary>
    public interface IEditService
    {
        // ── Single-file writes ───────────────────────────────────────────────────

        /// <summary>Overwrites (or creates) a file with new content.</summary>
        Task<EditResult> WriteFileAsync(WriteFileRequest request, CancellationToken ct = default);

        /// <summary>Creates a new file; optionally fails if it already exists.</summary>
        Task<EditResult> CreateFileAsync(CreateFileRequest request, CancellationToken ct = default);

        /// <summary>Creates a directory, optionally including all parent directories.</summary>
        Task<EditResult> CreateDirectoryAsync(CreateDirectoryRequest request, CancellationToken ct = default);

        // ── Move / rename ────────────────────────────────────────────────────────

        /// <summary>Renames a file or directory in place (no path change).</summary>
        Task<EditResult> RenamePathAsync(RenamePathRequest request, CancellationToken ct = default);

        /// <summary>Moves a file or directory to a new location within the repository.</summary>
        Task<EditResult> MovePathAsync(MovePathRequest request, CancellationToken ct = default);

        // ── Deletion ─────────────────────────────────────────────────────────────

        /// <summary>Deletes a single file, optionally creating a backup first.</summary>
        Task<EditResult> DeleteFileAsync(DeleteFileRequest request, CancellationToken ct = default);

        /// <summary>Deletes a directory, optionally recursively.</summary>
        Task<EditResult> DeleteDirectoryAsync(DeleteDirectoryRequest request, CancellationToken ct = default);

        // ── In-place edits ───────────────────────────────────────────────────────

        /// <summary>Appends content to the end of an existing file.</summary>
        Task<EditResult> AppendFileAsync(AppendFileRequest request, CancellationToken ct = default);

        /// <summary>Replaces all (or a limited number of) occurrences of a literal string.</summary>
        Task<EditResult> ReplaceTextAsync(ReplaceTextRequest request, CancellationToken ct = default);

        /// <summary>Replaces a contiguous range of lines with new content.</summary>
        Task<EditResult> ReplaceLinesAsync(ReplaceLinesRequest request, CancellationToken ct = default);

        // ── Patch / diff ─────────────────────────────────────────────────────────

        /// <summary>Applies a unified diff patch to a single file.</summary>
        Task<EditResult> PatchFileAsync(PatchFileRequest request, CancellationToken ct = default);

        /// <summary>Applies a multi-file unified diff in one operation.</summary>
        Task<BatchEditResult> ApplyDiffAsync(ApplyDiffRequest request, CancellationToken ct = default);

        // ── Batch ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Executes multiple heterogeneous edits atomically.
        /// If any operation fails all changes are rolled back (or skipped in dry-run).
        /// </summary>
        Task<BatchEditResult> ApplyBatchEditsAsync(BatchEditRequest request, CancellationToken ct = default);

        // ── Rollback ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Restores a file from a backup identified by a prior operation's rollback token.
        /// </summary>
        Task<RollbackResult> RollbackAsync(RollbackRequest request, CancellationToken ct = default);

        // ── Preview ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Dry-runs a batch of edits and returns a preview diff + an approval token.
        /// The token may be passed to destructive operations when
        /// ApprovalRequiredForDestructive is enabled.
        /// </summary>
        Task<PreviewChangesResponse> PreviewChangesAsync(PreviewChangesRequest request, CancellationToken ct = default);
    }
}

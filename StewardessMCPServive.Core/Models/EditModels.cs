using System;
using System.Collections.Generic;

namespace StewardessMCPServive.Models
{
    // ────────────────────────────────────────────────────────────────────────────
    //  File write / edit request models
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Common options applied to every mutating operation.
    /// </summary>
    public sealed class EditOptions
    {
        /// <summary>
        /// When true, the operation is simulated and no files are changed.
        /// The response contains the full proposed diff.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// When true (default), a backup copy is created before overwriting.
        /// The backup path is returned in <see cref="EditResult.BackupPath"/>.
        /// </summary>
        public bool CreateBackup { get; set; } = true;

        /// <summary>
        /// Optional agent-supplied reason for the change, recorded in the audit log.
        /// </summary>
        public string ChangeReason { get; set; }

        /// <summary>Agent-level session/correlation identifier for grouping related operations.</summary>
        public string SessionId { get; set; }
    }

    // ── write_file ───────────────────────────────────────────────────────────────

    /// <summary>Overwrites (or creates) a file with new content.</summary>
    public sealed class WriteFileRequest
    {
        /// <summary>File path relative to repository root.</summary>
        public string Path { get; set; }

        /// <summary>New full content of the file.</summary>
        public string Content { get; set; }

        /// <summary>
        /// Encoding to use when writing.  Supported values: "utf-8", "utf-8-bom",
        /// "utf-16", "ascii".  Defaults to "utf-8".
        /// </summary>
        public string Encoding { get; set; } = "utf-8";

        /// <summary>Line ending style: "lf", "crlf", "cr", "auto" (preserve existing, default).</summary>
        public string LineEnding { get; set; } = "auto";

        /// <summary>Write options such as dry-run mode and backup control.</summary>
        public EditOptions Options { get; set; } = new EditOptions();
    }

    // ── create_file ──────────────────────────────────────────────────────────────

    /// <summary>Creates a new file. Fails if the file already exists unless Overwrite is true.</summary>
    public sealed class CreateFileRequest
    {
        /// <summary>File path relative to repository root.</summary>
        public string Path { get; set; }
        /// <summary>Initial content of the new file.</summary>
        public string Content { get; set; } = "";
        /// <summary>Encoding to use when writing the file (e.g. "utf-8").</summary>
        public string Encoding { get; set; } = "utf-8";
        /// <summary>When true, overwrites an existing file.</summary>
        public bool Overwrite { get; set; } = false;
        /// <summary>Write options such as dry-run mode and backup control.</summary>
        public EditOptions Options { get; set; } = new EditOptions();
    }

    // ── create_directory ─────────────────────────────────────────────────────────

    /// <summary>Creates a new directory at the given path.</summary>
    public sealed class CreateDirectoryRequest
    {
        /// <summary>Directory path relative to repository root.</summary>
        public string Path { get; set; }

        /// <summary>When true, creates all missing parent directories.</summary>
        public bool CreateParents { get; set; } = true;

        /// <summary>Write options such as dry-run mode and backup control.</summary>
        public EditOptions Options { get; set; } = new EditOptions();
    }

    // ── rename_path / move_path ──────────────────────────────────────────────────

    /// <summary>Renames a file or directory within its current parent directory.</summary>
    public sealed class RenamePathRequest
    {
        /// <summary>Path of the file or directory to rename, relative to repository root.</summary>
        public string Path { get; set; }

        /// <summary>New name only (not a full path). To move to another directory use move_path.</summary>
        public string NewName { get; set; }

        /// <summary>Write options such as dry-run mode and backup control.</summary>
        public EditOptions Options { get; set; } = new EditOptions();
    }

    /// <summary>Moves a file or directory to a new location within the repository.</summary>
    public sealed class MovePathRequest
    {
        /// <summary>Source path relative to repository root.</summary>
        public string SourcePath { get; set; }

        /// <summary>Destination path relative to repository root.</summary>
        public string DestinationPath { get; set; }

        /// <summary>When true, overwrites an existing path at the destination.</summary>
        public bool Overwrite { get; set; } = false;
        /// <summary>Write options such as dry-run mode and backup control.</summary>
        public EditOptions Options { get; set; } = new EditOptions();
    }

    // ── delete_file / delete_directory ──────────────────────────────────────────

    /// <summary>Deletes a single file within the repository.</summary>
    public sealed class DeleteFileRequest
    {
        /// <summary>File path relative to repository root.</summary>
        public string Path { get; set; }

        /// <summary>
        /// Confirmation token returned by a prior preview_changes call.
        /// Required when ApprovalRequiredForDestructive is enabled.
        /// </summary>
        public string ApprovalToken { get; set; }

        /// <summary>Write options such as dry-run mode and backup control.</summary>
        public EditOptions Options { get; set; } = new EditOptions();
    }

    /// <summary>Deletes a directory, optionally recursively.</summary>
    public sealed class DeleteDirectoryRequest
    {
        /// <summary>Directory path relative to repository root.</summary>
        public string Path { get; set; }

        /// <summary>When false (default), deletion fails if the directory is non-empty.</summary>
        public bool Recursive { get; set; } = false;

        /// <summary>Approval token required when ApprovalRequiredForDestructive is enabled.</summary>
        public string ApprovalToken { get; set; }
        /// <summary>Write options such as dry-run mode and backup control.</summary>
        public EditOptions Options { get; set; } = new EditOptions();
    }

    // ── append_file ──────────────────────────────────────────────────────────────

    /// <summary>Appends content to the end of an existing file.</summary>
    public sealed class AppendFileRequest
    {
        /// <summary>File path relative to repository root.</summary>
        public string Path { get; set; }
        /// <summary>Text to append.</summary>
        public string Content { get; set; }

        /// <summary>When true, ensures the content starts on a new line.</summary>
        public bool EnsureNewLine { get; set; } = true;

        /// <summary>Write options such as dry-run mode and backup control.</summary>
        public EditOptions Options { get; set; } = new EditOptions();
    }

    // ── replace_text ─────────────────────────────────────────────────────────────

    /// <summary>Replaces all occurrences of a literal string within a file.</summary>
    public sealed class ReplaceTextRequest
    {
        /// <summary>File path relative to repository root.</summary>
        public string Path { get; set; }
        /// <summary>The literal text to find.</summary>
        public string OldText { get; set; }
        /// <summary>The replacement text.</summary>
        public string NewText { get; set; }
        /// <summary>When true, performs a case-insensitive search.</summary>
        public bool IgnoreCase { get; set; } = false;

        /// <summary>When > 0, limits the number of replacements made.</summary>
        public int MaxReplacements { get; set; } = 0;

        /// <summary>Write options such as dry-run mode and backup control.</summary>
        public EditOptions Options { get; set; } = new EditOptions();
    }

    // ── replace_lines ────────────────────────────────────────────────────────────

    /// <summary>Replaces a range of lines in a file with new content.</summary>
    public sealed class ReplaceLinesRequest
    {
        /// <summary>File path relative to repository root.</summary>
        public string Path { get; set; }

        /// <summary>1-based start line (inclusive).</summary>
        public int StartLine { get; set; }

        /// <summary>1-based end line (inclusive).</summary>
        public int EndLine { get; set; }

        /// <summary>Replacement text (can span multiple lines).</summary>
        public string NewContent { get; set; }

        /// <summary>Write options such as dry-run mode and backup control.</summary>
        public EditOptions Options { get; set; } = new EditOptions();
    }

    // ── patch_file / apply_unified_diff ─────────────────────────────────────────

    /// <summary>Applies a unified diff patch to a single file.</summary>
    public sealed class PatchFileRequest
    {
        /// <summary>File path relative to repository root.</summary>
        public string Path { get; set; }

        /// <summary>Unified diff text (--- a/ +++ b/ format).</summary>
        public string Patch { get; set; }

        /// <summary>Number of context lines for fuzzy matching (default 3).</summary>
        public int FuzzFactor { get; set; } = 3;

        /// <summary>Write options such as dry-run mode and backup control.</summary>
        public EditOptions Options { get; set; } = new EditOptions();
    }

    /// <summary>Applies a multi-file unified diff in one operation.</summary>
    public sealed class ApplyDiffRequest
    {
        /// <summary>Full unified diff text that may span multiple files.</summary>
        public string Diff { get; set; }

        /// <summary>Number of context lines for fuzzy matching (default 3).</summary>
        public int FuzzFactor { get; set; } = 3;

        /// <summary>Write options such as dry-run mode and backup control.</summary>
        public EditOptions Options { get; set; } = new EditOptions();
    }

    // ── apply_batch_edits ────────────────────────────────────────────────────────

    /// <summary>Executes multiple edit operations atomically (all succeed or all roll back).</summary>
    public sealed class BatchEditRequest
    {
        /// <summary>The ordered list of individual edit operations to apply.</summary>
        public List<BatchEditItem> Edits { get; set; } = new List<BatchEditItem>();

        /// <summary>When true, all edits are dry-run and nothing is written.</summary>
        public bool DryRun { get; set; } = false;

        /// <summary>Free-text description of why this batch of changes is being made.</summary>
        public string ChangeReason { get; set; }
        /// <summary>Agent session or correlation identifier.</summary>
        public string SessionId { get; set; }
    }

    /// <summary>A single edit operation within a batch request.</summary>
    public sealed class BatchEditItem
    {
        /// <summary>
        /// Operation type: "write_file", "create_file", "delete_file",
        /// "replace_text", "replace_lines", "patch_file", "append_file".
        /// </summary>
        public string Operation { get; set; }

        /// <summary>Target file path relative to repository root.</summary>
        public string Path { get; set; }

        // Polymorphic payload — only the fields relevant to the operation need to be set.
        /// <summary>File content for write/create operations.</summary>
        public string Content { get; set; }
        /// <summary>Text to find for replace_text operations.</summary>
        public string OldText { get; set; }
        /// <summary>Replacement text for replace_text operations.</summary>
        public string NewText { get; set; }
        /// <summary>1-based start line for replace_lines operations.</summary>
        public int StartLine { get; set; }
        /// <summary>1-based end line for replace_lines operations.</summary>
        public int EndLine { get; set; }
        /// <summary>Unified diff text for patch_file operations.</summary>
        public string Patch { get; set; }
        /// <summary>Encoding override (e.g. "utf-8") for write/create operations.</summary>
        public string Encoding { get; set; }
        /// <summary>When true, performs case-insensitive matching for replace_text operations.</summary>
        public bool IgnoreCase { get; set; }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  Edit results
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Result of a single edit operation.</summary>
    public sealed class EditResult
    {
        /// <summary>True when the operation completed without error.</summary>
        public bool Success { get; set; }
        /// <summary>Relative path of the file that was edited.</summary>
        public string RelativePath { get; set; }
        /// <summary>Name of the operation (e.g. "write_file").</summary>
        public string Operation { get; set; }

        /// <summary>True when the operation was a dry run and no file was changed.</summary>
        public bool WasDryRun { get; set; }

        /// <summary>Relative path of the backup file created before the edit; null if no backup.</summary>
        public string BackupPath { get; set; }

        /// <summary>Rollback token that can be passed to rollback_last_change.</summary>
        public string RollbackToken { get; set; }

        /// <summary>Unified diff of the proposed (dry-run) or applied change.</summary>
        public string Diff { get; set; }

        /// <summary>Number of replacements or lines affected.</summary>
        public int AffectedCount { get; set; }

        /// <summary>Error description when the operation failed.</summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>Result of a batch edit operation.</summary>
    public sealed class BatchEditResult
    {
        /// <summary>True when all individual operations succeeded.</summary>
        public bool Success { get; set; }
        /// <summary>True when the entire batch was a dry run.</summary>
        public bool WasDryRun { get; set; }
        /// <summary>Per-item results in the same order as the request.</summary>
        public List<EditResult> Results { get; set; } = new List<EditResult>();
        /// <summary>Number of individual operations that succeeded.</summary>
        public int SucceededCount { get; set; }
        /// <summary>Number of individual operations that failed.</summary>
        public int FailedCount { get; set; }
        /// <summary>True when a failure caused the entire batch to be rolled back.</summary>
        public bool WasRolledBack { get; set; }
        /// <summary>Explanation of why the batch was rolled back.</summary>
        public string RollbackReason { get; set; }
    }

    // ── preview_changes ──────────────────────────────────────────────────────────

    /// <summary>Request to preview proposed changes before applying them.</summary>
    public sealed class PreviewChangesRequest
    {
        /// <summary>List of files to preview changes for.</summary>
        public List<string> Paths { get; set; } = new List<string>();

        /// <summary>The intended edit operations (as a BatchEditRequest with DryRun=true).</summary>
        public BatchEditRequest ProposedEdits { get; set; }
    }

    /// <summary>Response containing per-file diffs and a one-time approval token.</summary>
    public sealed class PreviewChangesResponse
    {
        /// <summary>Diffs showing the proposed changes for each affected file.</summary>
        public List<FilePreview> FilePreviews { get; set; } = new List<FilePreview>();

        /// <summary>
        /// One-time approval token for submitting the previewed changes.
        /// Valid for the configured approval window.
        /// </summary>
        public string ApprovalToken { get; set; }

        /// <summary>UTC time after which the approval token is no longer valid.</summary>
        public DateTimeOffset TokenExpiry { get; set; }
    }

    /// <summary>Diff preview for a single file.</summary>
    public sealed class FilePreview
    {
        /// <summary>Repository-relative path of the file.</summary>
        public string RelativePath { get; set; }
        /// <summary>Operation being previewed (e.g. "write_file").</summary>
        public string Operation { get; set; }
        /// <summary>Unified diff of the proposed change.</summary>
        public string UnifiedDiff { get; set; }
        /// <summary>Number of lines that would be added.</summary>
        public int LinesAdded { get; set; }
        /// <summary>Number of lines that would be removed.</summary>
        public int LinesRemoved { get; set; }
    }

    // ── rollback ────────────────────────────────────────────────────────────────

    /// <summary>Request to roll back a prior edit using its rollback token.</summary>
    public sealed class RollbackRequest
    {
        /// <summary>Token returned by a prior edit operation's <see cref="EditResult.RollbackToken"/>.</summary>
        public string RollbackToken { get; set; }
    }

    /// <summary>Result of a rollback operation.</summary>
    public sealed class RollbackResult
    {
        /// <summary>True when the rollback completed successfully.</summary>
        public bool Success { get; set; }
        /// <summary>Repository-relative path of the restored file.</summary>
        public string RelativePath { get; set; }
        /// <summary>Backup path that was used to restore the file.</summary>
        public string RestoredFromBackup { get; set; }
        /// <summary>Error description when the rollback failed.</summary>
        public string ErrorMessage { get; set; }
    }
}

// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPService.Models;

namespace StewardessMCPService.Services
{
    /// <summary>
    /// Read-only and metadata operations on the repository file system.
    /// All paths are relative to the configured RepositoryRoot.
    /// </summary>
    public interface IFileSystemService
    {
        // ── Repository info ──────────────────────────────────────────────────────

        /// <summary>Returns high-level information about the served repository.</summary>
        Task<RepositoryInfoResponse> GetRepositoryInfoAsync(CancellationToken ct = default);

        // ── Directory navigation ─────────────────────────────────────────────────

        /// <summary>Lists immediate children of a directory.</summary>
        Task<ListDirectoryResponse> ListDirectoryAsync(ListDirectoryRequest request, CancellationToken ct = default);

        /// <summary>Returns a recursive directory tree up to the requested depth.</summary>
        Task<ListTreeResponse> ListTreeAsync(ListTreeRequest request, CancellationToken ct = default);

        /// <summary>Checks whether a path (file or directory) exists within the repository.</summary>
        Task<PathExistsResponse> PathExistsAsync(string relativePath, CancellationToken ct = default);

        /// <summary>Returns detailed metadata for a single file or directory.</summary>
        Task<FileMetadataResponse> GetMetadataAsync(FileMetadataRequest request, CancellationToken ct = default);

        // ── File reading ─────────────────────────────────────────────────────────

        /// <summary>Reads the full or partial content of a file.</summary>
        Task<ReadFileResponse> ReadFileAsync(ReadFileRequest request, CancellationToken ct = default);

        /// <summary>Reads a binary or media file and returns it as base64 with MIME type.</summary>
        Task<ReadMediaFileResponse> ReadMediaFileAsync(ReadMediaFileRequest request, CancellationToken ct = default);

        /// <summary>Reads a contiguous range of lines from a file.</summary>
        Task<ReadFileRangeResponse> ReadFileRangeAsync(ReadFileRangeRequest request, CancellationToken ct = default);

        /// <summary>Reads multiple files in a single call.</summary>
        Task<ReadMultipleFilesResponse> ReadMultipleFilesAsync(ReadMultipleFilesRequest request, CancellationToken ct = default);

        /// <summary>Computes a hash digest of the file contents.</summary>
        Task<FileHashResponse> GetFileHashAsync(FileHashRequest request, CancellationToken ct = default);

        /// <summary>
        /// Returns a best-effort structural summary of a code file
        /// (namespaces, types, members) parsed via text heuristics.
        /// </summary>
        Task<FileStructureSummaryResponse> GetFileStructureSummaryAsync(FileStructureSummaryRequest request, CancellationToken ct = default);

        // ── Encoding / format helpers ────────────────────────────────────────────

        /// <summary>Detects the encoding of a file (UTF-8, UTF-16, ASCII, etc.).</summary>
        Task<string> DetectEncodingAsync(string relativePath, CancellationToken ct = default);

        /// <summary>Detects the dominant line-ending style in a file ("LF", "CRLF", "CR", "Mixed").</summary>
        Task<string> DetectLineEndingAsync(string relativePath, CancellationToken ct = default);
    }
}

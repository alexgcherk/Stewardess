// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Parsers.Abstractions;

/// <summary>
/// Input to a parser adapter for extracting structure from a single file.
/// </summary>
public sealed class ParseRequest
{
    /// <summary>Stable file identifier to use in produced nodes.</summary>
    public required string FileId { get; init; }

    /// <summary>Absolute or repository-relative file path.</summary>
    public required string FilePath { get; init; }

    /// <summary>Decoded text content of the file.</summary>
    public required string Content { get; init; }

    /// <summary>Detected language identifier.</summary>
    public required string LanguageId { get; init; }

    /// <summary>Requested parse depth.</summary>
    public ParseMode Mode { get; init; } = ParseMode.Declarations;

    /// <summary>Maximum file size the adapter should process. <see langword="null"/> means no limit.</summary>
    public long? MaxFileSizeBytes { get; init; }

    /// <summary>Per-file parse timeout. <see langword="null"/> means no timeout beyond the CancellationToken.</summary>
    public TimeSpan? Timeout { get; init; }
}

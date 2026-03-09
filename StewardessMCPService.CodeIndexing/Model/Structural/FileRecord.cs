// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Model.Structural;

/// <summary>
/// Tracks one indexed file revision within a snapshot.
/// </summary>
public sealed class FileRecord
{
    /// <summary>Unique stable identifier for this file within the snapshot.</summary>
    public required string FileId { get; init; }

    /// <summary>Repository-relative path using forward slashes.</summary>
    public required string Path { get; init; }

    /// <summary>Language identifier from <see cref="LanguageDetection.LanguageId"/>.</summary>
    public required string LanguageId { get; init; }

    /// <summary>SHA-256 hex hash of file content at time of indexing.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Monotonically increasing revision counter for this file path.</summary>
    public int Revision { get; init; }

    /// <summary>File size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Detected encoding (e.g., "utf-8", "utf-16-le", "ascii").</summary>
    public string Encoding { get; init; } = "utf-8";

    /// <summary>Eligibility determination result.</summary>
    public EligibilityStatus EligibilityStatus { get; init; }

    /// <summary>Parse outcome.</summary>
    public ParseStatus ParseStatus { get; init; }

    /// <summary>Node IDs of top-level structural nodes declared in this file.</summary>
    public IReadOnlyList<string> TopLevelNodeIds { get; init; } = [];

    /// <summary>Diagnostic IDs associated with this file.</summary>
    public IReadOnlyList<string> DiagnosticIds { get; init; } = [];
}

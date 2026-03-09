// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using StewardessMCPService.CodeIndexing.Model.Snapshots;
using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Indexing;

/// <summary>
/// Result of a completed index build operation.
/// </summary>
public sealed class IndexBuildResult
{
    /// <summary>ID of the snapshot published by this build.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>Repository root that was indexed.</summary>
    public required string RootPath { get; init; }

    /// <summary>UTC time the build started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>UTC time the build completed.</summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>Total elapsed milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>Parse mode used.</summary>
    public ParseMode ParseMode { get; init; }

    /// <summary>Total files discovered under the root path.</summary>
    public int FilesScanned { get; init; }

    /// <summary>Files that passed eligibility checks.</summary>
    public int FilesEligible { get; init; }

    /// <summary>Files successfully parsed (Status = Success or Partial).</summary>
    public int FilesIndexed { get; init; }

    /// <summary>Files skipped (ineligible or unsupported language).</summary>
    public int FilesSkipped { get; init; }

    /// <summary>Files that failed to parse entirely.</summary>
    public int FilesFailed { get; init; }

    /// <summary>Total structural nodes produced.</summary>
    public int StructuralNodeCount { get; init; }

    /// <summary>Total logical symbols projected.</summary>
    public int SymbolCount { get; init; }

    /// <summary>Total symbol occurrences.</summary>
    public int OccurrenceCount { get; init; }

    /// <summary>Total reference edges.</summary>
    public int ReferenceCount { get; init; }

    /// <summary>Total dependency edges.</summary>
    public int DependencyEdgeCount { get; init; }

    /// <summary>Total diagnostics emitted.</summary>
    public int DiagnosticCount { get; init; }

    /// <summary>Per-language file counts.</summary>
    public IReadOnlyDictionary<string, int> LanguageBreakdown { get; init; } =
        new Dictionary<string, int>();

    /// <summary>"success" | "partial" | "failed"</summary>
    public required string Status { get; init; }

    /// <summary>Non-fatal warnings emitted during the build.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

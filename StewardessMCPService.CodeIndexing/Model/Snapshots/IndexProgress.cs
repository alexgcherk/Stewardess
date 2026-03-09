// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
namespace StewardessMCPService.CodeIndexing.Model.Snapshots;

/// <summary>
/// Progress report emitted during an active indexing build or update.
/// </summary>
public sealed class IndexProgress
{
    /// <summary>Total files to process.</summary>
    public int TotalFiles { get; init; }

    /// <summary>Files processed so far.</summary>
    public int ProcessedFiles { get; init; }

    /// <summary>Files that were skipped (ineligible or unknown language).</summary>
    public int SkippedFiles { get; init; }

    /// <summary>Files that failed to parse.</summary>
    public int FailedFiles { get; init; }

    /// <summary>Percentage complete in [0, 100].</summary>
    public int PercentComplete =>
        TotalFiles > 0 ? (int)((ProcessedFiles * 100.0) / TotalFiles) : 0;

    /// <summary>Current file being processed, if available.</summary>
    public string? CurrentFile { get; init; }

    /// <summary>Elapsed milliseconds since the build started.</summary>
    public long ElapsedMs { get; init; }
}

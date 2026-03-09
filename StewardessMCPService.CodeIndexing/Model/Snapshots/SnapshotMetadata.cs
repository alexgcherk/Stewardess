// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.CodeIndexing.Model.Snapshots;

/// <summary>
///     Immutable metadata for a published index snapshot.
///     A snapshot captures a consistent view of the entire indexed repository at a point in time.
/// </summary>
public sealed class SnapshotMetadata
{
    /// <summary>
    ///     Unique snapshot identifier.
    ///     Format: snap-{rootHashPrefix}-{timestamp:yyyyMMddHHmmss}-{revision}.
    /// </summary>
    public required string SnapshotId { get; init; }

    /// <summary>Schema version for this snapshot payload format.</summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>UTC timestamp when this snapshot was published.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Absolute path of the repository root that was indexed.</summary>
    public required string RootPath { get; init; }

    /// <summary>Index mode used to build this snapshot.</summary>
    public IndexMode IndexMode { get; init; }

    /// <summary>Number of files in this snapshot.</summary>
    public int FileCount { get; init; }

    /// <summary>Number of structural nodes across all files.</summary>
    public int StructuralNodeCount { get; init; }

    /// <summary>Number of logical symbols projected.</summary>
    public int SymbolCount { get; init; }

    /// <summary>Number of symbol occurrences.</summary>
    public int OccurrenceCount { get; init; }

    /// <summary>Number of reference edges.</summary>
    public int ReferenceCount { get; init; }

    /// <summary>Number of hard dependency edges.</summary>
    public int DependencyEdgeCount { get; init; }

    /// <summary>Number of import/using/require entries across all files.</summary>
    public int ImportCount { get; init; }

    /// <summary>Number of diagnostics emitted.</summary>
    public int DiagnosticCount { get; init; }

    /// <summary>Per-language file counts. Key is a <see cref="LanguageDetection.LanguageId" /> constant.</summary>
    public IReadOnlyDictionary<string, int> LanguageBreakdown { get; init; } =
        new Dictionary<string, int>();

    /// <summary>Version string for each parser adapter that contributed to this snapshot.</summary>
    public IReadOnlyDictionary<string, string> AdapterVersions { get; init; } =
        new Dictionary<string, string>();
}
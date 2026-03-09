// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Model.References;

/// <summary>
///     Represents an import, using, require, or include directive extracted from a file.
/// </summary>
public sealed class ImportEntry
{
    /// <summary>Import kind (e.g., "using", "import", "require", "#include").</summary>
    public required string Kind { get; init; }

    /// <summary>Raw import text as written in source.</summary>
    public required string RawText { get; init; }

    /// <summary>Normalized target path or module name.</summary>
    public string? NormalizedTarget { get; init; }

    /// <summary>Alias introduced by this import, if any (e.g., "using Alias = Namespace.Type").</summary>
    public string? Alias { get; init; }

    /// <summary>Source location of the import directive.</summary>
    public SourceSpan? SourceSpan { get; init; }

    /// <summary>How confidently this import was resolved.</summary>
    public ResolutionClass ResolutionClass { get; init; } = ResolutionClass.Unknown;

    /// <summary>Resolved symbol ID if this import maps to a repository-defined symbol.</summary>
    public string? ResolvedSymbolId { get; init; }
}
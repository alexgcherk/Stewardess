// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.CodeIndexing.Model.Structural;

/// <summary>
///     Universal node for all supported source and document structures.
///     Forms a language-neutral containment hierarchy within a file.
/// </summary>
public sealed class StructuralNode
{
    /// <summary>Unique node identifier within the snapshot.</summary>
    public required string NodeId { get; init; }

    /// <summary>File record ID this node belongs to.</summary>
    public required string FileId { get; init; }

    /// <summary>Parent node ID, or <see langword="null" /> for top-level nodes.</summary>
    public string? ParentNodeId { get; init; }

    /// <summary>Broad structural category.</summary>
    public NodeKind Kind { get; init; }

    /// <summary>Language-specific sub-category (e.g., "class", "interface", "function").</summary>
    public string? Subkind { get; init; }

    /// <summary>Language this node was extracted from.</summary>
    public required string LanguageId { get; init; }

    /// <summary>Simple name of the declaration (e.g., "MyClass", "DoWork").</summary>
    public required string Name { get; init; }

    /// <summary>Display-friendly name that may include signature hints.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Dotted/qualified path relative to file root (e.g., "MyNamespace.MyClass.DoWork").</summary>
    public string? QualifiedPath { get; init; }

    /// <summary>Source location of this node within its file.</summary>
    public required SourceSpan SourceSpan { get; init; }

    /// <summary>Language modifiers present (e.g., "public", "static", "async", "abstract").</summary>
    public IReadOnlyList<string> Modifiers { get; init; } = [];

    /// <summary>Accessibility/visibility keyword (e.g., "public", "private", "protected").</summary>
    public string? Visibility { get; init; }

    /// <summary>How this node was extracted.</summary>
    public ExtractionMode ExtractionMode { get; init; }

    /// <summary>Extraction confidence in [0.0, 1.0]. Heuristic extractions use lower values.</summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>Node IDs of direct child structural nodes.</summary>
    public IReadOnlyList<string> Children { get; init; } = [];
}
// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using StewardessMCPService.CodeIndexing.Model.Semantic;
using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Projection;

/// <summary>
///     Projects C# structural nodes into logical symbols and occurrences.
/// </summary>
/// <remarks>
///     Handles the following C# constructs:
///     <list type="bullet">
///         <item>Namespaces → <see cref="SymbolKind.Namespace" /></item>
///         <item>Classes, structs, interfaces, enums, records, delegates → type symbols</item>
///         <item>Methods, constructors → callable symbols</item>
///         <item>Properties, fields, events, indexers → member symbols</item>
///     </list>
///     One <see cref="SymbolOccurrence" /> with role <see cref="OccurrenceRole.Declaration" />
///     is produced for each projected symbol.
/// </remarks>
public sealed class CSharpSymbolProjector : ISymbolProjector
{
    /// <inheritdoc />
    public string LanguageId => LanguageDetection.LanguageId.CSharp;

    /// <inheritdoc />
    public SymbolProjectionResult Project(
        string fileId,
        string repoScope,
        IReadOnlyDictionary<string, StructuralNode> fileNodes)
    {
        var symbols = new List<LogicalSymbol>();
        var occurrences = new List<SymbolOccurrence>();
        // Tracks NodeId → symbolId so child nodes can resolve their parent symbol
        var nodeToSymbolId = new Dictionary<string, string>();

        foreach (var node in TopologicalOrder(fileNodes))
        {
            var symbolKind = MapToSymbolKind(node);
            if (symbolKind is null) continue;

            var kind = symbolKind.Value;
            var qualifiedName = node.QualifiedPath ?? node.Name;
            var kindCategory = SymbolIdBuilder.GetKindCategory(kind);
            var symbolId = SymbolIdBuilder.BuildSymbolId(LanguageId, repoScope, kindCategory, qualifiedName);
            var symbolKey = SymbolIdBuilder.BuildSymbolKey(LanguageId, repoScope, kindCategory, qualifiedName);

            string? parentSymbolId = null;
            if (node.ParentNodeId is not null &&
                nodeToSymbolId.TryGetValue(node.ParentNodeId, out var pid))
                parentSymbolId = pid;

            nodeToSymbolId[node.NodeId] = symbolId;

            var parts = qualifiedName.Split('.');
            IReadOnlyList<string> containerPath = parts.Length > 1
                ? parts[..^1]
                : [];

            var genericParams = ExtractGenericParameters(node.DisplayName);
            var modifiers = node.Modifiers
                .Where(m => m is not ("public" or "private" or "protected" or "internal"
                    or "protected internal" or "private protected"))
                .ToList();

            var capFlags = CapabilityFlags.HasDeclarations;
            if (node.Children.Count > 0) capFlags |= CapabilityFlags.HasMembers;

            var occurrenceId = OccurrenceIdBuilder.BuildOccurrenceId(
                symbolId, fileId, node.SourceSpan, OccurrenceRole.Declaration);

            symbols.Add(new LogicalSymbol
            {
                SymbolId = symbolId,
                SymbolKey = symbolKey,
                Name = node.Name,
                QualifiedName = qualifiedName,
                DisplayName = node.DisplayName,
                Kind = kind,
                Subkind = node.Subkind,
                LanguageId = LanguageId,
                PrimaryFileId = fileId,
                PrimaryOccurrenceId = occurrenceId,
                ParentSymbolId = parentSymbolId,
                ContainerPath = containerPath,
                Visibility = node.Visibility,
                Modifiers = modifiers,
                GenericParameters = genericParams,
                SignatureDisplay = node.DisplayName,
                ExtractionMode = node.ExtractionMode,
                Confidence = node.Confidence,
                CapabilityFlags = capFlags
            });

            occurrences.Add(new SymbolOccurrence
            {
                OccurrenceId = occurrenceId,
                SymbolId = symbolId,
                FileId = fileId,
                Role = OccurrenceRole.Declaration,
                SourceSpan = node.SourceSpan,
                IsPrimary = true,
                ExtractionMode = node.ExtractionMode,
                Confidence = node.Confidence
            });
        }

        return new SymbolProjectionResult { Symbols = symbols, Occurrences = occurrences };
    }

    /// <summary>
    ///     Maps a structural node to the appropriate <see cref="SymbolKind" />,
    ///     or returns <see langword="null" /> for nodes that should not be projected.
    /// </summary>
    private static SymbolKind? MapToSymbolKind(StructuralNode node)
    {
        return node.Kind switch
        {
            NodeKind.Container when node.Subkind == "namespace" => SymbolKind.Namespace,
            NodeKind.Declaration => node.Subkind switch
            {
                "class" => SymbolKind.Class,
                "struct" => SymbolKind.Struct,
                "interface" => SymbolKind.Interface,
                "enum" => SymbolKind.Enum,
                "record" or "record class" => SymbolKind.Record,
                "record struct" => SymbolKind.Record,
                "delegate" => SymbolKind.TypeAlias,
                _ => null
            },
            NodeKind.Callable => node.Subkind switch
            {
                "method" => SymbolKind.Method,
                "constructor" => SymbolKind.Constructor,
                _ => null
            },
            NodeKind.Member => node.Subkind switch
            {
                "property" or "indexer" => SymbolKind.Property,
                "field" => SymbolKind.Field,
                "event" => SymbolKind.Event,
                _ => null
            },
            _ => null
        };
    }

    /// <summary>
    ///     Extracts generic type parameter names from a display name (e.g., "List&lt;T&gt;" → ["T"]).
    /// </summary>
    private static IReadOnlyList<string> ExtractGenericParameters(string displayName)
    {
        var start = displayName.IndexOf('<');
        var end = displayName.LastIndexOf('>');
        if (start < 0 || end < 0 || end <= start) return [];
        var inner = displayName[(start + 1)..end];
        return inner.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }

    /// <summary>
    ///     Orders nodes breadth-first from root to leaves, ensuring parents are
    ///     visited before their children so <c>nodeToSymbolId</c> lookups succeed.
    /// </summary>
    private static IReadOnlyList<StructuralNode> TopologicalOrder(
        IReadOnlyDictionary<string, StructuralNode> nodes)
    {
        var result = new List<StructuralNode>(nodes.Count);
        var queue = new Queue<StructuralNode>(
            nodes.Values.Where(n => n.ParentNodeId is null));

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);
            foreach (var childId in node.Children)
                if (nodes.TryGetValue(childId, out var child))
                    queue.Enqueue(child);
        }

        return result;
    }
}
using StewardessMCPService.CodeIndexing.Model.Semantic;
using StewardessMCPService.CodeIndexing.Model.Structural;

namespace StewardessMCPService.CodeIndexing.Projection;

/// <summary>
/// Projects Python structural nodes into logical symbols and occurrences.
/// </summary>
/// <remarks>
/// Handles the following Python constructs:
/// <list type="bullet">
///   <item>Module containers → <see cref="SymbolKind.Module"/></item>
///   <item>Classes → <see cref="SymbolKind.Class"/></item>
///   <item>Top-level functions and async functions → <see cref="SymbolKind.Function"/></item>
///   <item>Methods and async methods inside a class → <see cref="SymbolKind.Method"/></item>
/// </list>
/// One <see cref="SymbolOccurrence"/> with role <see cref="OccurrenceRole.Declaration"/>
/// is produced for each projected symbol.
/// </remarks>
public sealed class PythonSymbolProjector : ISymbolProjector
{
    /// <inheritdoc/>
    public string LanguageId => LanguageDetection.LanguageId.Python;

    /// <inheritdoc/>
    public SymbolProjectionResult Project(
        string fileId,
        string repoScope,
        IReadOnlyDictionary<string, StructuralNode> fileNodes)
    {
        var symbols = new List<LogicalSymbol>();
        var occurrences = new List<SymbolOccurrence>();
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
                Modifiers = [],
                ExtractionMode = node.ExtractionMode,
                Confidence = node.Confidence,
                CapabilityFlags = capFlags,
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
                Confidence = node.Confidence,
            });
        }

        return new SymbolProjectionResult { Symbols = symbols, Occurrences = occurrences };
    }

    /// <summary>
    /// Maps a Python structural node to its <see cref="SymbolKind"/>,
    /// or returns <see langword="null"/> for nodes that should not be projected.
    /// </summary>
    private static SymbolKind? MapToSymbolKind(StructuralNode node) => node.Kind switch
    {
        NodeKind.Container when node.Subkind == "module" => SymbolKind.Module,
        NodeKind.Declaration when node.Subkind == "class" => SymbolKind.Class,
        NodeKind.Callable => node.Subkind switch
        {
            "function" or "async function" => SymbolKind.Function,
            "method" or "async method" => SymbolKind.Method,
            _ => null,
        },
        _ => null,
    };

    /// <summary>
    /// Orders nodes breadth-first from root to leaves so parents are visited
    /// before children, enabling parent symbol ID resolution.
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
            {
                if (nodes.TryGetValue(childId, out var child))
                    queue.Enqueue(child);
            }
        }

        return result;
    }
}

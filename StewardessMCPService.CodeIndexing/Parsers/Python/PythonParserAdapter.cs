using System.Text.RegularExpressions;
using StewardessMCPService.CodeIndexing.LanguageDetection;
using StewardessMCPService.CodeIndexing.Model.Diagnostics;
using StewardessMCPService.CodeIndexing.Model.References;
using StewardessMCPService.CodeIndexing.Model.Structural;
using StewardessMCPService.CodeIndexing.Parsers.Abstractions;

namespace StewardessMCPService.CodeIndexing.Parsers.Python;

/// <summary>
/// Heuristic parser adapter for Python files.
/// Uses regex-based pattern matching to extract classes, functions, and methods.
/// Extraction mode: <see cref="ExtractionMode.Heuristic"/> with reduced confidence.
/// </summary>
/// <remarks>
/// This adapter does NOT execute Python code. It relies on indentation and keyword
/// patterns to identify declarations.
/// </remarks>
public sealed class PythonParserAdapter : IParserAdapter
{
    private const string Version = "1.0.0";

    // Matches: [optional_indent]class ClassName[(BaseClasses)]:
    private static readonly Regex _classPattern = new(
        @"^(?<indent>[ \t]*)class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s*\((?<bases>[^)]*)\))?\s*:",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Matches: [optional_indent][async ]def name(params) [-> return_type]:
    private static readonly Regex _defPattern = new(
        @"^(?<indent>[ \t]*)(?<async>async\s+)?def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<params>[^)]*)\)(?:\s*->\s*(?<ret>[^:]+))?\s*:",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Matches: import module or import module as alias
    private static readonly Regex _importPattern = new(
        @"^(?<indent>[ \t]*)import\s+(?<module>[A-Za-z_][A-Za-z0-9_.]*?)(?:\s+as\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*))?\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Matches: from module import name [as alias][, ...]
    private static readonly Regex _fromImportPattern = new(
        @"^(?<indent>[ \t]*)from\s+(?<module>\.+[A-Za-z0-9_.]*|[A-Za-z_][A-Za-z0-9_.]*)?\s+import\s+(?<names>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <inheritdoc/>
    public string LanguageId => LanguageDetection.LanguageId.Python;

    /// <inheritdoc/>
    public AdapterCapabilities Capabilities { get; } = new()
    {
        LanguageId = LanguageDetection.LanguageId.Python,
        AdapterVersion = Version,
        SupportsOutline = true,
        SupportsDeclarations = true,
        SupportsLogicalSymbols = false,
        SupportsOccurrences = false,
        SupportsImportsOrUses = true,
        SupportsTypeExtraction = true,
        SupportsCallableExtraction = true,
        SupportsMemberExtraction = false,
        SupportsReferenceExtraction = true,
        SupportsRepositoryResolution = false,
        SupportsCrossFileResolution = false,
        SupportsHeuristicFallback = true,
        SupportsSyntaxErrorRecovery = true,
        SupportsDocumentTreeOnly = false,
        GuaranteeNotes = "Heuristic regex-based extraction. Reduced confidence. " +
                         "Classes, functions, and methods are extracted by indentation and keyword patterns.",
    };

    /// <inheritdoc/>
    public Task<ParseResult> ParseAsync(ParseRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var lines = request.Content.Split('\n');
            var nodes = ExtractNodes(request.FileId, lines, ct);
            var imports = ExtractImports(lines);
            var referenceHints = ExtractReferenceHints(nodes, lines);

            return Task.FromResult(new ParseResult
            {
                FileId = request.FileId,
                Status = ParseStatus.Success,
                Nodes = nodes,
                Imports = imports,
                ReferenceHints = referenceHints,
                ExtractionMode = ExtractionMode.Heuristic,
                AdapterVersion = Version,
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ParseResult
            {
                FileId = request.FileId,
                Status = ParseStatus.Failed,
                Diagnostics =
                [
                    new IndexDiagnostic
                    {
                        DiagnosticId = $"diag-{request.FileId}-fatal",
                        Severity = DiagnosticSeverity.Error,
                        Source = DiagnosticSource.ParserAdapter,
                        Code = "PARSE_EXCEPTION",
                        Message = $"Python heuristic parser threw: {ex.Message}",
                        FilePath = request.FilePath,
                    }
                ],
                ExtractionMode = ExtractionMode.Heuristic,
                AdapterVersion = Version,
            });
        }
    }

    private static List<StructuralNode> ExtractNodes(
        string fileId, string[] lines, CancellationToken ct)
    {
        var nodes = new List<StructuralNode>();
        int counter = 0;

        // Stack of (indentLevel, nodeId, qualifiedPath)
        var parentStack = new Stack<(int Indent, string NodeId, string QualPath)>();

        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;

            int indent = MeasureIndent(line);

            // Pop stack back to parent level
            while (parentStack.Count > 0 && parentStack.Peek().Indent >= indent)
                parentStack.Pop();

            string? parentNodeId = parentStack.Count > 0 ? parentStack.Peek().NodeId : null;
            string parentPath = parentStack.Count > 0 ? parentStack.Peek().QualPath : "";

            // Class detection
            var classMatch = _classPattern.Match(line);
            if (classMatch.Success && MeasureIndent(classMatch.Value) == indent)
            {
                var name = classMatch.Groups["name"].Value;
                var nodeId = $"cls-{fileId}-{++counter}";
                var qualPath = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}.{name}";
                int endLine = EstimateBlockEnd(lines, i, indent);

                nodes.Add(BuildNode(nodeId, fileId, parentNodeId, NodeKind.Declaration,
                    "class", name, name, qualPath,
                    SourceSpan.FromLines(i + 1, endLine), [], 0.75));

                parentStack.Push((indent, nodeId, qualPath));
                continue;
            }

            // Function / method detection
            var defMatch = _defPattern.Match(line);
            if (defMatch.Success && MeasureIndent(defMatch.Value) == indent)
            {
                var name = defMatch.Groups["name"].Value;
                var isAsync = defMatch.Groups["async"].Success;
                var retType = defMatch.Groups["ret"].Value.Trim();
                var paramSig = defMatch.Groups["params"].Value.Trim();

                var subkind = parentNodeId != null ? "method" : "function";
                var nodeId = $"def-{fileId}-{++counter}";
                var qualPath = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}.{name}";
                var displayName = $"{(isAsync ? "async " : "")}{name}({paramSig}){(string.IsNullOrEmpty(retType) ? "" : $" -> {retType}")}";
                int endLine = EstimateBlockEnd(lines, i, indent);
                List<string> mods = isAsync ? ["async"] : [];

                nodes.Add(BuildNode(nodeId, fileId, parentNodeId, NodeKind.Callable,
                    subkind, name, displayName, qualPath,
                    SourceSpan.FromLines(i + 1, endLine), mods, 0.80));

                parentStack.Push((indent, nodeId, qualPath));
                continue;
            }
        }

        BuildChildrenLists(nodes);
        return nodes;
    }

    private static StructuralNode BuildNode(
        string nodeId, string fileId, string? parentNodeId,
        NodeKind kind, string subkind, string name, string displayName,
        string qualPath, SourceSpan span, List<string> modifiers, double confidence) =>
        new()
        {
            NodeId = nodeId,
            FileId = fileId,
            ParentNodeId = parentNodeId,
            Kind = kind,
            Subkind = subkind,
            LanguageId = LanguageDetection.LanguageId.Python,
            Name = name,
            DisplayName = displayName,
            QualifiedPath = qualPath,
            SourceSpan = span,
            Modifiers = modifiers,
            ExtractionMode = ExtractionMode.Heuristic,
            Confidence = confidence,
            Children = [],
        };

    private static void BuildChildrenLists(List<StructuralNode> nodes)
    {
        var childMap = new Dictionary<string, List<string>>();
        foreach (var node in nodes)
        {
            if (node.ParentNodeId is not null)
            {
                if (!childMap.TryGetValue(node.ParentNodeId, out var list))
                    childMap[node.ParentNodeId] = list = [];
                list.Add(node.NodeId);
            }
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (childMap.TryGetValue(node.NodeId, out var children))
            {
                nodes[i] = new StructuralNode
                {
                    NodeId = node.NodeId, FileId = node.FileId,
                    ParentNodeId = node.ParentNodeId, Kind = node.Kind, Subkind = node.Subkind,
                    LanguageId = node.LanguageId, Name = node.Name, DisplayName = node.DisplayName,
                    QualifiedPath = node.QualifiedPath, SourceSpan = node.SourceSpan,
                    Modifiers = node.Modifiers, Visibility = node.Visibility,
                    ExtractionMode = node.ExtractionMode, Confidence = node.Confidence,
                    Children = children,
                };
            }
        }
    }

    private static int MeasureIndent(string line)
    {
        int count = 0;
        foreach (var ch in line)
        {
            if (ch == ' ') count++;
            else if (ch == '\t') count += 4;
            else break;
        }
        return count;
    }

    private static int EstimateBlockEnd(string[] lines, int startLine, int blockIndent)
    {
        for (int i = startLine + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (MeasureIndent(line) <= blockIndent)
                return i;
        }
        return lines.Length;
    }

    private static IReadOnlyList<ImportEntry> ExtractImports(string[] lines)
    {
        var imports = new List<ImportEntry>();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var importMatch = _importPattern.Match(line);
            if (importMatch.Success)
            {
                imports.Add(new ImportEntry
                {
                    Kind = "import",
                    RawText = line.Trim(),
                    NormalizedTarget = importMatch.Groups["module"].Value,
                    Alias = importMatch.Groups["alias"].Success ? importMatch.Groups["alias"].Value : null,
                    SourceSpan = SourceSpan.FromLines(i + 1, i + 1),
                });
                continue;
            }

            var fromMatch = _fromImportPattern.Match(line);
            if (fromMatch.Success)
            {
                var module = fromMatch.Groups["module"].Value;
                var isRelative = module.StartsWith('.');
                imports.Add(new ImportEntry
                {
                    Kind = isRelative ? "relative-import" : "from-import",
                    RawText = line.Trim(),
                    NormalizedTarget = module.TrimStart('.'),
                    Alias = fromMatch.Groups["names"].Value.Trim(),
                    SourceSpan = SourceSpan.FromLines(i + 1, i + 1),
                });
            }
        }
        return imports;
    }

    private static IReadOnlyList<ReferenceHint> ExtractReferenceHints(
        IReadOnlyList<StructuralNode> nodes, string[] lines)
    {
        var hints = new List<ReferenceHint>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var classMatch = _classPattern.Match(line);
            if (classMatch.Success)
            {
                var className = classMatch.Groups["name"].Value;
                var basesStr = classMatch.Groups["bases"].Value.Trim();
                if (string.IsNullOrEmpty(basesStr)) continue;

                // Find the qualified path for this class node
                var classNode = nodes.FirstOrDefault(n =>
                    n.Name == className &&
                    n.SourceSpan != null &&
                    n.SourceSpan.StartLine == i + 1);
                var qualPath = classNode?.QualifiedPath ?? className;

                foreach (var baseName in basesStr.Split(',').Select(b => b.Trim()).Where(b => b.Length > 0))
                {
                    // Strip generic parameters if any (e.g., Base[T] -> Base)
                    var simpleName = baseName.Split('[')[0].Trim();
                    if (string.IsNullOrEmpty(simpleName)) continue;

                    hints.Add(new ReferenceHint
                    {
                        SourceQualifiedPath = qualPath,
                        Kind = IsLikelyInterface(simpleName) ? RelationshipKind.Implements : RelationshipKind.Inherits,
                        TargetName = simpleName,
                        Evidence = baseName,
                        EvidenceSpan = SourceSpan.FromLines(i + 1, i + 1),
                    });
                }
            }
        }

        return hints;
    }

    private static bool IsLikelyInterface(string name) =>
        name.StartsWith("I", StringComparison.Ordinal) && name.Length > 1 && char.IsUpper(name[1]);
}

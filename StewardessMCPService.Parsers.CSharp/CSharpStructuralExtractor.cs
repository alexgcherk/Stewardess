// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using StewardessMCPService.CodeIndexing.Model.Structural;
using LangId = StewardessMCPService.CodeIndexing.LanguageDetection.LanguageId;

namespace StewardessMCPService.Parsers.CSharp;

/// <summary>
///     Walks a Roslyn C# SyntaxTree and produces a flat list of <see cref="StructuralNode" />
///     objects representing the file's declaration hierarchy.
/// </summary>
internal sealed class CSharpStructuralExtractor
{
    private readonly CancellationToken _ct;
    private readonly string _fileId;
    private readonly string _filePath;
    private readonly Dictionary<SyntaxNode, string> _nodeIds = [];
    private readonly List<StructuralNode> _nodes = [];
    private int _counter;

    internal CSharpStructuralExtractor(string fileId, string filePath, CancellationToken ct)
    {
        _fileId = fileId;
        _filePath = filePath;
        _ct = ct;
    }

    /// <summary>
    ///     Extracts structural nodes from the syntax root.
    ///     Returns a flat list; parent-child relationships are encoded via <see cref="StructuralNode.Children" />.
    /// </summary>
    internal IReadOnlyList<StructuralNode> Extract(SyntaxNode root)
    {
        Visit(root, null, []);
        return _nodes;
    }

    private void Visit(SyntaxNode node, string? parentNodeId, IReadOnlyList<string> containerPath)
    {
        _ct.ThrowIfCancellationRequested();

        switch (node)
        {
            case CompilationUnitSyntax cu:
                VisitChildren(cu, parentNodeId, containerPath);
                break;

            case FileScopedNamespaceDeclarationSyntax fsns:
                VisitNamespace(fsns, fsns.Name.ToString(), parentNodeId, containerPath);
                break;

            case NamespaceDeclarationSyntax ns:
                VisitNamespace(ns, ns.Name.ToString(), parentNodeId, containerPath);
                break;

            case ClassDeclarationSyntax cls:
                VisitTypeDeclaration(cls, "class", cls.Identifier.Text,
                    cls.Modifiers, cls.TypeParameterList, parentNodeId, containerPath);
                break;

            case StructDeclarationSyntax str:
                VisitTypeDeclaration(str, "struct", str.Identifier.Text,
                    str.Modifiers, str.TypeParameterList, parentNodeId, containerPath);
                break;

            case InterfaceDeclarationSyntax iface:
                VisitTypeDeclaration(iface, "interface", iface.Identifier.Text,
                    iface.Modifiers, iface.TypeParameterList, parentNodeId, containerPath);
                break;

            case EnumDeclarationSyntax enm:
                VisitTypeDeclaration(enm, "enum", enm.Identifier.Text,
                    enm.Modifiers, null, parentNodeId, containerPath);
                break;

            case RecordDeclarationSyntax rec:
                var recordSubkind = rec.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)
                    ? "record struct"
                    : "record class";
                VisitTypeDeclaration(rec, recordSubkind, rec.Identifier.Text,
                    rec.Modifiers, rec.TypeParameterList, parentNodeId, containerPath);
                break;

            case DelegateDeclarationSyntax del:
                VisitTypeDeclaration(del, "delegate", del.Identifier.Text,
                    del.Modifiers, del.TypeParameterList, parentNodeId, containerPath);
                break;

            case MethodDeclarationSyntax method:
                VisitCallable(method, "method", method.Identifier.Text,
                    method.Modifiers, method.TypeParameterList,
                    method.ReturnType.ToString(), method.ParameterList,
                    parentNodeId, containerPath);
                break;

            case ConstructorDeclarationSyntax ctor:
                VisitCallable(ctor, "constructor", ctor.Identifier.Text,
                    ctor.Modifiers, null,
                    null, ctor.ParameterList,
                    parentNodeId, containerPath);
                break;

            case PropertyDeclarationSyntax prop:
                VisitMember(prop, "property", prop.Identifier.Text,
                    prop.Modifiers, prop.Type.ToString(), parentNodeId, containerPath);
                break;

            case FieldDeclarationSyntax field:
                foreach (var variable in field.Declaration.Variables)
                    VisitMember(field, "field", variable.Identifier.Text,
                        field.Modifiers, field.Declaration.Type.ToString(),
                        parentNodeId, containerPath);
                break;

            case EventDeclarationSyntax evt:
                VisitMember(evt, "event", evt.Identifier.Text,
                    evt.Modifiers, evt.Type.ToString(), parentNodeId, containerPath);
                break;

            case EventFieldDeclarationSyntax efld:
                foreach (var variable in efld.Declaration.Variables)
                    VisitMember(efld, "event", variable.Identifier.Text,
                        efld.Modifiers, efld.Declaration.Type.ToString(),
                        parentNodeId, containerPath);
                break;

            case IndexerDeclarationSyntax idx:
                VisitMember(idx, "indexer", "this[]",
                    idx.Modifiers, idx.Type.ToString(), parentNodeId, containerPath);
                break;

            default:
                VisitChildren(node, parentNodeId, containerPath);
                break;
        }
    }

    private void VisitNamespace(SyntaxNode node, string name, string? parentNodeId,
        IReadOnlyList<string> containerPath)
    {
        var nodeId = NextId("ns");
        var span = GetSpan(node);
        var children = new List<string>();

        IReadOnlyList<string> newPath = [.. containerPath, name];

        _nodes.Add(new StructuralNode
        {
            NodeId = nodeId,
            FileId = _fileId,
            ParentNodeId = parentNodeId,
            Kind = NodeKind.Container,
            Subkind = "namespace",
            LanguageId = LangId.CSharp,
            Name = name,
            DisplayName = name,
            QualifiedPath = string.Join(".", newPath),
            SourceSpan = span,
            Modifiers = [],
            ExtractionMode = ExtractionMode.CompilerSyntax,
            Confidence = 1.0,
            Children = children
        });

        VisitChildrenInto(node, nodeId, newPath, children);
    }

    private void VisitTypeDeclaration(SyntaxNode node, string subkind, string name,
        SyntaxTokenList modifiers, TypeParameterListSyntax? typeParams,
        string? parentNodeId, IReadOnlyList<string> containerPath)
    {
        var nodeId = NextId("decl");
        var span = GetSpan(node);
        var children = new List<string>();

        var displayName = typeParams != null && typeParams.Parameters.Count > 0
            ? $"{name}<{string.Join(", ", typeParams.Parameters.Select(p => p.Identifier.Text))}>"
            : name;

        IReadOnlyList<string> newPath = [.. containerPath, name];

        _nodes.Add(new StructuralNode
        {
            NodeId = nodeId,
            FileId = _fileId,
            ParentNodeId = parentNodeId,
            Kind = NodeKind.Declaration,
            Subkind = subkind,
            LanguageId = LangId.CSharp,
            Name = name,
            DisplayName = displayName,
            QualifiedPath = string.Join(".", newPath),
            SourceSpan = span,
            Modifiers = modifiers.Select(m => m.Text).ToList(),
            Visibility = GetVisibility(modifiers),
            ExtractionMode = ExtractionMode.CompilerSyntax,
            Confidence = 1.0,
            Children = children
        });

        VisitChildrenInto(node, nodeId, newPath, children);
    }

    private void VisitCallable(SyntaxNode node, string subkind, string name,
        SyntaxTokenList modifiers, TypeParameterListSyntax? typeParams,
        string? returnType, ParameterListSyntax? paramList,
        string? parentNodeId, IReadOnlyList<string> containerPath)
    {
        var nodeId = NextId("callable");
        var span = GetSpan(node);

        var paramSig = paramList != null
            ? $"({string.Join(", ", paramList.Parameters.Select(p => p.Type?.ToString() ?? ""))})"
            : "()";

        var genericSig = typeParams != null && typeParams.Parameters.Count > 0
            ? $"<{string.Join(", ", typeParams.Parameters.Select(p => p.Identifier.Text))}>"
            : "";

        var returnPrefix = returnType != null ? $"{returnType} " : "";
        var displayName = $"{returnPrefix}{name}{genericSig}{paramSig}";

        _nodes.Add(new StructuralNode
        {
            NodeId = nodeId,
            FileId = _fileId,
            ParentNodeId = parentNodeId,
            Kind = NodeKind.Callable,
            Subkind = subkind,
            LanguageId = LangId.CSharp,
            Name = name,
            DisplayName = displayName,
            QualifiedPath = string.Join(".", [.. containerPath, name]),
            SourceSpan = span,
            Modifiers = modifiers.Select(m => m.Text).ToList(),
            Visibility = GetVisibility(modifiers),
            ExtractionMode = ExtractionMode.CompilerSyntax,
            Confidence = 1.0,
            Children = []
        });
    }

    private void VisitMember(SyntaxNode node, string subkind, string name,
        SyntaxTokenList modifiers, string typeDisplay,
        string? parentNodeId, IReadOnlyList<string> containerPath)
    {
        var nodeId = NextId("member");
        var span = GetSpan(node);

        _nodes.Add(new StructuralNode
        {
            NodeId = nodeId,
            FileId = _fileId,
            ParentNodeId = parentNodeId,
            Kind = NodeKind.Member,
            Subkind = subkind,
            LanguageId = LangId.CSharp,
            Name = name,
            DisplayName = $"{typeDisplay} {name}",
            QualifiedPath = string.Join(".", [.. containerPath, name]),
            SourceSpan = span,
            Modifiers = modifiers.Select(m => m.Text).ToList(),
            Visibility = GetVisibility(modifiers),
            ExtractionMode = ExtractionMode.CompilerSyntax,
            Confidence = 1.0,
            Children = []
        });
    }

    private void VisitChildren(SyntaxNode parent, string? parentNodeId, IReadOnlyList<string> containerPath)
    {
        foreach (var child in parent.ChildNodes())
            Visit(child, parentNodeId, containerPath);
    }

    private void VisitChildrenInto(SyntaxNode parent, string parentNodeId,
        IReadOnlyList<string> containerPath, List<string> childIds)
    {
        foreach (var child in parent.ChildNodes())
        {
            var countBefore = _nodes.Count;
            Visit(child, parentNodeId, containerPath);
            // Any nodes added since our count are direct children
            for (var i = countBefore; i < _nodes.Count; i++)
                if (_nodes[i].ParentNodeId == parentNodeId)
                    childIds.Add(_nodes[i].NodeId);
        }
    }

    private string NextId(string prefix)
    {
        return $"{prefix}-{_fileId}-{++_counter}";
    }

    private static SourceSpan GetSpan(SyntaxNode node)
    {
        var location = node.GetLocation();
        var lineSpan = location.GetLineSpan();
        return new SourceSpan
        {
            StartLine = lineSpan.StartLinePosition.Line + 1,
            StartColumn = lineSpan.StartLinePosition.Character + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            EndColumn = lineSpan.EndLinePosition.Character + 1,
            StartOffset = node.SpanStart,
            EndOffset = node.Span.End
        };
    }

    private static string? GetVisibility(SyntaxTokenList modifiers)
    {
        foreach (var mod in modifiers)
            switch (mod.Kind())
            {
                case SyntaxKind.PublicKeyword: return "public";
                case SyntaxKind.PrivateKeyword: return "private";
                case SyntaxKind.ProtectedKeyword: return "protected";
                case SyntaxKind.InternalKeyword: return "internal";
            }

        return null;
    }
}
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using StewardessMCPService.CodeIndexing.Model.References;
using StewardessMCPService.CodeIndexing.Model.Structural;
using StewardessMCPService.CodeIndexing.Parsers.Abstractions;

namespace StewardessMCPService.Parsers.CSharp;

/// <summary>
/// Extracts reference hints from a C# syntax tree using Roslyn (CompilerSyntax mode).
/// Produces unresolved hints that the indexing engine resolves post-projection.
/// </summary>
internal static class CSharpReferenceHintExtractor
{
    /// <summary>
    /// Walks the Roslyn syntax root and returns a flat list of reference hints.
    /// </summary>
    internal static IReadOnlyList<ReferenceHint> ExtractHints(SyntaxNode root, CancellationToken ct = default)
    {
        var hints = new List<ReferenceHint>();

        // Base types and interfaces
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            ExtractBaseTypeHints(typeDecl, hints);
        }

        // Field types
        foreach (var fieldDecl in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            ExtractFieldTypeHints(fieldDecl, hints);
        }

        // Property types
        foreach (var propDecl in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            ExtractPropertyTypeHints(propDecl, hints);
        }

        // Method return types and parameters
        foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            ExtractMethodHints(methodDecl, hints);
        }

        // Constructor parameter types
        foreach (var ctorDecl in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            ExtractConstructorHints(ctorDecl, hints);
        }

        // Object creation expressions
        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            ExtractObjectCreationHints(creation, hints);
        }

        // Attribute uses
        foreach (var attr in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            ExtractAttributeHints(attr, hints);
        }

        return hints;
    }

    // ── Base types and interfaces ─────────────────────────────────────────────

    private static void ExtractBaseTypeHints(TypeDeclarationSyntax typeDecl, List<ReferenceHint> hints)
    {
        if (typeDecl.BaseList == null) return;
        var typePath = ComputeTypePath(typeDecl);

        foreach (var baseType in typeDecl.BaseList.Types)
        {
            var typeName = GetSimpleTypeName(baseType.Type);
            if (typeName == null) continue;

            // Without semantic model: use naming convention heuristic.
            // Structs can only implement interfaces; interfaces extend interfaces.
            // For classes: names starting with I+uppercase are likely interfaces.
            var kind = typeDecl switch
            {
                InterfaceDeclarationSyntax => RelationshipKind.Implements,
                StructDeclarationSyntax => RelationshipKind.Implements,
                _ => IsLikelyInterface(typeName) ? RelationshipKind.Implements : RelationshipKind.Inherits,
            };

            hints.Add(new ReferenceHint
            {
                SourceQualifiedPath = typePath,
                Kind = kind,
                TargetName = typeName,
                Evidence = baseType.Type.ToString(),
                EvidenceSpan = ToSourceSpan(baseType.Type.GetLocation().GetLineSpan()),
            });
        }
    }

    // ── Field types ───────────────────────────────────────────────────────────

    private static void ExtractFieldTypeHints(FieldDeclarationSyntax fieldDecl, List<ReferenceHint> hints)
    {
        var typeName = GetSimpleTypeName(fieldDecl.Declaration.Type);
        if (typeName == null) return;

        var containingType = fieldDecl.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (containingType == null) return;

        hints.Add(new ReferenceHint
        {
            SourceQualifiedPath = ComputeTypePath(containingType),
            Kind = RelationshipKind.ContainsFieldOfType,
            TargetName = typeName,
            Evidence = fieldDecl.Declaration.Type.ToString(),
            EvidenceSpan = ToSourceSpan(fieldDecl.Declaration.Type.GetLocation().GetLineSpan()),
        });
    }

    // ── Property types ────────────────────────────────────────────────────────

    private static void ExtractPropertyTypeHints(PropertyDeclarationSyntax propDecl, List<ReferenceHint> hints)
    {
        var typeName = GetSimpleTypeName(propDecl.Type);
        if (typeName == null) return;

        var containingType = propDecl.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (containingType == null) return;

        hints.Add(new ReferenceHint
        {
            SourceQualifiedPath = ComputeTypePath(containingType),
            Kind = RelationshipKind.ContainsPropertyOfType,
            TargetName = typeName,
            Evidence = propDecl.Type.ToString(),
            EvidenceSpan = ToSourceSpan(propDecl.Type.GetLocation().GetLineSpan()),
        });
    }

    // ── Methods ───────────────────────────────────────────────────────────────

    private static void ExtractMethodHints(MethodDeclarationSyntax methodDecl, List<ReferenceHint> hints)
    {
        var methodPath = ComputeMethodPath(methodDecl, methodDecl.Identifier.Text);

        // Return type
        var returnTypeName = GetSimpleTypeName(methodDecl.ReturnType);
        if (returnTypeName != null)
        {
            hints.Add(new ReferenceHint
            {
                SourceQualifiedPath = methodPath,
                Kind = RelationshipKind.ReturnsType,
                TargetName = returnTypeName,
                Evidence = methodDecl.ReturnType.ToString(),
                EvidenceSpan = ToSourceSpan(methodDecl.ReturnType.GetLocation().GetLineSpan()),
            });
        }

        // Parameter types
        foreach (var param in methodDecl.ParameterList.Parameters)
        {
            if (param.Type == null) continue;
            var paramTypeName = GetSimpleTypeName(param.Type);
            if (paramTypeName == null) continue;

            hints.Add(new ReferenceHint
            {
                SourceQualifiedPath = methodPath,
                Kind = RelationshipKind.AcceptsParameterType,
                TargetName = paramTypeName,
                Evidence = param.Type.ToString(),
                EvidenceSpan = ToSourceSpan(param.Type.GetLocation().GetLineSpan()),
            });
        }
    }

    // ── Constructors ──────────────────────────────────────────────────────────

    private static void ExtractConstructorHints(ConstructorDeclarationSyntax ctorDecl, List<ReferenceHint> hints)
    {
        var ctorPath = ComputeMethodPath(ctorDecl, ctorDecl.Identifier.Text);

        foreach (var param in ctorDecl.ParameterList.Parameters)
        {
            if (param.Type == null) continue;
            var paramTypeName = GetSimpleTypeName(param.Type);
            if (paramTypeName == null) continue;

            hints.Add(new ReferenceHint
            {
                SourceQualifiedPath = ctorPath,
                Kind = RelationshipKind.AcceptsParameterType,
                TargetName = paramTypeName,
                Evidence = param.Type.ToString(),
                EvidenceSpan = ToSourceSpan(param.Type.GetLocation().GetLineSpan()),
            });
        }
    }

    // ── Object creation ───────────────────────────────────────────────────────

    private static void ExtractObjectCreationHints(ObjectCreationExpressionSyntax creation, List<ReferenceHint> hints)
    {
        var typeName = GetSimpleTypeName(creation.Type);
        if (typeName == null) return;

        var srcPath = GetContainingMemberPath(creation);
        if (srcPath == null) return;

        hints.Add(new ReferenceHint
        {
            SourceQualifiedPath = srcPath,
            Kind = RelationshipKind.CreatesInstanceOf,
            TargetName = typeName,
            Evidence = creation.Type.ToString(),
            EvidenceSpan = ToSourceSpan(creation.Type.GetLocation().GetLineSpan()),
        });
    }

    // ── Attributes ────────────────────────────────────────────────────────────

    private static void ExtractAttributeHints(AttributeSyntax attr, List<ReferenceHint> hints)
    {
        var attrName = GetAttributeName(attr.Name);
        if (attrName == null) return;

        var srcPath = GetContainingMemberPath(attr);
        if (srcPath == null) return;

        // Normalize: C# allows omitting "Attribute" suffix in usage
        var normalizedName = attrName.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase)
            ? attrName : attrName + "Attribute";

        hints.Add(new ReferenceHint
        {
            SourceQualifiedPath = srcPath,
            Kind = RelationshipKind.UsesAttributeOrAnnotation,
            TargetName = normalizedName,
            Evidence = attr.Name.ToString(),
            EvidenceSpan = ToSourceSpan(attr.Name.GetLocation().GetLineSpan()),
        });
    }

    // ── Path computation helpers ──────────────────────────────────────────────

    /// <summary>
    /// Computes the fully qualified path of a type declaration by walking up the syntax tree.
    /// </summary>
    internal static string ComputeTypePath(TypeDeclarationSyntax typDecl)
    {
        var parts = new List<string> { typDecl.Identifier.Text };
        SyntaxNode? current = typDecl.Parent;
        while (current != null)
        {
            switch (current)
            {
                case TypeDeclarationSyntax parentType:
                    parts.Insert(0, parentType.Identifier.Text);
                    break;
                case NamespaceDeclarationSyntax ns:
                    var nsParts = ns.Name.ToString().Split('.');
                    for (int i = nsParts.Length - 1; i >= 0; i--)
                        parts.Insert(0, nsParts[i]);
                    break;
                case FileScopedNamespaceDeclarationSyntax fsns:
                    var fsnsParts = fsns.Name.ToString().Split('.');
                    for (int i = fsnsParts.Length - 1; i >= 0; i--)
                        parts.Insert(0, fsnsParts[i]);
                    break;
            }
            current = current.Parent;
        }
        return string.Join(".", parts);
    }

    /// <summary>
    /// Computes the qualified path of a method or constructor.
    /// </summary>
    private static string ComputeMethodPath(SyntaxNode methodNode, string memberName)
    {
        var containingType = methodNode.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (containingType == null) return memberName;
        return $"{ComputeTypePath(containingType)}.{memberName}";
    }

    /// <summary>
    /// Returns the qualified path of the innermost method, constructor, or type containing <paramref name="node"/>.
    /// </summary>
    private static string? GetContainingMemberPath(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is MethodDeclarationSyntax m)
                return ComputeMethodPath(m, m.Identifier.Text);
            if (ancestor is ConstructorDeclarationSyntax c)
                return ComputeMethodPath(c, c.Identifier.Text);
            if (ancestor is TypeDeclarationSyntax t)
                return ComputeTypePath(t);
        }
        return null;
    }

    // ── Type name extraction ──────────────────────────────────────────────────

    /// <summary>
    /// Extracts the simple (unqualified, non-generic) type name from a TypeSyntax.
    /// Returns null for primitive/predefined types (int, string, void, etc.).
    /// </summary>
    private static string? GetSimpleTypeName(TypeSyntax? typeSyntax) => typeSyntax switch
    {
        null => null,
        PredefinedTypeSyntax => null,
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax gen => gen.Identifier.Text,
        QualifiedNameSyntax qual => GetQualifiedRightName(qual),
        NullableTypeSyntax nullable => GetSimpleTypeName(nullable.ElementType),
        ArrayTypeSyntax array => GetSimpleTypeName(array.ElementType),
        _ => null,
    };

    private static string GetQualifiedRightName(QualifiedNameSyntax qual)
    {
        // Walk to the rightmost identifier: A.B.C → "C"
        SimpleNameSyntax right = qual.Right;
        return right switch
        {
            GenericNameSyntax gen => gen.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => right.Identifier.Text,
        };
    }

    private static string? GetAttributeName(NameSyntax name) => name switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        QualifiedNameSyntax qual => GetQualifiedRightName(qual),
        GenericNameSyntax gen => gen.Identifier.Text,
        _ => null,
    };

    // ── Heuristics ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the type name follows the C# interface naming convention (starts with I+uppercase).
    /// </summary>
    private static bool IsLikelyInterface(string name) =>
        name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]);

    // ── SourceSpan helpers ────────────────────────────────────────────────────

    private static SourceSpan? ToSourceSpan(FileLinePositionSpan span)
    {
        if (!span.IsValid) return null;
        return new SourceSpan
        {
            StartLine = span.StartLinePosition.Line + 1,
            StartColumn = span.StartLinePosition.Character + 1,
            EndLine = span.EndLinePosition.Line + 1,
            EndColumn = span.EndLinePosition.Character + 1,
        };
    }
}

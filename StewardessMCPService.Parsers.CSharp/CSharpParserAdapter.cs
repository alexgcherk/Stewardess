// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using StewardessMCPService.CodeIndexing.Model.Diagnostics;
using StewardessMCPService.CodeIndexing.Model.References;
using StewardessMCPService.CodeIndexing.Model.Structural;
using StewardessMCPService.CodeIndexing.Parsers.Abstractions;
using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;
using IndexDiagSeverity = StewardessMCPService.CodeIndexing.Model.Diagnostics.DiagnosticSeverity;
using IndexDiagSource = StewardessMCPService.CodeIndexing.Model.Diagnostics.DiagnosticSource;
using LangId = StewardessMCPService.CodeIndexing.LanguageDetection.LanguageId;

namespace StewardessMCPService.Parsers.CSharp;

/// <summary>
///     Parser adapter for C# source files using the Roslyn syntax tree.
///     Extraction mode: <see cref="ExtractionMode.CompilerSyntax" /> (no semantic model).
/// </summary>
public sealed class CSharpParserAdapter : IParserAdapter
{
    private const string Version = "1.0.0";

    /// <inheritdoc />
    public string LanguageId => LangId.CSharp;

    /// <inheritdoc />
    public AdapterCapabilities Capabilities { get; } = new()
    {
        LanguageId = LangId.CSharp,
        AdapterVersion = Version,
        SupportsOutline = true,
        SupportsDeclarations = true,
        SupportsLogicalSymbols = false, // Phase 2
        SupportsOccurrences = false, // Phase 2
        SupportsImportsOrUses = true,
        SupportsTypeExtraction = true,
        SupportsCallableExtraction = true,
        SupportsMemberExtraction = true,
        SupportsReferenceExtraction = true, // Phase 3
        SupportsRepositoryResolution = false,
        SupportsCrossFileResolution = false,
        SupportsHeuristicFallback = false,
        SupportsSyntaxErrorRecovery = true, // Roslyn always produces a tree
        SupportsDocumentTreeOnly = false,
        GuaranteeNotes = "Uses Roslyn SyntaxTree (CompilerSyntax mode). " +
                         "Structural nodes and source spans are accurate. " +
                         "Semantic analysis (type resolution, overloads) is deferred to Phase 2."
    };

    /// <inheritdoc />
    public Task<ParseResult> ParseAsync(ParseRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var diagnostics = new List<IndexDiagnostic>();

        try
        {
            var tree = CSharpSyntaxTree.ParseText(
                request.Content,
                new CSharpParseOptions(LanguageVersion.Latest),
                request.FilePath,
                cancellationToken: ct);

            var root = tree.GetRoot(ct);

            // Collect Roslyn parser diagnostics
            foreach (var d in tree.GetDiagnostics(ct))
                if (d.Severity == RoslynSeverity.Error)
                {
                    var span = d.Location.GetLineSpan();
                    diagnostics.Add(new IndexDiagnostic
                    {
                        DiagnosticId = $"diag-{request.FileId}-{Guid.NewGuid():N}",
                        Severity = IndexDiagSeverity.Warning,
                        Source = IndexDiagSource.ParserAdapter,
                        Code = d.Id,
                        Message = d.GetMessage(),
                        FilePath = request.FilePath,
                        SourceSpan = ToSourceSpan(span)
                    });
                }

            var extractor = new CSharpStructuralExtractor(request.FileId, request.FilePath, ct);
            var nodes = extractor.Extract(root);

            // Extract using directives (Phase 3)
            var imports = ExtractImports(root);

            // Extract reference hints (Phase 3)
            var referenceHints = CSharpReferenceHintExtractor.ExtractHints(root, ct);

            var parseStatus = tree.GetDiagnostics(ct).Any(d => d.Severity == RoslynSeverity.Error)
                ? ParseStatus.Partial
                : ParseStatus.Success;

            return Task.FromResult(new ParseResult
            {
                FileId = request.FileId,
                Status = parseStatus,
                Nodes = nodes,
                Imports = imports,
                ReferenceHints = referenceHints,
                Diagnostics = diagnostics,
                ExtractionMode = ExtractionMode.CompilerSyntax,
                AdapterVersion = Version
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new IndexDiagnostic
            {
                DiagnosticId = $"diag-{request.FileId}-fatal",
                Severity = IndexDiagSeverity.Error,
                Source = IndexDiagSource.ParserAdapter,
                Code = "PARSE_EXCEPTION",
                Message = $"Roslyn parser threw: {ex.Message}",
                FilePath = request.FilePath
            });

            return Task.FromResult(new ParseResult
            {
                FileId = request.FileId,
                Status = ParseStatus.Failed,
                Diagnostics = diagnostics,
                ExtractionMode = ExtractionMode.CompilerSyntax,
                AdapterVersion = Version
            });
        }
    }

    /// <summary>Extracts using directives as import entries.</summary>
    private static IReadOnlyList<ImportEntry> ExtractImports(SyntaxNode root)
    {
        var imports = new List<ImportEntry>();
        foreach (var usingDir in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var isAlias = usingDir.Alias != null;
            var isStatic = usingDir.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);
            var kind = isAlias ? "using-alias" : isStatic ? "using-static" : "using";
            var target = usingDir.NamespaceOrType?.ToString() ?? usingDir.Name?.ToString();
            var alias = usingDir.Alias?.Name.Identifier.Text;

            imports.Add(new ImportEntry
            {
                Kind = kind,
                RawText = usingDir.ToString().Trim(),
                NormalizedTarget = target,
                Alias = alias,
                SourceSpan = ToSourceSpan(usingDir.GetLocation().GetLineSpan())
            });
        }

        return imports;
    }

    private static SourceSpan? ToSourceSpan(FileLinePositionSpan span)
    {
        if (!span.IsValid) return null;
        return new SourceSpan
        {
            StartLine = span.StartLinePosition.Line + 1,
            StartColumn = span.StartLinePosition.Character + 1,
            EndLine = span.EndLinePosition.Line + 1,
            EndColumn = span.EndLinePosition.Character + 1
        };
    }
}
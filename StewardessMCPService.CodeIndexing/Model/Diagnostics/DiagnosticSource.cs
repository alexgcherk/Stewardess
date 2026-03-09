// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
namespace StewardessMCPService.CodeIndexing.Model.Diagnostics;

/// <summary>
/// Identifies which pipeline stage produced an <see cref="IndexDiagnostic"/>.
/// </summary>
public enum DiagnosticSource
{
    /// <summary>Diagnostic produced by the eligibility filter stage.</summary>
    EligibilityFilter,
    /// <summary>Diagnostic produced by the language detection stage.</summary>
    LanguageDetector,
    /// <summary>Diagnostic produced by a parser adapter.</summary>
    ParserAdapter,
    /// <summary>Diagnostic produced during declaration projection.</summary>
    DeclarationProjection,
    /// <summary>Diagnostic produced during reference extraction.</summary>
    ReferenceExtraction,
    /// <summary>Diagnostic produced during symbol resolution.</summary>
    Resolution,
    /// <summary>Diagnostic produced during index storage.</summary>
    Storage,
    /// <summary>Diagnostic produced during general indexing coordination.</summary>
    Indexing,
}

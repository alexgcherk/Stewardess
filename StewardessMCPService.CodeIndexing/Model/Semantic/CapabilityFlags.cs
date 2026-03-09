// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
namespace StewardessMCPService.CodeIndexing.Model.Semantic;

/// <summary>
/// Bitmask of capabilities that were exercised when projecting a <see cref="LogicalSymbol"/>.
/// Consumers can use these flags to understand what information is available.
/// </summary>
[Flags]
public enum CapabilityFlags
{
    /// <summary>No special capabilities exercised.</summary>
    None = 0,

    /// <summary>Symbol has at least one declared occurrence.</summary>
    HasDeclarations = 1 << 0,

    /// <summary>Symbol has multiple occurrences (e.g., partial classes).</summary>
    HasMultipleOccurrences = 1 << 1,

    /// <summary>Symbol has outbound references extracted.</summary>
    HasReferences = 1 << 2,

    /// <summary>Symbol participates in the dependency graph.</summary>
    HasDependencies = 1 << 3,

    /// <summary>Symbol has imports or using directives associated.</summary>
    HasImports = 1 << 4,

    /// <summary>Symbol has child members (methods, fields, properties).</summary>
    HasMembers = 1 << 5,

    /// <summary>Symbol documentation summary was extracted.</summary>
    HasDocumentation = 1 << 6,
}

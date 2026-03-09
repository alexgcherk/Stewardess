// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.CodeIndexing.Model.References;

/// <summary>
///     Describes the semantic relationship between a source and target symbol in a reference edge.
/// </summary>
public enum RelationshipKind
{
    // --- Type hierarchy ---
    /// <summary>Source type inherits from the target type.</summary>
    Inherits,

    /// <summary>Source type implements the target interface.</summary>
    Implements,

    // --- Member type relationships ---
    /// <summary>Source type declares a field whose type is the target.</summary>
    ContainsFieldOfType,

    /// <summary>Source type declares a property whose type is the target.</summary>
    ContainsPropertyOfType,

    /// <summary>Source callable returns the target type.</summary>
    ReturnsType,

    /// <summary>Source callable accepts a parameter of the target type.</summary>
    AcceptsParameterType,

    /// <summary>Source callable declares a local variable of the target type.</summary>
    UsesLocalType,

    /// <summary>Source symbol uses the target type as a generic type argument.</summary>
    UsesGenericArgumentType,

    // --- Instantiation / usage ---
    /// <summary>Source symbol creates an instance of the target type.</summary>
    CreatesInstanceOf,

    /// <summary>Source symbol references a static member of the target type.</summary>
    ReferencesStaticMemberOf,

    /// <summary>Source symbol is decorated with an attribute or annotation from the target.</summary>
    UsesAttributeOrAnnotation,

    // --- Module / import ---
    /// <summary>Source file imports or requires the target module.</summary>
    ImportsModule,

    /// <summary>Source file brings the target namespace into scope.</summary>
    UsesNamespace,

    /// <summary>Source introduces an alias that maps to the target symbol.</summary>
    AliasesSymbol,

    // --- Nested / containment ---
    /// <summary>Source type declares a nested type that is the target.</summary>
    DefinesNestedType,

    // --- Callable / member reference ---
    /// <summary>Source symbol calls or references the target callable.</summary>
    ReferencesCallable,

    /// <summary>Source symbol reads or writes the target member.</summary>
    ReferencesMember,

    /// <summary>Source symbol references the target type alias.</summary>
    ReferencesTypeAlias,

    // --- Database objects ---
    /// <summary>Source symbol issues a query against the target database table.</summary>
    ReferencesTable,

    /// <summary>Source symbol queries the target database view.</summary>
    ReferencesView,

    /// <summary>Source symbol invokes the target stored procedure.</summary>
    ReferencesStoredProcedure
}
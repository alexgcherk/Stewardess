// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
namespace StewardessMCPService.CodeIndexing.Model.Semantic;

/// <summary>
/// Semantic kind of a logical symbol. Maps language-specific constructs to a
/// canonical vocabulary.
/// </summary>
public enum SymbolKind
{
    // --- Container / scope symbols ---
    /// <summary>A namespace or package scope container.</summary>
    Namespace,
    /// <summary>A package or distribution unit container.</summary>
    Package,
    /// <summary>A module-level container (e.g., Python module, JavaScript module).</summary>
    Module,
    /// <summary>A top-level script file acting as a scope container.</summary>
    Script,

    // --- Type symbols ---
    /// <summary>A class type symbol.</summary>
    Class,
    /// <summary>A struct (value type) symbol.</summary>
    Struct,
    /// <summary>An interface type symbol.</summary>
    Interface,
    /// <summary>An enumeration type symbol.</summary>
    Enum,
    /// <summary>A record type symbol.</summary>
    Record,
    /// <summary>A trait symbol (e.g., Rust trait, Scala trait).</summary>
    Trait,
    /// <summary>A singleton object symbol (e.g., Scala object, Kotlin object).</summary>
    Object,
    /// <summary>A union type symbol.</summary>
    Union,
    /// <summary>A type alias symbol.</summary>
    TypeAlias,

    // --- Callable symbols ---
    /// <summary>An instance method symbol.</summary>
    Method,
    /// <summary>A free function symbol.</summary>
    Function,
    /// <summary>A constructor symbol.</summary>
    Constructor,
    /// <summary>A procedure symbol (e.g., stored procedure, VB Sub).</summary>
    Procedure,
    /// <summary>A property getter accessor symbol.</summary>
    Getter,
    /// <summary>A property setter accessor symbol.</summary>
    Setter,

    // --- Member symbols ---
    /// <summary>A property member symbol.</summary>
    Property,
    /// <summary>A field member symbol.</summary>
    Field,
    /// <summary>A constant symbol.</summary>
    Constant,
    /// <summary>An event member symbol.</summary>
    Event,
    /// <summary>A local or module-level variable symbol.</summary>
    Variable,

    // --- SQL / DB symbols ---
    /// <summary>A database table symbol.</summary>
    Table,
    /// <summary>A database view symbol.</summary>
    View,
    /// <summary>A stored procedure symbol.</summary>
    StoredProcedure,
    /// <summary>A database function object symbol.</summary>
    FunctionObject,

    // --- Document symbols ---
    /// <summary>A logical section within a document (e.g., Markdown heading).</summary>
    DocumentSection,
}

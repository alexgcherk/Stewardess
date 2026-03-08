# Technical Specification: Multi-Language Code Structure Support for Existing MCP Service

## 1. Purpose

This specification defines how to extend the existing MCP service so it can parse, normalize, index, and expose navigable source-code structure across the following file types:

- `.cs` → C#
- `.vb` → VB.NET
- `.fs` → F#
- `.ts` → TypeScript
- `.js` → JavaScript
- `.py` → Python
- `.java` → Java
- `.go` → Go
- `.rs` → Rust
- `.cpp`, `.cc`, `.cxx` → C++
- `.c` → C
- `.json` → JSON
- `.xml` → XML
- `.html` → HTML
- `.css` → CSS
- `.sql` → SQL
- `.sh` → Shell
- `.ps1` → PowerShell
- `.md` → Markdown

The primary goal is to allow a consumer application to reliably and efficiently discover:

- namespaces / packages / modules
- classes / structs / interfaces / enums / records / traits / types
- methods / functions / constructors / procedures
- properties / fields / constants
- dependencies between user-defined types
- source locations and containment hierarchy

This design intentionally separates **language-specific parsing** from a **common semantic model**, so the consuming application can query all supported languages through one stable API.

---

## 2. Goals

### 2.1 Functional goals

The updated MCP service shall:

1. Detect the programming language of a file from extension, with optional content-based fallback.
2. Parse source files and extract structural declarations.
3. Normalize language-specific syntax into a shared code model.
4. Index symbols and relationships for fast lookups.
5. Expose MCP tools for consumers to query:
   - file outline
   - namespace/type/function/property lookup
   - inbound/outbound type dependencies
   - symbol location
   - members of a type
   - references to user-defined types
6. Support partial results even when files contain syntax errors.
7. Prefer user-defined class/type dependencies over third-party or framework types.

### 2.2 Non-functional goals

The updated system shall:

1. Be modular and easy to extend with new languages.
2. Support incremental re-indexing when files change.
3. Be resilient to broken or incomplete code.
4. Scale to medium and large repositories.
5. Return stable identifiers for symbols.
6. Keep parser implementations isolated behind adapters.
7. Favor declaration-level correctness over full semantic compilation when cross-language consistency is more important than compiler-perfect resolution.

### 2.3 Out of scope for first release

The first release will not require:

- full compile-time type checking
- full generic type inference
- compiler-perfect overload resolution in every language
- macro/preprocessor expansion parity across all languages
- full framework dependency graphs
- complete SQL semantic parsing for all dialects
- control flow graph construction
- data flow analysis
- inter-procedural call graph perfection

---

## 3. Key Design Principle

The MCP service must not force all languages into a pure OOP model. Instead, it shall use a **unified structural model** with broad enough abstractions to represent object-oriented, procedural, functional, and document-oriented source formats.

Core abstractions:

- **Container**: namespace, package, module, script, file, document section
- **Type**: class, struct, interface, enum, record, trait, union-like type
- **Callable**: method, function, constructor, procedure, script function
- **Member**: property, field, constant, variable-like declaration
- **Dependency**: a user-defined type referenced by another type/member/signature/body

---

## 4. Supported Language Behavior Matrix

## 4.1 Full structure extraction target

These languages shall support declaration extraction for types and callables:

- C#
- VB.NET
- F#
- TypeScript
- JavaScript
- Python
- Java
- Go
- Rust
- C++
- C
- PowerShell
- SQL
- Shell

## 4.2 Document/tree extraction target

These file types shall be parsed into structural trees, but not forced into class/method semantics:

- JSON
- XML
- HTML
- CSS
- Markdown

These document-like formats still participate in file outline/search, but not in class dependency graphs.

---

## 5. Proposed Application Architecture

## 5.1 High-level architecture

```text
Repository / Filesystem / Connected Source
        ↓
Language Detection Layer
        ↓
Parser Adapter Layer
        ↓
Language-Specific AST / Parse Result
        ↓
Normalization / Semantic Mapping Layer
        ↓
Canonical Code Model
        ↓
Symbol Index + Relationship Graph + File Outline Index
        ↓
MCP Query Tools / Consumer APIs
```

## 5.2 Architecture components

### A. Source Provider
Responsible for enumerating files, reading content, and providing change notifications.

Responsibilities:
- enumerate repository files
- filter supported extensions
- provide file content and hash
- emit file changed / file removed events

### B. Language Detector
Maps file extension to internal language ID. May optionally inspect content for fallback detection.

Responsibilities:
- extension mapping
- shebang detection for shell/python
- fallback to text/document classification

### C. Parser Adapter Layer
A plug-in architecture that hides parser implementation details.

Recommended interface:

```csharp
public interface ILanguageParserAdapter
{
    string LanguageId { get; }
    ParseResult Parse(ParseRequest request);
}
```

Recommended `ParseRequest`:
- file path
- file content
- repository root
- parse mode (`OutlineOnly`, `Structure`, `StructureAndDependencies`)

Recommended `ParseResult`:
- raw parser diagnostics
- syntax tree or intermediate tree
- extracted declarations
- extracted references
- confidence level

### D. Normalization Layer
Maps parser output to a canonical code model.

Responsibilities:
- convert language-specific constructs to common nodes
- normalize package / namespace / module semantics
- normalize callable/member declarations
- assign stable symbol identifiers
- attach source spans and containment hierarchy

### E. Indexing Layer
Builds query-optimized indexes.

Required indexes:
- symbol-by-id
- symbols-by-name
- symbols-by-qualified-name
- file-to-symbols
- container-to-children
- type-to-members
- type dependency graph
- reverse dependency graph

### F. MCP Query Layer
Exposes stable tools to consumer applications.

Responsibilities:
- search and navigation
- type/member inspection
- dependency graph retrieval
- source location resolution
- outline generation

### G. Incremental Update Coordinator
Re-indexes only impacted files and updates graph edges.

Responsibilities:
- compare content hash
- remove stale symbols on file deletion
- recalculate affected dependency edges
- preserve index integrity

---

## 6. Parser Strategy

## 6.1 General strategy

The system shall use the best parser available per language, while exposing a consistent result contract.

### Recommended parser backends

- **C# / VB.NET**: Roslyn
- **TypeScript / JavaScript**: TypeScript compiler API or Tree-sitter
- **Python**: Python AST or Tree-sitter
- **Java**: JavaParser or Tree-sitter
- **Go**: native Go parser or Tree-sitter
- **Rust**: syn or Tree-sitter
- **C / C++**: Tree-sitter or Clang-based extractor
- **PowerShell**: PowerShell AST
- **JSON / XML / HTML**: standard document parsers
- **CSS**: CSS parser or Tree-sitter
- **SQL / Shell / F# / Markdown**: Tree-sitter or language-specific parser where practical

## 6.2 Fallback requirement

If a full parser is unavailable or fails, the system shall fall back to a **heuristic declaration scanner**.

The heuristic scanner shall:
- ignore comments and string literals where possible
- track braces/indentation blocks
- identify likely declarations via keywords
- return lower-confidence nodes

This guarantees outline continuity even on malformed files.

---

## 7. Canonical Semantic Model

## 7.1 Core entities

### CodeFile
Represents one source file.

Fields:
- fileId
- path
- languageId
- hash
- parseStatus
- diagnostics
- topLevelContainers
- topLevelTypes
- topLevelCallables
- topLevelMembers

### Symbol
Common base metadata for all symbols.

Fields:
- symbolId
- name
- qualifiedName
- displayName
- kind
- languageId
- fileId
- parentSymbolId
- sourceSpan
- visibility
- modifiers
- confidence

### ContainerSymbol
Represents namespace/package/module/script/file-level grouping.

Kinds:
- namespace
- package
- module
- file-module
- script
- document-section

### TypeSymbol
Represents a type-like declaration.

Kinds:
- class
- struct
- interface
- enum
- record
- trait
- union
- object
- type-alias

Fields:
- baseTypes
- implementedTypes
- genericParameters
- memberIds

### CallableSymbol
Represents an executable/member operation.

Kinds:
- method
- function
- constructor
- destructor
- operator
- procedure
- getter
- setter

Fields:
- returnTypeDisplay
- parameter list
- localTypeReferences (optional)

### MemberSymbol
Represents property/field/constant-like declarations.

Kinds:
- property
- field
- constant
- event
- variable

Fields:
- typeDisplay
- accessor metadata

### TypeReferenceEdge
Represents a dependency from one symbol to another user-defined type.

Fields:
- edgeId
- sourceSymbolId
- targetSymbolId
- relationshipKind
- evidence
- sourceSpan
- confidence

## 7.2 Relationship kinds

Supported dependency kinds:
- inherits
- implements
- contains-field-of-type
- contains-property-of-type
- method-returns-type
- method-parameter-type
- method-local-uses-type
- generic-argument-type
- creates-instance-of
- references-static-member-of
- attribute-or-annotation-type

---

## 8. Stable Symbol Identity

Each symbol must receive a stable ID so consumer applications can cache and re-request symbols.

Recommended identity format:

```text
{language}:{repo-relative-path}:{qualified-symbol-name}:{symbol-kind}:{signature-hash?}
```

Examples:
- `csharp:src/Domain/User.cs:MyApp.Domain.User:class`
- `csharp:src/Domain/User.cs:MyApp.Domain.User.GetDisplayName:method:8D12AB`
- `python:app/models/user.py:app.models.user.User:class`

Guidelines:
- include signature hash for overloaded methods
- exclude line numbers from identity if possible
- allow source span to change without changing symbol ID

---

## 9. Dependency Resolution Strategy

## 9.1 Objective

The system must allow a consumer to easily discover **dependencies on other user-defined classes/types**. This means the index should prefer repository-defined types over framework/external symbols.

## 9.2 Resolution phases

### Phase 1: Local file extraction
Extract candidate type references from:
- base type lists
- implemented interfaces
- method return types
- parameter types
- property/field types
- object creation expressions where feasible
- attribute/annotation usage where feasible

### Phase 2: Local normalization
Normalize candidate type names:
- strip nullable wrappers
- unwrap generic containers
- split namespace-qualified names
- normalize alias/import usage where possible

### Phase 3: Repository resolution
Resolve candidate references against the repository symbol index.

Resolution order:
1. exact fully-qualified symbol match
2. imported namespace/module + simple name match
3. same namespace/package/module match
4. same file/container match
5. unique name match across repo
6. unresolved candidate recorded as external or unknown

### Phase 4: Edge creation
Create dependency edges only when the target is a repository-defined symbol.

### Phase 5: Reverse indexing
Maintain reverse dependency graph to answer:
- what classes depend on this class?
- where is this type used?

## 9.3 Dependency evidence

Each dependency edge should store where it came from:
- field declaration
- property declaration
- method signature
- inheritance clause
- object creation
- annotation/attribute

This makes results explainable in the consumer UI.

---

## 10. Query Requirements for Consumer Applications

The consumer application needs simple, reliable discovery APIs. The MCP service should therefore present query tools that hide parser complexity.

## 10.1 Required consumer capabilities

The consumer must be able to:

1. List namespaces/packages/modules in a repo or file.
2. Find all classes/types in a namespace/module.
3. Find methods and properties of a class/type.
4. Search symbol by simple name or qualified name.
5. Jump to source location for a symbol.
6. Retrieve outbound dependencies of a class/type.
7. Retrieve inbound dependencies of a class/type.
8. Retrieve a file outline.
9. Filter symbols by language, file, namespace, or kind.
10. Distinguish user-defined dependencies from external/unresolved references.

---

## 11. Proposed MCP Tool Surface

The MCP service should add or update tools roughly along the following lines.

## 11.1 `code_index.build`
Builds or rebuilds index for a repository or path.

Input:
- root path
- include/exclude patterns
- parse mode
- full rebuild or incremental

Output:
- files indexed
- symbols created
- edges created
- diagnostics summary

## 11.2 `code_index.get_file_outline`
Returns the structural outline of a file.

Input:
- file path

Output:
- containers
- types
- callables
- members
- source spans

## 11.3 `code_index.find_symbols`
Finds symbols by name or pattern.

Input:
- query text
- optional language filter
- optional kind filter
- optional namespace/container filter

Output:
- matching symbols with kind, qualified name, file path, span

## 11.4 `code_index.get_symbol`
Returns full details for one symbol.

Input:
- symbol ID

Output:
- metadata
- container path
- members
- signatures
- direct dependencies
- reverse dependencies summary

## 11.5 `code_index.get_type_members`
Returns methods, properties, fields, and nested types for a type.

Input:
- type symbol ID

Output:
- member list grouped by kind

## 11.6 `code_index.get_dependencies`
Returns outbound dependencies for a type or callable.

Input:
- symbol ID
- dependency kinds filter

Output:
- target symbols
- relationship kind
- evidence spans

## 11.7 `code_index.get_dependents`
Returns inbound dependencies to a type.

Input:
- symbol ID

Output:
- source symbols
- relationship kind
- evidence spans

## 11.8 `code_index.resolve_location`
Resolves symbol to file path and source span.

Input:
- symbol ID

Output:
- file path
- start/end line and column

## 11.9 `code_index.get_namespace_tree`
Returns hierarchical namespace/package/module tree.

Input:
- optional language filter
- optional subtree root

Output:
- namespace/module tree with counts

---

## 12. Storage and Index Design

## 12.1 Recommended storage layers

### In-memory indexes
For active query speed:
- symbol-by-id dictionary
- symbol name lookup maps
- file-to-symbol map
- adjacency lists for dependency graph

### Persistent store
For repository reopening and large index reuse:
- SQLite, LiteDB, or similar embedded store

Tables/collections:
- files
- symbols
- symbol_membership
- dependency_edges
- diagnostics
- index_metadata

## 12.2 Suggested indexes

Required DB indexes:
- symbols(symbol_id)
- symbols(name)
- symbols(qualified_name)
- symbols(file_id)
- symbols(kind)
- dependency_edges(source_symbol_id)
- dependency_edges(target_symbol_id)

---

## 13. Incremental Indexing

## 13.1 Requirements

The service must support incremental updates when files change.

Process:
1. detect changed file by hash/timestamp
2. remove old symbols and edges belonging to file
3. reparse file
4. rebuild symbols for file
5. re-resolve edges for file
6. optionally re-resolve files that referenced removed symbols if needed

## 13.2 Integrity rules

- symbol IDs must remain stable where declaration identity remains stable
- deleting a file must remove all symbols and edges from that file
- reverse dependency index must be updated transactionally

---

## 14. Language-Specific Mapping Guidance

## 14.1 Namespace-like constructs

Map to `ContainerSymbol`:
- C# `namespace`
- VB.NET `Namespace`
- Java `package`
- Go `package`
- Rust `mod`
- TS/JS module/file
- Python module/package
- F# module/namespace
- PowerShell script scope

## 14.2 Type-like constructs

Map to `TypeSymbol`:
- class
- struct
- interface
- enum
- record
- trait
- object/module where applicable
- type aliases only when consumer benefit justifies indexing them as pseudo-types

## 14.3 Callable-like constructs

Map to `CallableSymbol`:
- methods inside type definitions
- top-level functions
- constructors
- property getter/setter accessors when exposed by parser backend
- stored procedures/functions in SQL where parser supports it
- PowerShell / shell functions

## 14.4 Property/member constructs

Map to `MemberSymbol`:
- C# properties/fields/events
- Java fields
- TS class properties
- Python class attributes only when reasonably extractable
- Go struct fields
- Rust struct fields

---

## 15. Consumer-Facing Response Shape

The MCP layer should return predictable, implementation-neutral JSON structures.

Example symbol payload:

```json
{
  "symbolId": "csharp:src/Domain/User.cs:MyApp.Domain.User:class",
  "name": "User",
  "qualifiedName": "MyApp.Domain.User",
  "kind": "class",
  "language": "csharp",
  "filePath": "src/Domain/User.cs",
  "containerPath": ["MyApp.Domain"],
  "sourceSpan": {
    "startLine": 12,
    "startColumn": 1,
    "endLine": 88,
    "endColumn": 2
  },
  "members": {
    "properties": [
      {
        "symbolId": "...",
        "name": "Email",
        "kind": "property",
        "typeDisplay": "string"
      }
    ],
    "methods": [
      {
        "symbolId": "...",
        "name": "GetDisplayName",
        "kind": "method",
        "returnTypeDisplay": "string"
      }
    ]
  },
  "dependencies": [
    {
      "targetSymbolId": "csharp:src/Domain/Role.cs:MyApp.Domain.Role:class",
      "qualifiedName": "MyApp.Domain.Role",
      "relationshipKind": "contains-property-of-type",
      "evidence": "Property Role : Role"
    }
  ]
}
```

---

## 16. Error Handling and Diagnostics

The parser system must never fail the whole index because one file is malformed.

Requirements:
- store per-file diagnostics
- return parse status: `Success`, `Partial`, `Failed`
- preserve best-effort outline on partial parse
- tag low-confidence symbols where heuristic extraction was used

---

## 17. Security and Isolation Considerations

The parser/indexing system must not execute user code.

Requirements:
- parsing only, no script execution
- do not evaluate PowerShell, shell, or JavaScript
- do not run build systems or compilers unless explicitly enabled in a future phase
- apply file size limits and timeout limits per file
- protect against parser hangs on pathological input

---

## 18. Performance Requirements

Suggested initial targets for a medium repository:
- initial indexing should provide progress feedback
- incremental re-index of a single file should be near-interactive
- symbol lookup should be sub-second
- dependency query for a symbol should be sub-second once index is built

Recommended optimizations:
- content hash cache
- parallel parsing by file where parser backend permits
- lazy loading of file bodies
- separate outline pass from deep dependency pass if needed

---

## 19. Implementation Phases

## Phase 1: Core foundation

Deliverables:
- language detection
- canonical model
- parser adapter interface
- symbol index
- MCP read/query tools
- stable IDs

Languages in phase 1:
- C#
- VB.NET
- TypeScript
- JavaScript
- Python
- Java

## Phase 2: Dependency graph maturity

Deliverables:
- user-defined type dependency resolution
- reverse dependency index
- evidence capture
- incremental re-indexing

Languages added:
- Go
- Rust
- C++
- C
- F#

## Phase 3: Extended script/document support

Deliverables:
- PowerShell outline
- shell function outline
- SQL object extraction
- JSON/XML/HTML/CSS/Markdown outline tree

## Phase 4: Advanced accuracy

Possible future work:
- import/alias-aware resolution improvements
- generic type unwrapping improvements
- partial call graph
- richer cross-file symbol resolution
- IDE-grade “find references” for selected languages

---

## 20. Recommended Internal Project Structure

```text
/src
  /Core
    CodeModel/
    Indexing/
    Query/
    Diagnostics/
  /LanguageDetection
  /Parsers
    /Abstractions
    /CSharpRoslyn
    /VbRoslyn
    /TreeSitterCommon
    /TypeScript
    /JavaScript
    /Python
    /Java
    /Go
    /Rust
    /Cpp
    /C
    /FSharp
    /PowerShell
    /Sql
    /Shell
    /Documents
  /Normalization
  /DependencyResolution
  /Storage
  /McpTools
  /Tests
    /Fixtures
    /GoldenFiles
```

---

## 21. Testing Strategy

### 21.1 Unit tests

Validate:
- symbol extraction per language
- namespace/container mapping
- member extraction
- stable symbol IDs
- dependency edge creation

### 21.2 Golden-file tests

For each language, store representative sample source files and expected normalized JSON output.

### 21.3 Fault-tolerance tests

Validate behavior on:
- malformed code
- partial files
- huge files
- mixed encodings
- unsupported extensions

### 21.4 Cross-language consistency tests

Validate that semantically similar constructs map consistently:
- C# namespace vs Java package vs Python module
- C# property vs Java field vs TS class property
- C# method vs Python function vs Go method

---

## 22. Recommended Consumer UX Patterns

To make class, namespace, method/property, and dependency discovery easy, the consuming application should present the index through the following views:

1. **Repository Explorer**
   - language filter
   - namespace/module tree
   - file outline

2. **Type Explorer**
   - select type
   - view members grouped by properties, fields, methods, nested types

3. **Dependency Explorer**
   - outbound dependencies to user-defined types
   - inbound dependents
   - group by relationship kind

4. **Search**
   - exact and fuzzy symbol lookup
   - kind/language filters

5. **Source Jump**
   - open file and line/column range from symbol payload

The MCP service should support all of these with minimal client-side inference.

---

## 23. Architecture Recommendation Summary

The recommended architecture is:

1. **Language adapter-based parser architecture**
   - isolates syntax differences
   - allows best parser per language

2. **Canonical semantic model**
   - gives one contract to consumer applications
   - avoids UI/query branching per language

3. **Symbol index plus directed dependency graph**
   - enables fast navigation and dependency analysis

4. **Incremental update pipeline**
   - keeps performance practical

5. **Best-effort parsing with confidence metadata**
   - keeps system robust on imperfect source trees

This architecture is the best fit for an MCP service because it is extensible, query-friendly, resilient, and straightforward for downstream applications to consume.

---

## 24. Final Recommendation

The MCP service should be updated to implement a **multi-language code indexing subsystem** rather than a narrow parser. The service should parse supported files through language adapters, normalize declarations into a common symbol model, persist a symbol/dependency index, and expose MCP query tools that let a consumer application reliably answer:

- what namespaces/modules exist?
- what classes/types exist?
- what methods and properties belong to a type?
- what other user-defined classes does this class depend on?
- where is a symbol defined?
- what depends on this class?

That combination provides a strong foundation for repository navigation, code intelligence, dependency inspection, and future advanced analysis.


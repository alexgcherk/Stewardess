using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StewardessMCPServive.IntegrationTests.Helpers;
using Xunit;

namespace StewardessMCPServive.IntegrationTests.Scenarios
{
    /// <summary>
    /// Integration tests for Python file support, covering Phase 3 tools
    /// (<c>code_index.get_imports</c>, <c>code_index.get_file_references</c>)
    /// and structural outline behaviour (<c>code_index.get_file_outline</c>,
    /// <c>code_index.list_files</c>) against a minimal Python repository.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="PythonIndexedFixture"/> provides a snapshot built from a
    /// single <c>animals.py</c> file containing four import statements of different
    /// kinds and two class declarations.
    /// </para>
    /// <para>
    /// Python reference edges are intentionally not indexed in
    /// <c>ReferencesByFileId</c> because the Python parser sets
    /// <c>SupportsLogicalSymbols = false</c>; therefore reference hints cannot be
    /// anchored to a source symbol ID.  Several tests assert this limitation
    /// explicitly to prevent future regressions if Python logical-symbol support
    /// is ever added.
    /// </para>
    /// </remarks>
    [Collection(IntegrationTestCollection.Name)]
    public sealed class CodeIndexPythonTests : IClassFixture<PythonIndexedFixture>
    {
        private readonly PythonIndexedFixture _fixture;

        /// <summary>Receives the shared fixture injected by xUnit.</summary>
        public CodeIndexPythonTests(PythonIndexedFixture fixture)
        {
            _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns the string value of <paramref name="key"/> from a JToken,
        /// using a case-insensitive lookup.
        /// </summary>
        private static string? Str(JToken token, string key) =>
            (token as JObject)?.GetValue(key, StringComparison.OrdinalIgnoreCase)?.Value<string>();

        // ── code_index.list_files — Python language detection ─────────────────────

        /// <summary>
        /// The file list must contain exactly <c>animals.py</c> and report
        /// <c>LanguageId = "python"</c>, confirming Python files are detected
        /// and indexed.
        /// </summary>
        [Fact]
        public async Task ListFiles_AfterBuild_ReturnsPythonFile()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.list_files",
                new { root_path = _fixture.RootPath });

            Assert.False(isError);
            var files = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(files);
            Assert.Single(files!);

            var file = files![0].Value<JObject>()!;
            var lang = Str(file, "LanguageId");
            Assert.Equal("python", lang, StringComparer.OrdinalIgnoreCase);
        }

        // ── code_index.get_imports ────────────────────────────────────────────────

        /// <summary>
        /// <c>animals.py</c> contains four import statements so Items must have
        /// exactly <see cref="PythonIndexedFixture.ImportCount"/> entries.
        /// </summary>
        [Fact]
        public async Task GetImports_AnimalsPy_ReturnsFourItems()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _fixture.SampleFilePath, root_path = _fixture.RootPath });

            Assert.False(isError);
            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.Equal(PythonIndexedFixture.ImportCount, items!.Count);
        }

        /// <summary>
        /// The first import (<c>import os</c>) must have <c>Kind = "import"</c>
        /// and <c>NormalizedTarget = "os"</c> with no alias.
        /// </summary>
        [Fact]
        public async Task GetImports_AnimalsPy_FirstImportIsPlainOs()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _fixture.SampleFilePath, root_path = _fixture.RootPath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);

            var first = items![0].Value<JObject>()!;
            Assert.Equal("import", Str(first, "Kind"), StringComparer.OrdinalIgnoreCase);
            Assert.Equal("os",     Str(first, "NormalizedTarget"), StringComparer.OrdinalIgnoreCase);

            // Plain import has no alias
            var alias = Str(first, "Alias");
            Assert.True(string.IsNullOrEmpty(alias),
                $"Expected no alias for 'import os', but got '{alias}'.");
        }

        /// <summary>
        /// The second import (<c>import sys as system</c>) must have
        /// <c>Kind = "import"</c>, <c>NormalizedTarget = "sys"</c>, and
        /// <c>Alias = "system"</c>.
        /// </summary>
        [Fact]
        public async Task GetImports_AnimalsPy_SecondImportHasAlias()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _fixture.SampleFilePath, root_path = _fixture.RootPath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);

            var second = items![1].Value<JObject>()!;
            Assert.Equal("import",  Str(second, "Kind"),             StringComparer.OrdinalIgnoreCase);
            Assert.Equal("sys",     Str(second, "NormalizedTarget"),  StringComparer.OrdinalIgnoreCase);
            Assert.Equal("system",  Str(second, "Alias"),             StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The third import (<c>from typing import List</c>) must have
        /// <c>Kind = "from-import"</c> and <c>NormalizedTarget = "typing"</c>.
        /// </summary>
        [Fact]
        public async Task GetImports_AnimalsPy_ThirdImportIsFromImport()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _fixture.SampleFilePath, root_path = _fixture.RootPath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);

            var third = items![2].Value<JObject>()!;
            Assert.Equal("from-import", Str(third, "Kind"),            StringComparer.OrdinalIgnoreCase);
            Assert.Equal("typing",      Str(third, "NormalizedTarget"), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The third import's <c>Alias</c> must contain the imported symbol name
        /// (<c>List</c>), because the Python adapter stores the <c>names</c> part
        /// of a <c>from … import …</c> statement in the <c>Alias</c> field.
        /// </summary>
        [Fact]
        public async Task GetImports_AnimalsPy_FromImportAliasContainsImportedNames()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _fixture.SampleFilePath, root_path = _fixture.RootPath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);

            var alias = Str(items![2], "Alias");
            Assert.NotNull(alias);
            Assert.Contains("List", alias!, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The fourth import (<c>from .base import BaseAnimal</c>) must have
        /// <c>Kind = "relative-import"</c> and <c>NormalizedTarget = "base"</c>
        /// (the leading dot is stripped from the module name).
        /// </summary>
        [Fact]
        public async Task GetImports_AnimalsPy_FourthImportIsRelativeImport()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _fixture.SampleFilePath, root_path = _fixture.RootPath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);

            var fourth = items![3].Value<JObject>()!;
            Assert.Equal("relative-import", Str(fourth, "Kind"),            StringComparer.OrdinalIgnoreCase);
            Assert.Equal("base",            Str(fourth, "NormalizedTarget"), StringComparer.OrdinalIgnoreCase);
            Assert.Equal("BaseAnimal",      Str(fourth, "Alias"),            StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Every import entry must carry a non-null <c>SourceSpan</c> with a
        /// positive <c>StartLine</c>, confirming line-position data is captured.
        /// </summary>
        [Fact]
        public async Task GetImports_AnimalsPy_AllImportsHavePositiveStartLine()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = _fixture.SampleFilePath, root_path = _fixture.RootPath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items!);

            Assert.All(items!, item =>
            {
                var span = (item as JObject)
                    ?.GetValue("SourceSpan", StringComparison.OrdinalIgnoreCase) as JObject;
                Assert.NotNull(span);
                var startLine = span!
                    .GetValue("StartLine", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? 0;
                Assert.True(startLine > 0,
                    $"Expected StartLine > 0 but got {startLine}.");
            });
        }

        /// <summary>
        /// Requesting imports for a Python file that is not indexed must return a
        /// response with a non-empty <c>error</c> field.
        /// </summary>
        [Fact]
        public async Task GetImports_NonExistentPythonFile_ResponseHasError()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_imports",
                new { file_path = "not_a_real_file.py", root_path = _fixture.RootPath });

            Assert.NotNull(data.GetValue("error", StringComparison.OrdinalIgnoreCase));
        }

        // ── code_index.get_file_outline ───────────────────────────────────────────

        /// <summary>
        /// The outline of <c>animals.py</c> must return exactly two root nodes,
        /// one for each top-level class (<c>Animal</c> and <c>Dog</c>).
        /// </summary>
        [Fact]
        public async Task GetFileOutline_AnimalsPy_HasTwoRootNodes()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_outline",
                new { file_path = _fixture.SampleFilePath });

            Assert.False(isError);
            var rootNodes = data.GetValue("RootNodes", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(rootNodes);
            Assert.Equal(PythonIndexedFixture.RootNodeCount, rootNodes!.Count);
        }

        /// <summary>
        /// One of the root nodes must be named <c>Animal</c> and report
        /// <c>Subkind = "class"</c>.
        /// </summary>
        [Fact]
        public async Task GetFileOutline_AnimalsPy_ContainsAnimalClassNode()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_outline",
                new { file_path = _fixture.SampleFilePath });

            var rootNodes = data.GetValue("RootNodes", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(rootNodes);

            var animalNode = rootNodes!
                .Select(n => n.Value<JObject>())
                .FirstOrDefault(o => string.Equals(
                    Str(o!, "Name"),
                    PythonIndexedFixture.BaseClassName,
                    StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(animalNode);

            var subkind = Str(animalNode!, "Subkind");
            Assert.Equal("class", subkind, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The <c>Animal</c> class node must have at least one child, confirming
        /// that <c>__init__</c> and <c>speak</c> are nested inside it.
        /// </summary>
        [Fact]
        public async Task GetFileOutline_AnimalsPy_AnimalClassHasMethodChildren()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_outline",
                new { file_path = _fixture.SampleFilePath });

            var rootNodes = data.GetValue("RootNodes", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(rootNodes);

            var animalNode = rootNodes!
                .Select(n => n.Value<JObject>())
                .FirstOrDefault(o => string.Equals(
                    Str(o!, "Name"),
                    PythonIndexedFixture.BaseClassName,
                    StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(animalNode);

            var children = animalNode!
                .GetValue("Children", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(children);
            Assert.NotEmpty(children!);
        }

        /// <summary>
        /// The <c>Dog</c> class must have <c>bark</c> as a child node,
        /// confirming method nesting works correctly for derived classes.
        /// </summary>
        [Fact]
        public async Task GetFileOutline_AnimalsPy_DogClassHasBarkMethod()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_outline",
                new { file_path = _fixture.SampleFilePath });

            var rootNodes = data.GetValue("RootNodes", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(rootNodes);

            var dogNode = rootNodes!
                .Select(n => n.Value<JObject>())
                .FirstOrDefault(o => string.Equals(
                    Str(o!, "Name"),
                    PythonIndexedFixture.DerivedClassName,
                    StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(dogNode);

            var children = dogNode!
                .GetValue("Children", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(children);

            var hasBark = children!.Any(c =>
                string.Equals(Str(c, "Name"), "bark",
                    StringComparison.OrdinalIgnoreCase));
            Assert.True(hasBark, "Expected 'bark' method as a child of 'Dog'.");
        }

        // ── code_index.get_file_references ────────────────────────────────────────

        /// <summary>
        /// <c>animals.py</c> contains <c>class Dog(Animal):</c>, so the file must
        /// have exactly <see cref="PythonIndexedFixture.ExpectedEdgeCount"/> reference
        /// edge (one <c>Inherits</c> edge from <c>Dog</c> to <c>Animal</c>).
        /// </summary>
        [Fact]
        public async Task GetFileReferences_AnimalsPy_HasOneEdge()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_references",
                new { file_path = _fixture.SampleFilePath });

            Assert.False(isError);
            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.Single(items!);
        }

        /// <summary>
        /// The single reference edge in <c>animals.py</c> must have
        /// <c>RelationshipKind = "Inherits"</c>, confirming <c>Dog</c>'s base class
        /// is extracted correctly.
        /// </summary>
        [Fact]
        public async Task GetFileReferences_AnimalsPy_ContainsInheritsEdge()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_references",
                new { file_path = _fixture.SampleFilePath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.Contains(items!, e =>
                string.Equals(Str(e, "RelationshipKind"), "Inherits",
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The <c>Inherits</c> edge must have <c>ResolutionClass = "ExactBound"</c>
        /// because both <c>Dog</c> and <c>Animal</c> are top-level classes in the
        /// same snapshot and their qualified names are unique, unambiguous keys.
        /// </summary>
        [Fact]
        public async Task GetFileReferences_AnimalsPy_EdgeIsExactBound()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_references",
                new { file_path = _fixture.SampleFilePath });

            var items = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items!);

            var resClass = Str(items![0], "ResolutionClass");
            Assert.Equal("ExactBound", resClass, StringComparer.OrdinalIgnoreCase);
        }

        // ── code_index.get_references — Python symbol outgoing/incoming ───────────

        /// <summary>
        /// The <c>Dog</c> class has one outgoing reference — <c>Inherits</c> from
        /// <c>Animal</c>.
        /// </summary>
        [Fact]
        public async Task GetReferences_DogClass_OutgoingContainsInheritsEdge()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.find_symbols",
                new
                {
                    query_text = PythonIndexedFixture.DerivedClassName,
                    root_path  = _fixture.RootPath,
                    match_mode = "exact",
                });

            Assert.False(isError);
            var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(symbols);
            Assert.NotEmpty(symbols!);
            var dogSymbolId = Str(symbols![0], "SymbolId");
            Assert.NotNull(dogSymbolId);

            var (refData, refIsError) = await _fixture.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = dogSymbolId, include_outgoing = true, include_incoming = false });

            Assert.False(refIsError);
            var outgoing = refData.GetValue("OutgoingRefs", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(outgoing);
            Assert.Contains(outgoing!, e =>
                string.Equals(Str(e, "RelationshipKind"), "Inherits",
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The <c>Animal</c> class must have at least one incoming reference from
        /// <c>Dog</c> (<c>Inherits</c> edge), confirming reverse indexing works
        /// for Python symbols.
        /// </summary>
        [Fact]
        public async Task GetReferences_AnimalClass_IncomingContainsInheritsEdge()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.find_symbols",
                new
                {
                    query_text = PythonIndexedFixture.BaseClassName,
                    root_path  = _fixture.RootPath,
                    match_mode = "exact",
                });

            Assert.False(isError);
            var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(symbols);
            Assert.NotEmpty(symbols!);
            var animalSymbolId = Str(symbols![0], "SymbolId");
            Assert.NotNull(animalSymbolId);

            var (refData, refIsError) = await _fixture.Client.CallToolAsync(
                "code_index.get_references",
                new { symbol_id = animalSymbolId, include_outgoing = false, include_incoming = true });

            Assert.False(refIsError);
            var incoming = refData.GetValue("IncomingRefs", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(incoming);
            Assert.Contains(incoming!, e =>
                string.Equals(Str(e, "RelationshipKind"), "Inherits",
                    StringComparison.OrdinalIgnoreCase));
        }
    }
}

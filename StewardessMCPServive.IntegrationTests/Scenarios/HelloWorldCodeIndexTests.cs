using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StewardessMCPServive.IntegrationTests.Helpers;
using Xunit;

namespace StewardessMCPServive.IntegrationTests.Scenarios
{
    /// <summary>
    /// Integration tests that exercise every Phase 1 and Phase 2 code-index MCP
    /// tool against a snapshot built from the canonical Hello World
    /// <c>Program.cs</c> file.
    ///
    /// <para>
    /// The file content (defined in <see cref="HelloWorldIndexedFixture.ProgramCsContent"/>)
    /// is intentionally minimal so that expected values — class names, method names,
    /// and source line numbers — are fixed and can be asserted exactly:
    /// <code>
    /// Line 1: using System;
    /// Line 2: (empty)
    /// Line 3: class Program
    /// Line 4: {
    /// Line 5:     static void Main()
    /// Line 6:     {
    /// Line 7:         Console.WriteLine("Hello, World!");
    /// Line 8:     }
    /// Line 9: }
    /// </code>
    /// </para>
    ///
    /// <para>
    /// Because the file has no namespace, it also verifies the behaviour of
    /// <c>get_namespace_tree</c> when the repository contains only global-scope
    /// declarations.
    /// </para>
    /// </summary>
    [Collection(IntegrationTestCollection.Name)]
    public sealed class HelloWorldCodeIndexTests : IClassFixture<HelloWorldIndexedFixture>
    {
        private readonly HelloWorldIndexedFixture _fixture;

        /// <summary>Receives the shared fixture injected by xUnit.</summary>
        public HelloWorldCodeIndexTests(HelloWorldIndexedFixture fixture)
        {
            _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the first exact-match symbol ID for <paramref name="name"/>
        /// using <c>code_index.find_symbols</c>.
        /// </summary>
        private async Task<string> GetSymbolIdAsync(string name)
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.find_symbols",
                new
                {
                    query_text = name,
                    root_path  = _fixture.RootPath,
                    match_mode = "exact",
                });

            var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(symbols);
            Assert.NotEmpty(symbols);

            return symbols![0]
                .Value<JObject>()!
                .GetValue("SymbolId", StringComparison.OrdinalIgnoreCase)!
                .Value<string>()!;
        }

        // ── code_index.list_files ─────────────────────────────────────────────────

        /// <summary>
        /// After building the index the file list must contain exactly one file —
        /// <c>Program.cs</c> — with language <c>csharp</c>.
        /// </summary>
        [Fact]
        public async Task ListFiles_AfterBuild_ReturnsSingleCSharpFile()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.list_files",
                new { root_path = _fixture.RootPath });

            Assert.False(isError);

            var files = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(files);
            Assert.Single(files!);

            var file = files![0].Value<JObject>()!;
            var lang = file.GetValue("LanguageId", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.Equal("csharp", lang, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The single listed file must have a path that ends with <c>Program.cs</c>.
        /// </summary>
        [Fact]
        public async Task ListFiles_AfterBuild_FilenameIsProgramCs()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.list_files",
                new { root_path = _fixture.RootPath });

            var files = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(files);
            Assert.Single(files!);

            var path = files![0].Value<JObject>()!
                .GetValue("Path", StringComparison.OrdinalIgnoreCase)
                ?.Value<string>() ?? "";
            Assert.True(
                path.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase),
                $"Expected path ending in 'Program.cs', got '{path}'");
        }

        // ── code_index.get_file_outline ───────────────────────────────────────────

        /// <summary>
        /// The outline's top-level root nodes must contain exactly one node named
        /// <c>Program</c> (no wrapping namespace node).
        /// </summary>
        [Fact]
        public async Task GetFileOutline_ProgramCs_HasProgramClassAtRootLevel()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_outline",
                new { file_path = _fixture.SampleFilePath });

            Assert.False(isError);
            var rootNodes = data.GetValue("RootNodes", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(rootNodes);
            Assert.NotEmpty(rootNodes!);

            var hasProgram = rootNodes!.Any(n =>
                string.Equals(
                    n.Value<JObject>()?.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                    HelloWorldIndexedFixture.ClassName,
                    StringComparison.OrdinalIgnoreCase));

            Assert.True(hasProgram, "Expected a root node named 'Program' with no namespace wrapper.");
        }

        /// <summary>
        /// The <c>Program</c> class outline node must have a child named <c>Main</c>,
        /// confirming the method is correctly nested inside the class.
        /// </summary>
        [Fact]
        public async Task GetFileOutline_ProgramCs_MainIsChildOfProgramClass()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_outline",
                new { file_path = _fixture.SampleFilePath });

            var rootNodes = data.GetValue("RootNodes", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(rootNodes);

            JObject? programNode = null;
            foreach (var node in rootNodes!)
            {
                var name = node.Value<JObject>()
                    ?.GetValue("Name", StringComparison.OrdinalIgnoreCase)
                    ?.Value<string>() ?? "";
                if (string.Equals(name, HelloWorldIndexedFixture.ClassName, StringComparison.OrdinalIgnoreCase))
                {
                    programNode = node.Value<JObject>();
                    break;
                }
            }
            Assert.NotNull(programNode);

            var children = programNode!.GetValue("Children", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(children);
            Assert.NotEmpty(children!);

            var hasMain = children!.Any(c =>
                string.Equals(
                    c.Value<JObject>()?.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                    HelloWorldIndexedFixture.MethodName,
                    StringComparison.OrdinalIgnoreCase));
            Assert.True(hasMain, "Expected a 'Main' child inside the 'Program' outline node.");
        }

        /// <summary>
        /// The <c>Program</c> class node's source span must begin at line 3
        /// (the <c>class Program</c> declaration).
        /// </summary>
        [Fact]
        public async Task GetFileOutline_ProgramCs_ProgramClassStartsAtLine3()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_outline",
                new { file_path = _fixture.SampleFilePath, include_spans = true });

            var rootNodes = data.GetValue("RootNodes", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(rootNodes);

            JObject? programNode = rootNodes!
                .Select(n => n.Value<JObject>())
                .FirstOrDefault(o => string.Equals(
                    o?.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                    HelloWorldIndexedFixture.ClassName,
                    StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(programNode);

            var span   = programNode!.GetValue("SourceSpan", StringComparison.OrdinalIgnoreCase) as JObject;
            Assert.NotNull(span);
            var startLine = span!.GetValue("StartLine", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? 0;
            Assert.Equal(HelloWorldIndexedFixture.ClassStartLine, startLine);
        }

        /// <summary>
        /// The <c>Main</c> method node's source span must begin at line 5
        /// (the <c>static void Main()</c> declaration).
        /// </summary>
        [Fact]
        public async Task GetFileOutline_ProgramCs_MainMethodStartsAtLine5()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_file_outline",
                new { file_path = _fixture.SampleFilePath, include_spans = true });

            var rootNodes = data.GetValue("RootNodes", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(rootNodes);

            // Locate Program → children → Main
            JObject? programNode = rootNodes!
                .Select(n => n.Value<JObject>())
                .FirstOrDefault(o => string.Equals(
                    o?.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                    HelloWorldIndexedFixture.ClassName,
                    StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(programNode);

            var children = programNode!.GetValue("Children", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(children);

            JObject? mainNode = children!
                .Select(c => c.Value<JObject>())
                .FirstOrDefault(o => string.Equals(
                    o?.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                    HelloWorldIndexedFixture.MethodName,
                    StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(mainNode);

            var span      = mainNode!.GetValue("SourceSpan", StringComparison.OrdinalIgnoreCase) as JObject;
            Assert.NotNull(span);
            var startLine = span!.GetValue("StartLine", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? 0;
            Assert.Equal(HelloWorldIndexedFixture.MethodStartLine, startLine);
        }

        // ── code_index.get_snapshot_info ──────────────────────────────────────────

        /// <summary>
        /// Snapshot metadata must report exactly one indexed file (only
        /// <c>Program.cs</c> was written to the repo).
        /// </summary>
        [Fact]
        public async Task GetSnapshotInfo_AfterBuild_ReportsExactlyOneFile()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_snapshot_info", new { root_path = _fixture.RootPath });

            Assert.False(isError);
            var fileCount = data.GetValue("FileCount", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? -1;
            Assert.Equal(1, fileCount);
        }

        /// <summary>
        /// Snapshot metadata must report the language breakdown as containing a
        /// <c>csharp</c> entry.
        /// </summary>
        [Fact]
        public async Task GetSnapshotInfo_AfterBuild_LanguageBreakdownIncludesCSharp()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_snapshot_info", new { root_path = _fixture.RootPath });

            var breakdown = data.GetValue("LanguageBreakdown", StringComparison.OrdinalIgnoreCase) as JObject;
            Assert.NotNull(breakdown);

            var csharpCount = breakdown!.GetValue("csharp", StringComparison.OrdinalIgnoreCase)?.Value<int>();
            Assert.True(csharpCount.HasValue && csharpCount.Value >= 1,
                $"Expected LanguageBreakdown[csharp] >= 1, got {csharpCount}");
        }

        // ── code_index.find_symbols ───────────────────────────────────────────────

        /// <summary>
        /// An exact-match search for "Program" must return exactly the
        /// <c>Program</c> class with kind <c>Class</c>.
        /// </summary>
        [Fact]
        public async Task FindSymbols_ExactProgram_ReturnsProgramClass()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.find_symbols",
                new { query_text = HelloWorldIndexedFixture.ClassName, root_path = _fixture.RootPath, match_mode = "exact" });

            Assert.False(isError);
            var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(symbols);
            Assert.NotEmpty(symbols!);

            var first = symbols![0].Value<JObject>()!;
            Assert.Equal(HelloWorldIndexedFixture.ClassName,
                first.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>());
            Assert.Equal("Class",
                first.GetValue("Kind", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// An exact-match search for "Main" must return the <c>Main</c> method
        /// with kind <c>Method</c>.
        /// </summary>
        [Fact]
        public async Task FindSymbols_ExactMain_ReturnsMainMethod()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.find_symbols",
                new { query_text = HelloWorldIndexedFixture.MethodName, root_path = _fixture.RootPath, match_mode = "exact" });

            Assert.False(isError);
            var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(symbols);
            Assert.NotEmpty(symbols!);

            var first = symbols![0].Value<JObject>()!;
            Assert.Equal(HelloWorldIndexedFixture.MethodName,
                first.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>());
            Assert.Equal("Method",
                first.GetValue("Kind", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Filtering by <c>kind=Class</c> must return only the <c>Program</c>
        /// class and never the <c>Main</c> method.
        /// </summary>
        [Fact]
        public async Task FindSymbols_WithClassKindFilter_ReturnsOnlyClasses()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.find_symbols",
                new { query_text = "", root_path = _fixture.RootPath, kind = "Class" });

            var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(symbols);
            Assert.NotEmpty(symbols!);
            Assert.All(symbols!, s =>
            {
                var kind = s.Value<JObject>()
                    ?.GetValue("Kind", StringComparison.OrdinalIgnoreCase)
                    ?.Value<string>();
                Assert.Equal("Class", kind, StringComparer.OrdinalIgnoreCase);
            });
        }

        /// <summary>
        /// A prefix search on "Pro" must include the <c>Program</c> class.
        /// </summary>
        [Fact]
        public async Task FindSymbols_PrefixPro_IncludesProgramClass()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.find_symbols",
                new { query_text = "Pro", root_path = _fixture.RootPath, match_mode = "prefix" });

            var symbols = data.GetValue("Items", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(symbols);
            Assert.NotEmpty(symbols!);

            var hasProgram = symbols!.Any(s =>
                string.Equals(
                    s.Value<JObject>()?.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                    HelloWorldIndexedFixture.ClassName,
                    StringComparison.OrdinalIgnoreCase));
            Assert.True(hasProgram, "Expected prefix 'Pro' to match 'Program'.");
        }

        // ── code_index.get_symbol ─────────────────────────────────────────────────

        /// <summary>
        /// Fetching the <c>Program</c> class by symbol ID must return
        /// <c>Symbol.Name = "Program"</c> and <c>Symbol.Kind = "Class"</c>.
        /// </summary>
        [Fact]
        public async Task GetSymbol_ProgramClass_ReturnsCorrectNameAndKind()
        {
            var symbolId = await GetSymbolIdAsync(HelloWorldIndexedFixture.ClassName);

            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_symbol", new { symbol_id = symbolId });

            Assert.False(isError);
            var error = data.GetValue("error", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.Null(error);

            var symbol = data.GetValue("Symbol", StringComparison.OrdinalIgnoreCase) as JObject;
            Assert.NotNull(symbol);
            Assert.Equal(HelloWorldIndexedFixture.ClassName,
                symbol!.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>());
            Assert.Equal("Class",
                symbol.GetValue("Kind", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Fetching the <c>Main</c> method by symbol ID must return
        /// <c>Symbol.Name = "Main"</c> and <c>Symbol.Kind = "Method"</c>.
        /// </summary>
        [Fact]
        public async Task GetSymbol_MainMethod_ReturnsCorrectNameAndKind()
        {
            var symbolId = await GetSymbolIdAsync(HelloWorldIndexedFixture.MethodName);

            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_symbol", new { symbol_id = symbolId });

            var symbol = data.GetValue("Symbol", StringComparison.OrdinalIgnoreCase) as JObject;
            Assert.NotNull(symbol);
            Assert.Equal(HelloWorldIndexedFixture.MethodName,
                symbol!.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>());
            Assert.Equal("Method",
                symbol.GetValue("Kind", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Fetching <c>Program</c> with <c>include_primary_occurrence=true</c>
        /// must return a <c>PrimaryOccurrence</c> whose <c>FilePath</c> points
        /// to <c>Program.cs</c>.
        /// </summary>
        [Fact]
        public async Task GetSymbol_ProgramClass_PrimaryOccurrencePointsToProgramCs()
        {
            var symbolId = await GetSymbolIdAsync(HelloWorldIndexedFixture.ClassName);

            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_symbol",
                new { symbol_id = symbolId, include_primary_occurrence = true });

            var primaryOcc = data.GetValue("PrimaryOccurrence", StringComparison.OrdinalIgnoreCase) as JObject;
            Assert.NotNull(primaryOcc);

            var filePath = primaryOcc!.GetValue("FilePath", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.NotNull(filePath);
            Assert.True(
                filePath!.EndsWith(HelloWorldIndexedFixture.ProgramCsFileName, StringComparison.OrdinalIgnoreCase),
                $"Expected PrimaryOccurrence.FilePath to end with 'Program.cs', got '{filePath}'");
        }

        // ── code_index.get_symbol_occurrences ─────────────────────────────────────

        /// <summary>
        /// The occurrences of the <c>Program</c> class must include at least one
        /// <c>Declaration</c> role occurrence.
        /// </summary>
        [Fact]
        public async Task GetSymbolOccurrences_ProgramClass_HasDeclarationOccurrence()
        {
            var symbolId = await GetSymbolIdAsync(HelloWorldIndexedFixture.ClassName);

            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_symbol_occurrences", new { symbol_id = symbolId });

            Assert.False(isError);
            var occurrences = data.GetValue("Occurrences", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(occurrences);
            Assert.NotEmpty(occurrences!);

            var hasDeclaration = occurrences!.Any(o =>
                string.Equals(
                    o.Value<JObject>()?.GetValue("Role", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                    "Declaration",
                    StringComparison.OrdinalIgnoreCase));
            Assert.True(hasDeclaration, "Expected at least one occurrence with Role = 'Declaration'.");
        }

        /// <summary>
        /// Every occurrence returned for <c>Program</c> must have a non-empty
        /// <c>FilePath</c> that ends with <c>Program.cs</c>.
        /// </summary>
        [Fact]
        public async Task GetSymbolOccurrences_ProgramClass_OccurrencesPointToProgramCs()
        {
            var symbolId = await GetSymbolIdAsync(HelloWorldIndexedFixture.ClassName);

            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_symbol_occurrences", new { symbol_id = symbolId });

            var occurrences = data.GetValue("Occurrences", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(occurrences);
            Assert.All(occurrences!, occ =>
            {
                var filePath = occ.Value<JObject>()
                    ?.GetValue("FilePath", StringComparison.OrdinalIgnoreCase)
                    ?.Value<string>();
                Assert.NotNull(filePath);
                Assert.True(
                    filePath!.EndsWith(HelloWorldIndexedFixture.ProgramCsFileName, StringComparison.OrdinalIgnoreCase),
                    $"Expected FilePath ending in 'Program.cs', got '{filePath}'");
            });
        }

        // ── code_index.get_symbol_children ────────────────────────────────────────

        /// <summary>
        /// The children of the <c>Program</c> class must include the
        /// <c>Main</c> method.
        /// </summary>
        [Fact]
        public async Task GetSymbolChildren_ProgramClass_ContainsMainMethod()
        {
            var symbolId = await GetSymbolIdAsync(HelloWorldIndexedFixture.ClassName);

            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_symbol_children", new { symbol_id = symbolId });

            Assert.False(isError);
            var children = data.GetValue("Children", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(children);
            Assert.NotEmpty(children!);

            var hasMain = children!.Any(c =>
                string.Equals(
                    c.Value<JObject>()?.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                    HelloWorldIndexedFixture.MethodName,
                    StringComparison.OrdinalIgnoreCase));
            Assert.True(hasMain, "Expected 'Main' to appear as a child of 'Program'.");
        }

        /// <summary>
        /// When filtering children by <c>kind=Method</c>, the result must include
        /// <c>Main</c> and every returned child must have kind <c>Method</c>.
        /// </summary>
        [Fact]
        public async Task GetSymbolChildren_ProgramClass_WithMethodFilter_ReturnsMainWithMethodKind()
        {
            var symbolId = await GetSymbolIdAsync(HelloWorldIndexedFixture.ClassName);

            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_symbol_children",
                new { symbol_id = symbolId, kind = "Method" });

            var children = data.GetValue("Children", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(children);
            Assert.NotEmpty(children!);

            Assert.All(children!, c =>
            {
                var kind = c.Value<JObject>()
                    ?.GetValue("Kind", StringComparison.OrdinalIgnoreCase)
                    ?.Value<string>();
                Assert.Equal("Method", kind, StringComparer.OrdinalIgnoreCase);
            });

            var hasMain = children!.Any(c =>
                string.Equals(
                    c.Value<JObject>()?.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                    HelloWorldIndexedFixture.MethodName,
                    StringComparison.OrdinalIgnoreCase));
            Assert.True(hasMain, "Expected 'Main' in method-filtered children of 'Program'.");
        }

        // ── code_index.get_type_members ───────────────────────────────────────────

        /// <summary>
        /// The type members of <c>Program</c> must include exactly the <c>Main</c>
        /// method in the <c>Methods</c> array.
        /// </summary>
        [Fact]
        public async Task GetTypeMembers_ProgramClass_HasMainInMethods()
        {
            var symbolId = await GetSymbolIdAsync(HelloWorldIndexedFixture.ClassName);

            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_type_members", new { type_symbol_id = symbolId });

            Assert.False(isError);
            var error = data.GetValue("error", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.Null(error);

            var methods = data.GetValue("Methods", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(methods);
            Assert.NotEmpty(methods!);

            var hasMain = methods!.Any(m =>
                string.Equals(
                    m.Value<JObject>()?.GetValue("Name", StringComparison.OrdinalIgnoreCase)?.Value<string>(),
                    HelloWorldIndexedFixture.MethodName,
                    StringComparison.OrdinalIgnoreCase));
            Assert.True(hasMain, "Expected 'Main' in the Methods array for 'Program'.");
        }

        /// <summary>
        /// The <c>Program</c> class has no declared properties, so the
        /// <c>Properties</c> array must be empty.
        /// </summary>
        [Fact]
        public async Task GetTypeMembers_ProgramClass_HasNoProperties()
        {
            var symbolId = await GetSymbolIdAsync(HelloWorldIndexedFixture.ClassName);

            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_type_members", new { type_symbol_id = symbolId });

            var propCount = (data.GetValue("Properties", StringComparison.OrdinalIgnoreCase) as JArray)?.Count ?? -1;
            Assert.Equal(0, propCount);
        }

        /// <summary>
        /// The <c>Program</c> class has no explicit constructor, so the
        /// <c>Constructors</c> array must be empty.
        /// </summary>
        [Fact]
        public async Task GetTypeMembers_ProgramClass_HasNoExplicitConstructors()
        {
            var symbolId = await GetSymbolIdAsync(HelloWorldIndexedFixture.ClassName);

            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_type_members", new { type_symbol_id = symbolId });

            var ctorCount = (data.GetValue("Constructors", StringComparison.OrdinalIgnoreCase) as JArray)?.Count ?? -1;
            Assert.Equal(0, ctorCount);
        }

        // ── code_index.resolve_location ───────────────────────────────────────────

        /// <summary>
        /// Resolving the <c>Program</c> class symbol must return
        /// <c>FilePath</c> ending with <c>Program.cs</c> and
        /// <c>SourceSpan.StartLine = 3</c>.
        /// </summary>
        [Fact]
        public async Task ResolveLocation_ProgramClass_ReturnsLine3InProgramCs()
        {
            var symbolId = await GetSymbolIdAsync(HelloWorldIndexedFixture.ClassName);

            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.resolve_location", new { symbol_id = symbolId });

            Assert.False(isError);
            var error = data.GetValue("error", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.Null(error);

            var filePath = data.GetValue("FilePath", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.NotNull(filePath);
            Assert.True(
                filePath!.EndsWith(HelloWorldIndexedFixture.ProgramCsFileName, StringComparison.OrdinalIgnoreCase),
                $"Expected FilePath ending in 'Program.cs', got '{filePath}'");

            var span      = data.GetValue("SourceSpan", StringComparison.OrdinalIgnoreCase) as JObject;
            Assert.NotNull(span);
            var startLine = span!.GetValue("StartLine", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? 0;
            Assert.Equal(HelloWorldIndexedFixture.ClassStartLine, startLine);
        }

        /// <summary>
        /// Resolving the <c>Main</c> method symbol must return
        /// <c>FilePath</c> ending with <c>Program.cs</c> and
        /// <c>SourceSpan.StartLine = 5</c>.
        /// </summary>
        [Fact]
        public async Task ResolveLocation_MainMethod_ReturnsLine5InProgramCs()
        {
            var symbolId = await GetSymbolIdAsync(HelloWorldIndexedFixture.MethodName);

            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.resolve_location", new { symbol_id = symbolId });

            Assert.False(isError);
            var error = data.GetValue("error", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.Null(error);

            var filePath = data.GetValue("FilePath", StringComparison.OrdinalIgnoreCase)?.Value<string>();
            Assert.True(
                filePath?.EndsWith(HelloWorldIndexedFixture.ProgramCsFileName, StringComparison.OrdinalIgnoreCase) == true,
                $"Expected FilePath ending in 'Program.cs', got '{filePath}'");

            var span      = data.GetValue("SourceSpan", StringComparison.OrdinalIgnoreCase) as JObject;
            Assert.NotNull(span);
            var startLine = span!.GetValue("StartLine", StringComparison.OrdinalIgnoreCase)?.Value<int>() ?? 0;
            Assert.Equal(HelloWorldIndexedFixture.MethodStartLine, startLine);
        }

        /// <summary>
        /// Calling <c>resolve_location</c> without any IDs must return an error
        /// object, not throw.
        /// </summary>
        [Fact]
        public async Task ResolveLocation_WithNoArguments_ReturnsErrorObject()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.resolve_location", new { });

            Assert.NotNull(data.GetValue("error", StringComparison.OrdinalIgnoreCase));
        }

        // ── code_index.get_namespace_tree ─────────────────────────────────────────

        /// <summary>
        /// <c>Program.cs</c> declares no namespaces, so <c>get_namespace_tree</c>
        /// must return an empty <c>Roots</c> array rather than wrapping
        /// <c>Program</c> in a synthetic namespace node.
        /// </summary>
        [Fact]
        public async Task GetNamespaceTree_HelloWorld_ReturnsEmptyRoots()
        {
            var (data, isError) = await _fixture.Client.CallToolAsync(
                "code_index.get_namespace_tree",
                new { root_path = _fixture.RootPath });

            Assert.False(isError);
            var roots = data.GetValue("Roots", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(roots);
            Assert.Empty(roots!);
        }

        /// <summary>
        /// Filtering the namespace tree by language <c>csharp</c> must also
        /// return empty roots for the Hello World repository.
        /// </summary>
        [Fact]
        public async Task GetNamespaceTree_WithCSharpFilter_ReturnsEmptyRoots()
        {
            var (data, _) = await _fixture.Client.CallToolAsync(
                "code_index.get_namespace_tree",
                new { root_path = _fixture.RootPath, language = "csharp" });

            var roots = data.GetValue("Roots", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(roots);
            Assert.Empty(roots!);
        }
    }
}

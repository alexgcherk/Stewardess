using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StewardessMCPService.Configuration;
using StewardessMCPService.Infrastructure;
using StewardessMCPService.Mcp;
using StewardessMCPService.Models;
using StewardessMCPService.Services;
using StewardessMCPService.Tests.Helpers;
using Xunit;

namespace StewardessMCPService.Tests.Mcp
{
    /// <summary>
    /// Unit tests for the <c>repo_browser.*</c> MCP tool group.
    ///
    /// Covers:
    /// <list type="bullet">
    ///   <item>Schema correctness — required parameters, enum constraints, defaults, category.</item>
    ///   <item>Annotation correctness — SideEffectClass, UsageGuidance.</item>
    ///   <item>Tool invocation — print_tree, grep, read_file, find_path return expected data.</item>
    /// </list>
    /// </summary>
    public sealed class RepoBrowserToolTests : IDisposable
    {
        // ── Tool names ───────────────────────────────────────────────────────────

        private const string PrintTree = "repo_browser.print_tree";
        private const string Grep      = "repo_browser.grep";
        private const string ReadFile  = "repo_browser.read_file";
        private const string FindPath  = "repo_browser.find_path";
        private const string Search    = "repo_browser.search";

        private static readonly string[] AllRepoBrowserTools = { PrintTree, Grep, ReadFile, FindPath, Search };

        // ── Fixture ──────────────────────────────────────────────────────────────

        private readonly TempRepository  _repo;
        private readonly McpToolRegistry _registry;

        public RepoBrowserToolTests()
        {
            _repo = new TempRepository();
            _repo.CreateSampleCsStructure();

            // Add a few extra files for grep and find_path tests
            _repo.CreateFile(@"src\MyLib\ServiceImpl.cs",
                "namespace MyLib\r\n{\r\n    public class ServiceImpl : IService\r\n    {\r\n        public void Execute() { }\r\n    }\r\n}");
            _repo.CreateDirectory("docs");
            _repo.CreateFile(@"docs\README.md", "# MyLib\r\nA sample library.");
            _repo.CreateFile(@"docs\CHANGELOG.md", "## v1.0\r\n- Initial release");

            var settings  = McpServiceSettings.CreateForTesting(_repo.Root);
            var validator = new PathValidator(settings);
            var audit     = new AuditService(settings);
            var security  = new SecurityService(settings, validator);

            var fileSvc   = new FileSystemService(settings, validator, audit);
            var searchSvc = new SearchService(settings, validator);
            var editSvc   = new EditService(settings, validator, security, audit);
            var gitSvc    = new GitService(settings, validator);
            var cmdSvc    = new CommandService(settings, validator, audit);

            _registry = new McpToolRegistry(settings, fileSvc, searchSvc, editSvc, gitSvc, cmdSvc);
        }

        public void Dispose() => _repo.Dispose();

        // ═════════════════════════════════════════════════════════════════════════
        // Schema tests
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>All four repo_browser tools must be registered in the registry.</summary>
        [Fact]
        public void AllRepoBrowserTools_AreRegistered()
        {
            var registered = _registry.GetAllDefinitions()
                                      .Select(d => d.Name)
                                      .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var name in AllRepoBrowserTools)
                Assert.True(registered.Contains(name), $"Tool '{name}' was not found in the registry.");
        }

        /// <summary>All four tools must declare category = "repo_browser".</summary>
        [Fact]
        public void AllRepoBrowserTools_HaveCorrectCategory()
        {
            foreach (var name in AllRepoBrowserTools)
            {
                var tool = GetTool(name);
                Assert.Equal("repo_browser", tool.Category, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>All four tools must be read-only with low risk.</summary>
        [Fact]
        public void AllRepoBrowserTools_AreSideEffectReadOnlyAndLowRisk()
        {
            foreach (var name in AllRepoBrowserTools)
            {
                var tool = GetTool(name);
                Assert.Equal("read-only", tool.SideEffectClass, StringComparer.OrdinalIgnoreCase);
                Assert.Equal("low",       tool.RiskLevel,       StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>All four tools must carry UsageGuidance with a non-empty UseWhen.</summary>
        [Fact]
        public void AllRepoBrowserTools_HaveUsageGuidanceWithUseWhen()
        {
            foreach (var name in AllRepoBrowserTools)
            {
                var tool = GetTool(name);
                Assert.NotNull(tool.UsageGuidance);
                Assert.False(string.IsNullOrWhiteSpace(tool.UsageGuidance.UseWhen),
                    $"Tool '{name}' UsageGuidance.UseWhen is empty.");
            }
        }

        /// <summary>All four tools must advertise at least one typical next tool.</summary>
        [Fact]
        public void AllRepoBrowserTools_HaveTypicalNextTools()
        {
            foreach (var name in AllRepoBrowserTools)
            {
                var tool = GetTool(name);
                Assert.NotNull(tool.UsageGuidance?.TypicalNextTools);
                Assert.NotEmpty(tool.UsageGuidance.TypicalNextTools);
            }
        }

        // ── print_tree schema ────────────────────────────────────────────────────

        /// <summary>print_tree.max_depth must default to 4.</summary>
        [Fact]
        public void PrintTree_MaxDepth_DefaultsTo4()
        {
            var prop = GetProperty(PrintTree, "max_depth");
            Assert.Equal(4, Convert.ToInt32(prop.Default));
        }

        /// <summary>print_tree.max_entries must default to 1000.</summary>
        [Fact]
        public void PrintTree_MaxEntries_DefaultsTo1000()
        {
            var prop = GetProperty(PrintTree, "max_entries");
            Assert.Equal(1000, Convert.ToInt32(prop.Default));
        }

        /// <summary>print_tree.include_files and include_directories must default to true.</summary>
        [Fact]
        public void PrintTree_IncludeFilesAndDirs_DefaultToTrue()
        {
            var files = GetProperty(PrintTree, "include_files");
            var dirs  = GetProperty(PrintTree, "include_directories");
            Assert.Equal(true.ToString(), files.Default?.ToString(), ignoreCase: true);
            Assert.Equal(true.ToString(), dirs.Default?.ToString(),  ignoreCase: true);
        }

        // ── grep schema ──────────────────────────────────────────────────────────

        /// <summary>grep.query must be required.</summary>
        [Fact]
        public void Grep_Query_IsRequired()
        {
            var tool = GetTool(Grep);
            Assert.Contains("query", tool.InputSchema.Required, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>grep.mode must enumerate literal, regex, word, symbol_hint.</summary>
        [Fact]
        public void Grep_Mode_HasEnumConstraint()
        {
            var prop = GetProperty(Grep, "mode");
            Assert.NotNull(prop.Enum);
            Assert.Contains("literal",     prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("regex",       prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("word",        prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("symbol_hint", prop.Enum, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>grep.mode must default to "literal".</summary>
        [Fact]
        public void Grep_Mode_DefaultsToLiteral()
        {
            var prop = GetProperty(Grep, "mode");
            Assert.Equal("literal", prop.Default?.ToString(), StringComparer.OrdinalIgnoreCase);
        }

        // ── read_file schema ─────────────────────────────────────────────────────

        /// <summary>repo_browser.read_file.file_path must be required.</summary>
        [Fact]
        public void ReadFile_FilePath_IsRequired()
        {
            var tool = GetTool(ReadFile);
            Assert.Contains("file_path", tool.InputSchema.Required, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>read_file.max_bytes must default to 65536.</summary>
        [Fact]
        public void ReadFile_MaxBytes_DefaultsTo65536()
        {
            var prop = GetProperty(ReadFile, "max_bytes");
            Assert.Equal(65536, Convert.ToInt32(prop.Default));
        }

        /// <summary>read_file.include_line_numbers must default to true.</summary>
        [Fact]
        public void ReadFile_IncludeLineNumbers_DefaultsToTrue()
        {
            var prop = GetProperty(ReadFile, "include_line_numbers");
            Assert.Equal(true.ToString(), prop.Default?.ToString(), ignoreCase: true);
        }

        // ── find_path schema ─────────────────────────────────────────────────────

        /// <summary>find_path.query must be required.</summary>
        [Fact]
        public void FindPath_Query_IsRequired()
        {
            var tool = GetTool(FindPath);
            Assert.Contains("query", tool.InputSchema.Required, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>find_path.match_mode must enumerate name, path_fragment, exact_path, prefix.</summary>
        [Fact]
        public void FindPath_MatchMode_HasEnumConstraint()
        {
            var prop = GetProperty(FindPath, "match_mode");
            Assert.NotNull(prop.Enum);
            Assert.Contains("name",          prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("path_fragment",  prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("exact_path",     prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("prefix",         prop.Enum, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>find_path.target_kind must enumerate file, directory, any.</summary>
        [Fact]
        public void FindPath_TargetKind_HasEnumConstraint()
        {
            var prop = GetProperty(FindPath, "target_kind");
            Assert.NotNull(prop.Enum);
            Assert.Contains("file",      prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("directory", prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("any",       prop.Enum, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>find_path.match_mode must default to "name".</summary>
        [Fact]
        public void FindPath_MatchMode_DefaultsToName()
        {
            var prop = GetProperty(FindPath, "match_mode");
            Assert.Equal("name", prop.Default?.ToString(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>find_path.max_results must default to 50.</summary>
        [Fact]
        public void FindPath_MaxResults_DefaultsTo50()
        {
            var prop = GetProperty(FindPath, "max_results");
            Assert.Equal(50, Convert.ToInt32(prop.Default));
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Invocation tests
        // ═════════════════════════════════════════════════════════════════════════

        // ── print_tree invocation ────────────────────────────────────────────────

        /// <summary>print_tree with no arguments returns a non-empty items list.</summary>
        [Fact]
        public async Task PrintTree_NoArgs_ReturnsNonEmptyItems()
        {
            var result = await InvokeAsync(PrintTree);

            var items = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);
        }

        /// <summary>print_tree with relative_path="." (current dir) returns the same results as no path.</summary>
        [Fact]
        public async Task PrintTree_DotRelativePath_ReturnsSameAsNoPath()
        {
            var dotResult  = await InvokeAsync(PrintTree, new { relative_path = "." });
            var rootResult = await InvokeAsync(PrintTree);

            var dotItems  = dotResult["items"]  as JArray;
            var rootItems = rootResult["items"] as JArray;

            Assert.NotNull(dotItems);
            Assert.NotNull(rootItems);
            Assert.NotEmpty(dotItems);
            Assert.Equal(rootItems.Count, dotItems.Count);
        }

        /// <summary>print_tree returns the repository root in the response.</summary>
        [Fact]
        public async Task PrintTree_ReturnsRootPath()
        {
            var result = await InvokeAsync(PrintTree);
            Assert.NotNull(result["rootPath"]);
            Assert.False(string.IsNullOrEmpty(result["rootPath"]?.Value<string>()));
        }

        /// <summary>print_tree items each have path, name, kind, and depth fields.</summary>
        [Fact]
        public async Task PrintTree_ItemsHaveRequiredFields()
        {
            var result = await InvokeAsync(PrintTree);
            var items  = (JArray)result["items"];
            Assert.NotEmpty(items);

            foreach (var item in items)
            {
                Assert.NotNull(item["path"]);
                Assert.NotNull(item["name"]);
                Assert.Contains(item["kind"]?.Value<string>(),
                    new[] { "file", "directory" }, StringComparer.OrdinalIgnoreCase);
                Assert.True(item["depth"] != null);
            }
        }

        /// <summary>print_tree with include_files=false returns only directory items.</summary>
        [Fact]
        public async Task PrintTree_IncludeFilesFalse_ReturnsOnlyDirectories()
        {
            var result = await InvokeAsync(PrintTree, new { include_files = false });
            var items  = result["items"] as JArray ?? new JArray();

            foreach (var item in items)
                Assert.Equal("directory", item["kind"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>print_tree with include_directories=false returns only file items.</summary>
        [Fact]
        public async Task PrintTree_IncludeDirsFalse_ReturnsOnlyFiles()
        {
            var result = await InvokeAsync(PrintTree, new { include_directories = false });
            var items  = result["items"] as JArray ?? new JArray();

            Assert.NotEmpty(items);
            foreach (var item in items)
                Assert.Equal("file", item["kind"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>print_tree with max_entries=1 sets truncated=true when repo has more entries.</summary>
        [Fact]
        public async Task PrintTree_MaxEntries1_SetsTruncatedTrue()
        {
            var result = await InvokeAsync(PrintTree, new { max_entries = 1 });
            Assert.True(result["truncated"]?.Value<bool>() == true, "Expected truncated=true");
        }

        /// <summary>print_tree with max_entries=1 returns exactly 1 item.</summary>
        [Fact]
        public async Task PrintTree_MaxEntries1_ReturnsExactlyOneItem()
        {
            var result = await InvokeAsync(PrintTree, new { max_entries = 1 });
            var items  = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.Equal(1, items.Count);
        }

        /// <summary>print_tree with max_depth=1 returns no items deeper than depth 0.</summary>
        [Fact]
        public async Task PrintTree_MaxDepth1_NoItemsDeepThan0()
        {
            var result = await InvokeAsync(PrintTree, new { max_depth = 1 });
            var items  = result["items"] as JArray ?? new JArray();

            foreach (var item in items)
                Assert.Equal(0, item["depth"]?.Value<int>() ?? 0);
        }

        // ── grep invocation ──────────────────────────────────────────────────────

        /// <summary>grep for "public class" in literal mode returns matches from C# files.</summary>
        [Fact]
        public async Task Grep_LiteralMode_FindsTextInCsFiles()
        {
            var result = await InvokeAsync(Grep, new { query = "public class" });

            var items = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            // Every match must reference a .cs file
            foreach (var item in items)
            {
                var fp = item["filePath"]?.Value<string>() ?? "";
                Assert.EndsWith(".cs", fp, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>grep items contain lineNumber, lineText, filePath.</summary>
        [Fact]
        public async Task Grep_Items_HaveRequiredFields()
        {
            var result = await InvokeAsync(Grep, new { query = "namespace" });
            var items  = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            foreach (var item in items)
            {
                Assert.True(item["lineNumber"]?.Value<int>() > 0, "lineNumber must be > 0");
                Assert.False(string.IsNullOrEmpty(item["lineText"]?.Value<string>()), "lineText must not be empty");
                Assert.False(string.IsNullOrEmpty(item["filePath"]?.Value<string>()), "filePath must not be empty");
            }
        }

        /// <summary>grep response includes rootPath, query, mode, and matchCount fields.</summary>
        [Fact]
        public async Task Grep_ResponseHasTopLevelFields()
        {
            var result = await InvokeAsync(Grep, new { query = "class" });
            Assert.NotNull(result["rootPath"]);
            Assert.Equal("class",   result["query"]?.Value<string>());
            Assert.Equal("literal", result["mode"]?.Value<string>());
            Assert.True(result["matchCount"]?.Value<int>() >= 0);
        }

        /// <summary>grep with max_results=1 returns at most 1 match.</summary>
        [Fact]
        public async Task Grep_MaxResults1_ReturnsAtMostOneMatch()
        {
            var result = await InvokeAsync(Grep, new { query = "namespace", max_results = 1 });
            var items  = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.True(items.Count <= 1);
        }

        /// <summary>grep with word mode finds whole-word occurrences.</summary>
        [Fact]
        public async Task Grep_WordMode_FindsMatches()
        {
            // "class" should match as a whole word in C# files
            var result = await InvokeAsync(Grep, new { query = "class", mode = "word" });
            var items  = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);
        }

        /// <summary>grep context_lines=0 returns empty before/after context arrays.</summary>
        [Fact]
        public async Task Grep_ContextLines0_EmptyContextArrays()
        {
            var result = await InvokeAsync(Grep, new { query = "namespace", context_lines = 0 });
            var items  = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            foreach (var item in items)
            {
                var before = item["beforeContext"] as JArray;
                var after  = item["afterContext"]  as JArray;
                // With context_lines=0 the arrays should be empty (or null)
                Assert.True(before == null || before.Count == 0,
                    "beforeContext should be empty when context_lines=0");
                Assert.True(after == null || after.Count == 0,
                    "afterContext should be empty when context_lines=0");
            }
        }

        // ── read_file invocation ─────────────────────────────────────────────────

        /// <summary>read_file for an existing file returns exists=true and non-empty content.</summary>
        [Fact]
        public async Task ReadFile_ExistingFile_ReturnsContentWithExistsTrue()
        {
            var result = await InvokeAsync(ReadFile, new { file_path = "src/MyLib/Class1.cs" });

            Assert.True(result["exists"]?.Value<bool>() == true, "exists must be true");
            Assert.False(string.IsNullOrEmpty(result["content"]?.Value<string>()),
                "content must not be empty");
        }

        /// <summary>read_file with include_line_numbers=true prefixes content with "1: ".</summary>
        [Fact]
        public async Task ReadFile_WithLineNumbers_ContentStartsWithLineNumber()
        {
            var result  = await InvokeAsync(ReadFile, new { file_path = "src/MyLib/Class1.cs", include_line_numbers = true });
            var content = result["content"]?.Value<string>() ?? "";

            Assert.True(content.StartsWith("1: "), $"Content should start with '1: ' but was: {content.Substring(0, Math.Min(30, content.Length))}");
        }

        /// <summary>read_file with include_line_numbers=false returns raw content.</summary>
        [Fact]
        public async Task ReadFile_WithoutLineNumbers_ContentDoesNotStartWithLineNumber()
        {
            var result  = await InvokeAsync(ReadFile, new { file_path = "src/MyLib/Class1.cs", include_line_numbers = false });
            var content = result["content"]?.Value<string>() ?? "";

            Assert.False(content.StartsWith("1: "), "Content should not start with '1: ' when include_line_numbers=false");
        }

        /// <summary>read_file for a non-existent path returns exists=false.</summary>
        [Fact]
        public async Task ReadFile_NonExistentFile_ExistsFalse()
        {
            var result = await InvokeAsync(ReadFile, new { file_path = "does/not/exist.cs" });
            Assert.True(result["exists"]?.Value<bool>() == false, "exists must be false for a missing file");
        }

        /// <summary>read_file response always includes rootPath and filePath.</summary>
        [Fact]
        public async Task ReadFile_ResponseHasRootPathAndFilePath()
        {
            var result = await InvokeAsync(ReadFile, new { file_path = "src/MyLib/Class1.cs" });
            Assert.NotNull(result["rootPath"]);
            Assert.Equal("src/MyLib/Class1.cs", result["filePath"]?.Value<string>());
        }

        /// <summary>read_file with startLine/endLine returns only that range.</summary>
        [Fact]
        public async Task ReadFile_WithRange_ReturnsOnlyRequestedLines()
        {
            var result = await InvokeAsync(ReadFile, new
            {
                file_path  = "src/MyLib/Class1.cs",
                start_line = 1,
                end_line   = 2,
            });

            Assert.True(result["exists"]?.Value<bool>() == true);
            Assert.Equal(1, result["startLine"]?.Value<int>());
            Assert.Equal(2, result["endLine"]?.Value<int>());
        }

        // ── find_path invocation ─────────────────────────────────────────────────

        /// <summary>find_path with name mode finds a file by exact name.</summary>
        [Fact]
        public async Task FindPath_NameMode_FindsFileByName()
        {
            var result = await InvokeAsync(FindPath, new { query = "Class1.cs" });

            var items = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            var first = items[0];
            Assert.Equal("file", first["kind"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
            Assert.EndsWith("Class1.cs", first["path"]?.Value<string>() ?? "", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>find_path with name mode and a partial query (no extension) finds matching files.</summary>
        [Fact]
        public async Task FindPath_NameMode_PartialQuery_FindsMatchingFiles()
        {
            // query = "Class1" (no extension) — should find both Class1.cs and Class1Tests.cs
            var result = await InvokeAsync(FindPath, new { query = "Class1" });

            var items = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            foreach (var item in items)
            {
                var name = item["name"]?.Value<string>() ?? "";
                Assert.Contains("Class1", name, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>find_path with target_kind=directory finds directories.</summary>
        [Fact]
        public async Task FindPath_TargetKindDirectory_FindsDirectories()
        {
            var result = await InvokeAsync(FindPath, new { query = "src", target_kind = "directory" });

            var items = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            foreach (var item in items)
                Assert.Equal("directory", item["kind"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>find_path with path_fragment mode finds files containing the fragment.</summary>
        [Fact]
        public async Task FindPath_PathFragmentMode_FindsMatchingFiles()
        {
            var result = await InvokeAsync(FindPath, new { query = "MyLib", match_mode = "path_fragment" });

            var items = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            foreach (var item in items)
            {
                var path = (item["path"]?.Value<string>() ?? "").Replace('\\', '/');
                Assert.Contains("MyLib", path, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>find_path response always contains rootPath, query, matchMode, targetKind, resultCount.</summary>
        [Fact]
        public async Task FindPath_ResponseHasTopLevelFields()
        {
            var result = await InvokeAsync(FindPath, new { query = "Class1.cs" });

            Assert.NotNull(result["rootPath"]);
            Assert.Equal("Class1.cs", result["query"]?.Value<string>());
            Assert.Equal("name",      result["matchMode"]?.Value<string>(),  StringComparer.OrdinalIgnoreCase);
            Assert.Equal("any",       result["targetKind"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
            Assert.True(result["resultCount"]?.Value<int>() >= 0);
        }

        /// <summary>find_path items always have path, name, kind, and matchReason.</summary>
        [Fact]
        public async Task FindPath_Items_HaveRequiredFields()
        {
            var result = await InvokeAsync(FindPath, new { query = "cs", match_mode = "path_fragment" });
            var items  = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            foreach (var item in items)
            {
                Assert.False(string.IsNullOrEmpty(item["path"]?.Value<string>()),        "path must not be empty");
                Assert.False(string.IsNullOrEmpty(item["name"]?.Value<string>()),        "name must not be empty");
                Assert.False(string.IsNullOrEmpty(item["kind"]?.Value<string>()),        "kind must not be empty");
                Assert.False(string.IsNullOrEmpty(item["matchReason"]?.Value<string>()), "matchReason must not be empty");
            }
        }

        /// <summary>find_path with max_results=1 returns at most one result.</summary>
        [Fact]
        public async Task FindPath_MaxResults1_ReturnsAtMostOne()
        {
            var result = await InvokeAsync(FindPath, new { query = "cs", match_mode = "path_fragment", max_results = 1 });
            var items  = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.True(items.Count <= 1);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // repo_browser.search
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>search is registered in the tool registry.</summary>
        [Fact]
        public void Search_IsRegistered()
        {
            var tool = GetTool(Search);
            Assert.Equal(Search, tool.Name);
        }

        /// <summary>search schema requires 'query' and has optional fields.</summary>
        [Fact]
        public void Search_Schema_HasExpectedProperties()
        {
            var tool = GetTool(Search);
            Assert.Contains("query", tool.InputSchema.Required, StringComparer.OrdinalIgnoreCase);

            var props = tool.InputSchema.Properties;
            Assert.True(props.ContainsKey("query"),          "'query' property must exist");
            Assert.True(props.ContainsKey("path_prefix"),    "'path_prefix' property must exist");
            Assert.True(props.ContainsKey("max_results"),    "'max_results' property must exist");
            Assert.True(props.ContainsKey("case_sensitive"), "'case_sensitive' property must exist");
        }

        /// <summary>search finds a file by exact name.</summary>
        [Fact]
        public async Task Search_ExactName_FindsFile()
        {
            var result = await InvokeAsync(Search, new { query = "Class1.cs" });

            var items = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            Assert.True(items.Any(i => i["name"]?.Value<string>()
                .Equals("Class1.cs", StringComparison.OrdinalIgnoreCase) == true),
                "Expected to find 'Class1.cs'");
        }

        /// <summary>search with partial name (no extension) finds matching files.</summary>
        [Fact]
        public async Task Search_PartialName_FindsMatchingFiles()
        {
            var result = await InvokeAsync(Search, new { query = "Class1" });

            var items = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            foreach (var item in items)
                Assert.Contains("Class1", item["name"]?.Value<string>() ?? "", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>search result items always have 'file' kind.</summary>
        [Fact]
        public async Task Search_Items_KindIsAlwaysFile()
        {
            var result = await InvokeAsync(Search, new { query = ".cs" });

            var items = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            foreach (var item in items)
                Assert.Equal("file", item["kind"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>search response has required top-level fields.</summary>
        [Fact]
        public async Task Search_ResponseHasTopLevelFields()
        {
            var result = await InvokeAsync(Search, new { query = "README" });

            Assert.NotNull(result["rootPath"]);
            Assert.Equal("README", result["query"]?.Value<string>());
            Assert.NotNull(result["pathPrefix"]);
            Assert.True(result["resultCount"]?.Value<int>() >= 0);
            Assert.NotNull(result["truncated"]);
        }

        /// <summary>search items have path, name, kind, and sizeBytes fields.</summary>
        [Fact]
        public async Task Search_Items_HaveRequiredFields()
        {
            var result = await InvokeAsync(Search, new { query = ".cs" });
            var items  = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            foreach (var item in items)
            {
                Assert.False(string.IsNullOrEmpty(item["path"]?.Value<string>()), "path must not be empty");
                Assert.False(string.IsNullOrEmpty(item["name"]?.Value<string>()), "name must not be empty");
                Assert.False(string.IsNullOrEmpty(item["kind"]?.Value<string>()), "kind must not be empty");
                Assert.True(item["sizeBytes"]?.Value<long>() >= 0,                "sizeBytes must be non-negative");
            }
        }

        /// <summary>search with max_results=1 returns at most one result.</summary>
        [Fact]
        public async Task Search_MaxResults1_ReturnsAtMostOne()
        {
            var result = await InvokeAsync(Search, new { query = ".cs", max_results = 1 });
            var items  = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.True(items.Count <= 1);
        }

        /// <summary>search auto-detects regex pattern and finds files by regex match.</summary>
        [Fact]
        public async Task Search_UseRegex_FindsMatchingFiles()
        {
            // \\.cs$ has regex metacharacters — auto-detected as regex
            var result = await InvokeAsync(Search, new { query = "\\.cs$" });

            var items = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            foreach (var item in items)
            {
                var name = item["name"]?.Value<string>() ?? "";
                Assert.EndsWith(".cs", name, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>search auto-detects anchored regex and excludes non-matching files.</summary>
        [Fact]
        public async Task Search_UseRegex_AnchoredPattern_ExcludesNonMatching()
        {
            // ^Class1\\.cs$ — auto-detected as regex; should match only "Class1.cs"
            var result = await InvokeAsync(Search, new { query = "^Class1\\.cs$" });

            var items = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            foreach (var item in items)
                Assert.Equal("Class1.cs", item["name"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>find_path auto-detects regex pattern and finds matching files.</summary>
        [Fact]
        public async Task FindPath_UseRegex_FindsMatchingFiles()
        {
            var result = await InvokeAsync(FindPath, new { query = "^Class1\\.cs$", target_kind = "file" });

            var items = result["items"] as JArray;
            Assert.NotNull(items);
            Assert.NotEmpty(items);

            foreach (var item in items)
                Assert.Equal("Class1.cs", item["name"]?.Value<string>(), StringComparer.OrdinalIgnoreCase);
        }

        private async Task<JObject> InvokeAsync(string toolName, object args = null)
        {
            var dictArgs = args == null
                ? new Dictionary<string, object>()
                : JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(args));

            var result = await _registry.InvokeAsync(toolName, dictArgs, CancellationToken.None);

            Assert.NotNull(result.Content);
            Assert.NotEmpty(result.Content);
            var text = result.Content[0].Text;
            Assert.False(string.IsNullOrEmpty(text), $"Tool '{toolName}' returned empty content");

            return JObject.Parse(text);
        }

        private McpToolDefinition GetTool(string name)
        {
            var tool = _registry.GetAllDefinitions()
                                .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            Assert.True(tool != null, $"Tool '{name}' was not found in the registry.");
            return tool;
        }

        private McpPropertySchema GetProperty(string toolName, string propName)
        {
            var tool = GetTool(toolName);
            Assert.NotNull(tool.InputSchema?.Properties);

            var prop = tool.InputSchema.Properties
                           .FirstOrDefault(p => string.Equals(p.Key, propName, StringComparison.OrdinalIgnoreCase))
                           .Value;

            Assert.True(prop != null, $"Tool '{toolName}' has no input property '{propName}'.");
            return prop;
        }
    }
}

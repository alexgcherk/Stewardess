using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Unit tests for <see cref="McpToolRegistry"/> input-schema definitions.
    /// Verifies enum constraints on tool parameters, metadata correctness
    /// (SideEffectClass, RiskLevel, UsageGuidance), and the new fields added
    /// to <see cref="McpToolDefinition"/>.
    /// </summary>
    public sealed class McpToolSchemaTests : IDisposable
    {
        private readonly TempRepository  _repo;
        private readonly McpToolRegistry _registry;

        /// <summary>Initialises a registry with real services backed by a temporary repository.</summary>
        public McpToolSchemaTests()
        {
            _repo = new TempRepository();
            _repo.CreateSampleCsStructure();

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

        /// <inheritdoc/>
        public void Dispose() => _repo.Dispose();

        // ── Enum constraint tests ─────────────────────────────────────────────────

        /// <summary>
        /// get_git_diff.scope must declare an enum with "unstaged", "staged", "head".
        /// </summary>
        [Fact]
        public void GetGitDiff_Scope_HasEnumConstraint()
        {
            var tool = FindTool("get_git_diff");
            var prop = FindProperty(tool, "scope");

            Assert.NotNull(prop.Enum);
            Assert.Contains("unstaged", prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("staged",   prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("head",     prop.Enum, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// get_file_hash.algorithm must declare an enum including MD5, SHA1, SHA256.
        /// </summary>
        [Fact]
        public void GetFileHash_Algorithm_HasEnumConstraint()
        {
            var tool = FindTool("get_file_hash");
            var prop = FindProperty(tool, "algorithm");

            Assert.NotNull(prop.Enum);
            Assert.Contains("MD5",    prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("SHA1",   prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("SHA256", prop.Enum, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// write_file.encoding must declare an enum that includes "utf-8".
        /// </summary>
        [Fact]
        public void WriteFile_Encoding_HasEnumConstraint()
        {
            var tool = FindTool("write_file");
            var prop = FindProperty(tool, "encoding");

            Assert.NotNull(prop.Enum);
            Assert.Contains("utf-8", prop.Enum, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// run_build.configuration must declare an enum including "Debug" and "Release".
        /// </summary>
        [Fact]
        public void RunBuild_Configuration_HasEnumConstraint()
        {
            var tool = FindTool("run_build");
            var prop = FindProperty(tool, "configuration");

            Assert.NotNull(prop.Enum);
            Assert.Contains("Debug",   prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Release", prop.Enum, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// search_symbol.symbol_kind must declare an enum with class, interface, method
        /// and must not default to the empty string.
        /// </summary>
        [Fact]
        public void SearchSymbol_SymbolKind_HasEnumConstraint()
        {
            var tool = FindTool("search_symbol");
            var prop = FindProperty(tool, "symbol_kind");

            Assert.NotNull(prop.Enum);
            Assert.Contains("class",     prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("interface", prop.Enum, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("method",    prop.Enum, StringComparer.OrdinalIgnoreCase);

            // Default should not be the empty string (prior to the fix it was "").
            Assert.False(
                string.Equals(prop.Default?.ToString(), string.Empty),
                "symbol_kind default must not be the empty string");
        }

        /// <summary>
        /// batch_edit.edits items schema must declare an operation property
        /// with an enum containing "write_file", "replace_text", "delete_file".
        /// </summary>
        [Fact]
        public void BatchEdit_Edits_ItemsHaveOperationEnum()
        {
            var tool  = FindTool("batch_edit");
            var prop  = FindProperty(tool, "edits");

            Assert.NotNull(prop.Items);

            // Inspect the anonymous-object Items via JSON round-trip.
            var itemsJson = JsonConvert.SerializeObject(prop.Items);
            var itemsObj  = JObject.Parse(itemsJson);

            var operationProp = itemsObj["properties"]?["operation"] as JObject;
            Assert.NotNull(operationProp);

            var enumArr = operationProp.GetValue("enum", StringComparison.OrdinalIgnoreCase) as JArray;
            Assert.NotNull(enumArr);

            var enumValues = enumArr.Select(e => e.Value<string>()).ToList();
            Assert.Contains("write_file",  enumValues, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("replace_text",enumValues, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("delete_file", enumValues, StringComparer.OrdinalIgnoreCase);
        }

        // ── Metadata correctness tests ────────────────────────────────────────────

        /// <summary>
        /// Every tool that has UsageGuidance set must have a non-empty UseWhen string.
        /// </summary>
        [Fact]
        public void AllTools_UsageGuidance_WhenSet_HasNonEmptyUseWhen()
        {
            foreach (var tool in _registry.GetAllDefinitions())
            {
                if (tool.UsageGuidance == null) continue;

                Assert.False(
                    string.IsNullOrEmpty(tool.UsageGuidance.UseWhen),
                    $"Tool '{tool.Name}' has UsageGuidance but UseWhen is empty");
            }
        }

        /// <summary>
        /// Every tool's SideEffectClass must be one of the recognised values.
        /// </summary>
        [Fact]
        public void AllTools_SideEffectClassIsValid()
        {
            var valid = new[]
            {
                "read-only", "file-write", "process-execution", "git-mutation", "destructive", "service-state-write"
            };

            foreach (var tool in _registry.GetAllDefinitions())
            {
                Assert.True(
                    valid.Contains(tool.SideEffectClass, StringComparer.OrdinalIgnoreCase),
                    $"Tool '{tool.Name}' has unrecognised SideEffectClass '{tool.SideEffectClass}'");
            }
        }

        /// <summary>
        /// Every tool's RiskLevel must be one of "low", "medium", or "high".
        /// </summary>
        [Fact]
        public void AllTools_RiskLevelIsValid()
        {
            var valid = new[] { "low", "medium", "high" };

            foreach (var tool in _registry.GetAllDefinitions())
            {
                Assert.True(
                    valid.Contains(tool.RiskLevel, StringComparer.OrdinalIgnoreCase),
                    $"Tool '{tool.Name}' has unrecognised RiskLevel '{tool.RiskLevel}'");
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private McpToolDefinition FindTool(string name)
        {
            var tool = _registry.GetAllDefinitions()
                .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

            Assert.True(tool != null, $"Tool '{name}' was not found in the registry");
            return tool;
        }

        private static McpPropertySchema FindProperty(McpToolDefinition tool, string propName)
        {
            Assert.NotNull(tool.InputSchema);
            Assert.NotNull(tool.InputSchema.Properties);

            var prop = tool.InputSchema.Properties
                .FirstOrDefault(p => string.Equals(p.Key, propName, StringComparison.OrdinalIgnoreCase))
                .Value;

            Assert.True(prop != null,
                $"Tool '{tool.Name}' has no input property named '{propName}'");
            return prop;
        }
    }
}

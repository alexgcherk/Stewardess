using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StewardessMCPServive.CodeIndexing.Indexing;
using StewardessMCPServive.CodeIndexing.Model.Snapshots;
using StewardessMCPServive.CodeIndexing.Query;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Mcp;
using StewardessMCPServive.Models;
using StewardessMCPServive.Services;
using StewardessMCPServive.Tests.Helpers;
using Xunit;

namespace StewardessMCPServive.Tests.Mcp
{
    /// <summary>
    /// Unit tests verifying the schema quality fixes introduced in the
    /// McpToolRegistry: required fields, enum constraints, type corrections,
    /// deprecated descriptions, and side-effect class values.
    /// </summary>
    public sealed class McpToolSchemaFixTests : IDisposable
    {
        private readonly TempRepository  _repo;
        private readonly McpToolRegistry _registry;

        /// <summary>
        /// Initialises a registry backed by real services and stub code-index services
        /// so that all code_index.* tools are registered.
        /// </summary>
        public McpToolSchemaFixTests()
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

            _registry = new McpToolRegistry(
                settings, fileSvc, searchSvc, editSvc, gitSvc, cmdSvc,
                indexer:    new StubIndexingEngine(),
                indexQuery: new StubIndexQueryService());
        }

        /// <inheritdoc/>
        public void Dispose() => _repo.Dispose();

        // ── CHANGE 1: required fields ─────────────────────────────────────────────

        /// <summary>
        /// get_commit must declare "sha" as required in its input schema.
        /// </summary>
        [Fact]
        public void GetCommit_Sha_IsRequired()
        {
            var tool = FindTool("get_commit");
            Assert.Contains("sha", tool.InputSchema.Required, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// code_index.get_dependencies must declare "symbol_id" as required.
        /// </summary>
        [Fact]
        public void GetDependencies_SymbolId_IsRequired()
        {
            var tool = FindTool("code_index.get_dependencies");
            Assert.Contains("symbol_id", tool.InputSchema.Required, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// code_index.get_dependents must declare "symbol_id" as required.
        /// </summary>
        [Fact]
        public void GetDependents_SymbolId_IsRequired()
        {
            var tool = FindTool("code_index.get_dependents");
            Assert.Contains("symbol_id", tool.InputSchema.Required, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// code_index.get_references must declare "symbol_id" as required.
        /// </summary>
        [Fact]
        public void GetReferences_SymbolId_IsRequired()
        {
            var tool = FindTool("code_index.get_references");
            Assert.Contains("symbol_id", tool.InputSchema.Required, StringComparer.OrdinalIgnoreCase);
        }

        // ── CHANGE 3: enum constraints ────────────────────────────────────────────

        /// <summary>
        /// code_index.build parse_mode must declare an enum with the three parse depth values.
        /// </summary>
        [Fact]
        public void CodeIndexBuild_ParseMode_HasEnumConstraint()
        {
            var tool = FindTool("code_index.build");
            var prop = FindProperty(tool, "parse_mode");

            Assert.NotNull(prop.Enum);
            Assert.Contains("Declarations",              prop.Enum!, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("OutlineOnly",               prop.Enum!, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("DeclarationsAndReferences", prop.Enum!, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// code_index.find_symbols match_mode must declare an enum with "exact", "prefix", "contains".
        /// </summary>
        [Fact]
        public void FindSymbols_MatchMode_HasEnumConstraint()
        {
            var tool = FindTool("code_index.find_symbols");
            var prop = FindProperty(tool, "match_mode");

            Assert.NotNull(prop.Enum);
            Assert.Contains("exact",    prop.Enum!, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("prefix",   prop.Enum!, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("contains", prop.Enum!, StringComparer.OrdinalIgnoreCase);
        }

        // ── CHANGE 2: changed_files array ─────────────────────────────────────────

        /// <summary>
        /// code_index.update changed_files must be declared as type "array".
        /// </summary>
        [Fact]
        public void CodeIndexUpdate_ChangedFiles_IsArray()
        {
            var tool = FindTool("code_index.update");
            var prop = FindProperty(tool, "changed_files");

            Assert.Equal("array", prop.Type, StringComparer.OrdinalIgnoreCase);
        }

        // ── CHANGE 7: deprecated descriptions ────────────────────────────────────

        /// <summary>
        /// code_index.get_status must have a description containing "Deprecated".
        /// </summary>
        [Fact]
        public void GetStatus_Description_ContainsDeprecated()
        {
            var tool = FindTool("code_index.get_status");
            Assert.Contains("Deprecated", tool.Description, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// code_index.list_roots must have a description containing "Deprecated".
        /// </summary>
        [Fact]
        public void ListRoots_Description_ContainsDeprecated()
        {
            var tool = FindTool("code_index.list_roots");
            Assert.Contains("Deprecated", tool.Description, StringComparison.OrdinalIgnoreCase);
        }

        // ── CHANGE 6: side-effect class ───────────────────────────────────────────

        /// <summary>
        /// code_index.build and code_index.update must declare SideEffectClass = "service-state-write".
        /// </summary>
        [Fact]
        public void IndexTools_BuildAndUpdate_SideEffectClass_IsServiceStateWrite()
        {
            var build  = FindTool("code_index.build");
            var update = FindTool("code_index.update");

            Assert.Equal("service-state-write", build.SideEffectClass,  StringComparer.OrdinalIgnoreCase);
            Assert.Equal("service-state-write", update.SideEffectClass, StringComparer.OrdinalIgnoreCase);
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private McpToolDefinition FindTool(string name)
        {
            var tool = _registry.GetAllDefinitions()
                .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

            Assert.True(tool != null, $"Tool '{name}' was not found in the registry");
            return tool!;
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
            return prop!;
        }

        // ── Stub implementations ──────────────────────────────────────────────────

        /// <summary>
        /// Minimal stub implementation of <see cref="IIndexingEngine"/> for schema tests.
        /// No actual indexing is performed; methods return stub results.
        /// </summary>
        private sealed class StubIndexingEngine : IIndexingEngine
        {
            public Task<IndexBuildResult> BuildAsync(IndexBuildRequest request, CancellationToken ct = default)
                => Task.FromResult(new IndexBuildResult
                {
                    SnapshotId = "stub-snap",
                    RootPath   = request.RootPath,
                    Status     = "success"
                });

            public Task<IndexUpdateResult> UpdateAsync(IndexUpdateRequest request, CancellationToken ct = default)
                => Task.FromResult(new IndexUpdateResult
                {
                    SnapshotId = "stub-snap",
                    RootPath   = request.RootPath
                });

            public Task<IndexStatus> GetStatusAsync(string rootPath, CancellationToken ct = default)
                => Task.FromResult(new IndexStatus
                {
                    RootPath = rootPath,
                    State    = IndexState.Ready
                });

            public Task<int> ClearRepositoryAsync(string rootPath, CancellationToken ct = default)
                => Task.FromResult(0);
        }

        /// <summary>
        /// Minimal stub implementation of <see cref="IIndexQueryService"/> for schema tests.
        /// All methods return empty/null results.
        /// </summary>
        private sealed class StubIndexQueryService : IIndexQueryService
        {
            public Task<SnapshotMetadata?> GetSnapshotInfoAsync(string? snapshotId, string? rootPath, CancellationToken ct = default)
                => Task.FromResult<SnapshotMetadata?>(null);

            public Task<StewardessMCPServive.CodeIndexing.Model.Snapshots.IndexSnapshot?> GetSnapshotAsync(string? snapshotId, string? rootPath, CancellationToken ct = default)
                => Task.FromResult<StewardessMCPServive.CodeIndexing.Model.Snapshots.IndexSnapshot?>(null);

            public Task<ListFilesResponse> ListFilesAsync(ListFilesRequest request, CancellationToken ct = default)
                => Task.FromResult(new ListFilesResponse { SnapshotId = "stub" });

            public Task<FileOutlineResponse?> GetFileOutlineAsync(GetFileOutlineRequest request, CancellationToken ct = default)
                => Task.FromResult<FileOutlineResponse?>(null);

            public Task<IReadOnlyList<string>> ListRootPathsAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            public Task<FindSymbolsResponse> FindSymbolsAsync(FindSymbolsRequest request, CancellationToken ct = default)
                => Task.FromResult(new FindSymbolsResponse { SnapshotId = "stub" });

            public Task<GetSymbolResponse> GetSymbolAsync(GetSymbolRequest request, CancellationToken ct = default)
                => Task.FromResult(new GetSymbolResponse { SnapshotId = "stub" });

            public Task<GetSymbolOccurrencesResponse> GetSymbolOccurrencesAsync(GetSymbolOccurrencesRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
                => Task.FromResult(new GetSymbolOccurrencesResponse { SnapshotId = "stub", SymbolId = request.SymbolId });

            public Task<GetSymbolChildrenResponse> GetSymbolChildrenAsync(GetSymbolChildrenRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
                => Task.FromResult(new GetSymbolChildrenResponse { SnapshotId = "stub", SymbolId = request.SymbolId });

            public Task<GetTypeMembersResponse> GetTypeMembersAsync(GetTypeMembersRequest request, CancellationToken ct = default)
                => Task.FromResult(new GetTypeMembersResponse { SnapshotId = "stub", TypeSymbolId = request.TypeSymbolId });

            public Task<ResolveLocationResponse> ResolveLocationAsync(ResolveLocationRequest request, CancellationToken ct = default)
                => Task.FromResult(new ResolveLocationResponse { SnapshotId = "stub" });

            public Task<GetNamespaceTreeResponse> GetNamespaceTreeAsync(GetNamespaceTreeRequest request, CancellationToken ct = default)
                => Task.FromResult(new GetNamespaceTreeResponse { SnapshotId = "stub" });

            public Task<GetImportsResponse> GetImportsAsync(GetImportsRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
                => Task.FromResult(new GetImportsResponse { SnapshotId = "stub", FilePath = request.FilePath, Error = "not implemented" });

            public Task<GetReferencesResponse> GetReferencesAsync(GetReferencesRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
                => Task.FromResult(new GetReferencesResponse { SnapshotId = "stub", SymbolId = request.SymbolId });

            public Task<GetFileReferencesResponse> GetFileReferencesAsync(GetFileReferencesRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
                => Task.FromResult(new GetFileReferencesResponse { SnapshotId = "stub", FilePath = request.FilePath, Error = "not implemented" });

            public Task<GetDependenciesResponse> GetDependenciesAsync(GetDependenciesRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
                => Task.FromResult(new GetDependenciesResponse { SnapshotId = "stub", SymbolId = request.SymbolId });

            public Task<GetDependentsResponse> GetDependentsAsync(GetDependentsRequest request, int page = 1, int pageSize = 50, CancellationToken ct = default)
                => Task.FromResult(new GetDependentsResponse { SnapshotId = "stub", SymbolId = request.SymbolId });

            public Task<GetSymbolRelationshipsResponse> GetSymbolRelationshipsAsync(GetSymbolRelationshipsRequest request, CancellationToken ct = default)
                => Task.FromResult(new GetSymbolRelationshipsResponse { SnapshotId = "stub", SymbolId = request.SymbolId });

            public Task<GetFileDependenciesResponse> GetFileDependenciesAsync(GetFileDependenciesRequest request, CancellationToken ct = default)
                => Task.FromResult(new GetFileDependenciesResponse { SnapshotId = "stub", FilePath = request.FilePath, Error = "not implemented" });
        }
    }
}

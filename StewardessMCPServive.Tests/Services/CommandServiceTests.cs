using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StewardessMCPServive.Configuration;
using StewardessMCPServive.Infrastructure;
using StewardessMCPServive.Models;
using StewardessMCPServive.Services;
using StewardessMCPServive.Tests.Helpers;
using Xunit;

namespace StewardessMCPServive.Tests.Services
{
    /// <summary>
    /// Unit tests for <see cref="CommandService"/>.
    ///
    /// Tests that actually execute processes require the corresponding tools
    /// (dotnet, git) to be installed and may be slow.  Tests for pure logic
    /// (allowlist, output parsing, SplitCommand) are always executed.
    /// </summary>
    public sealed class CommandServiceTests : IDisposable
    {
        private readonly TempRepository   _repo;
        private readonly CommandService   _svc;
        private readonly MockAuditService _audit;

        public CommandServiceTests()
        {
            _repo  = new TempRepository();
            _audit = new MockAuditService();
            _svc   = Build(_repo.Root, _audit);
        }

        public void Dispose() => _repo.Dispose();

        // ── IsCommandAllowed ─────────────────────────────────────────────────────

        [Theory]
        [InlineData("dotnet build", true)]
        [InlineData("dotnet test", true)]
        [InlineData("dotnet restore", true)]
        [InlineData("git status", true)]
        [InlineData("git diff", true)]
        [InlineData("msbuild", true)]
        [InlineData("rm -rf /", false)]
        [InlineData("del /f /q C:\\Windows", false)]
        [InlineData("cmd /c del *", false)]
        [InlineData("powershell -enc ABC", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsCommandAllowed_ReturnsExpected(string command, bool expected)
        {
            Assert.Equal(expected, _svc.IsCommandAllowed(command));
        }

        [Fact]
        public void IsCommandAllowed_CaseInsensitive_ReturnsTrue()
        {
            Assert.True(_svc.IsCommandAllowed("DOTNET BUILD"));
            Assert.True(_svc.IsCommandAllowed("Git Status"));
        }

        // ── SplitCommand ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData("dotnet build", "dotnet", "build")]
        [InlineData("git status --short", "git", "status --short")]
        [InlineData("msbuild", "msbuild", "")]
        [InlineData("dotnet test --filter Foo", "dotnet", "test --filter Foo")]
        public void SplitCommand_SplitsCorrectly(string input, string expectedFile, string expectedArgs)
        {
            var (file, args) = CommandService.SplitCommand(input);
            Assert.Equal(expectedFile, file);
            Assert.Equal(expectedArgs, args);
        }

        // ── ParseBuildSummary ────────────────────────────────────────────────────

        [Fact]
        public void ParseBuildSummary_NoErrors_ReturnsEmptySummary()
        {
            var summary = CommandService.ParseBuildSummary("Build succeeded.");
            Assert.Equal(0, summary.ErrorCount);
            Assert.Equal(0, summary.WarningCount);
        }

        [Fact]
        public void ParseBuildSummary_WithError_ExtractsError()
        {
            const string output =
                @"src\MyLib\Class1.cs(12,4): error CS0246: The type or namespace name 'Foo' could not be found";
            var summary = CommandService.ParseBuildSummary(output);

            Assert.Equal(1, summary.ErrorCount);
            Assert.Equal(0, summary.WarningCount);
            Assert.Single(summary.Errors);

            var diag = summary.Errors[0];
            Assert.Equal("error", diag.Severity);
            Assert.Equal("CS0246", diag.Code);
            Assert.Equal(12, diag.Line);
            Assert.Equal(4,  diag.Column);
        }

        [Fact]
        public void ParseBuildSummary_WithWarning_ExtractsWarning()
        {
            const string output =
                @"src\Program.cs(5,10): warning CS0168: The variable 'ex' is declared but never used";
            var summary = CommandService.ParseBuildSummary(output);

            Assert.Equal(0, summary.ErrorCount);
            Assert.Equal(1, summary.WarningCount);
            Assert.Single(summary.Warnings);
            Assert.Equal("CS0168", summary.Warnings[0].Code);
        }

        [Fact]
        public void ParseBuildSummary_MixedOutput_ExtractsBoth()
        {
            const string output =
                "src\\A.cs(1,1): error CS0001: foo\n" +
                "src\\B.cs(2,2): warning CS0100: bar\n" +
                "src\\C.cs(3,3): error CS0002: baz";

            var summary = CommandService.ParseBuildSummary(output);
            Assert.Equal(2, summary.ErrorCount);
            Assert.Equal(1, summary.WarningCount);
        }

        // ── ParseTestSummary ─────────────────────────────────────────────────────

        [Fact]
        public void ParseTestSummary_AllPassed_ReturnsZeroFailed()
        {
            const string output = "Total tests: 42   Passed: 42   Failed: 0   Skipped: 0";
            var summary = CommandService.ParseTestSummary(output);

            Assert.Equal(42, summary.TestsTotal);
            Assert.Equal(42, summary.TestsPassed);
            Assert.Equal(0,  summary.TestsFailed);
            Assert.Equal(0,  summary.ErrorCount);
        }

        [Fact]
        public void ParseTestSummary_WithFailures_SetsErrorCount()
        {
            const string output = "Total tests: 10   Passed: 7   Failed: 3   Skipped: 0";
            var summary = CommandService.ParseTestSummary(output);

            Assert.Equal(3, summary.TestsFailed);
            Assert.Equal(3, summary.ErrorCount);
        }

        // ── RunCustomCommandAsync — allowlist enforcement ─────────────────────────

        [Fact]
        public async Task RunCustomCommand_DisallowedCommand_ThrowsUnauthorized()
        {
            var req = new RunCustomCommandRequest { Command = "rm -rf /" };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _svc.RunCustomCommandAsync(req));
        }

        [Fact]
        public async Task RunCustomCommand_ReadOnlyMode_ThrowsInvalidOperation()
        {
            var readOnlySvc = BuildReadOnly(_repo.Root);
            var req = new RunCustomCommandRequest { Command = "dotnet build" };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                readOnlySvc.RunCustomCommandAsync(req));
        }

        [Fact]
        public async Task RunBuild_ReadOnlyMode_Throws()
        {
            var svc = BuildReadOnly(_repo.Root);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.RunBuildAsync(new RunBuildRequest()));
        }

        // ── RunCustomCommandAsync — process execution ─────────────────────────────

        [Fact]
        public async Task RunCustomCommand_GitStatus_SucceedsWhenGitInstalled()
        {
            // Only run this test if git is on PATH.
            if (!IsGitAvailable()) return;

            // Create a .git directory to avoid "not a git repository" error.
            Directory.CreateDirectory(Path.Combine(_repo.Root, ".git"));

            var req = new RunCustomCommandRequest { Command = "git status" };
            var result = await _svc.RunCustomCommandAsync(req);

            Assert.NotNull(result);
            Assert.Equal("git status", result.Command);
            // Exit code 128 is acceptable (no commits yet in empty repo).
            Assert.True(result.ExitCode == 0 || result.ExitCode == 128 || result.ExitCode == 129);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static CommandService Build(string root, IAuditService audit) =>
            new CommandService(
                McpServiceSettings.CreateForTesting(root),
                new PathValidator(McpServiceSettings.CreateForTesting(root)),
                audit);

        private static CommandService BuildReadOnly(string root) =>
            new CommandService(
                McpServiceSettings.CreateForTesting(root, readOnly: true),
                new PathValidator(McpServiceSettings.CreateForTesting(root, readOnly: true)),
                new MockAuditService());

        private static bool IsGitAvailable()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "--version")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    p?.WaitForExit(3000);
                    return p?.ExitCode == 0;
                }
            }
            catch { return false; }
        }
    }

    // ── Minimal IAuditService stub for tests ──────────────────────────────────────

    internal sealed class MockAuditService : StewardessMCPServive.Services.IAuditService
    {
        public System.Threading.Tasks.Task LogAsync(
            StewardessMCPServive.Models.AuditEntry entry,
            CancellationToken ct = default)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task LogOperationAsync(
            string requestId, string sessionId,
            StewardessMCPServive.Models.AuditOperationType operationType,
            string operationName, string clientIp, string targetPath,
            StewardessMCPServive.Models.AuditOutcome outcome, string errorCode,
            string description, long elapsedMs,
            string changeReason = null, string backupPath = null,
            CancellationToken ct = default)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task<StewardessMCPServive.Models.AuditLogQueryResponse> QueryAsync(
            StewardessMCPServive.Models.AuditLogQueryRequest request,
            CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(new StewardessMCPServive.Models.AuditLogQueryResponse());
    }
}

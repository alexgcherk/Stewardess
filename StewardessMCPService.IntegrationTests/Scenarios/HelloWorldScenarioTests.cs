using System;
using System.Threading.Tasks;
using StewardessMCPService.IntegrationTests.Helpers;
using Xunit;

namespace StewardessMCPService.IntegrationTests.Scenarios
{
    [Collection(IntegrationTestCollection.Name)]
    /// <summary>
    /// End-to-end integration scenario exercising the live MCP REST API.
    ///
    /// The test uses the in-process OWIN server (<see cref="McpTestServer"/>)
    /// to:
    /// <list type="number">
    ///   <item>Create a Hello World C# console project inside a temporary repository.</item>
    ///   <item>Build it with the .NET SDK via <c>POST /api/command/build</c>.</item>
    ///   <item>Run it via <c>POST /api/command/run</c>.</item>
    ///   <item>Assert the standard output contains <c>"Hello, World!"</c>.</item>
    /// </list>
    ///
    /// <para>
    /// Requires the .NET SDK (<c>dotnet</c>) to be installed and on the PATH.
    /// </para>
    /// </summary>
    public sealed class HelloWorldScenarioTests : IClassFixture<McpTestServer>
    {
        private readonly McpRestClient _client;

        // ── Hello World project content ──────────────────────────────────────────

        /// <summary>Minimal SDK-style .csproj targeting net9.0 as an executable.</summary>
        private const string ProjectFile =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <OutputType>Exe</OutputType>\n" +
            "    <TargetFramework>net9.0</TargetFramework>\n" +
            "    <Nullable>enable</Nullable>\n" +
            "  </PropertyGroup>\n" +
            "</Project>";

        /// <summary>Top-level-statement Program.cs that prints a greeting.</summary>
        private const string ProgramCs =
            "using System;\n" +
            "\n" +
            "class Program\n" +
            "{\n" +
            "    static void Main()\n" +
            "    {\n" +
            "        Console.WriteLine(\"Hello, World!\");\n" +
            "    }\n" +
            "}";

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>Receives the shared server fixture injected by xUnit.</summary>
        public HelloWorldScenarioTests(McpTestServer server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            _client = new McpRestClient(server.HttpClient);
        }

        // ── Tests ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Smoke test: the in-process server must respond to the health endpoint.
        /// </summary>
        [Fact]
        public async Task HealthCheck_ReturnsHealthyStatus()
        {
            var body = await _client.GetHealthAsync();

            Assert.NotNull(body);
            Assert.Contains("healthy", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Full scenario: create project files, build, run, verify output.
        /// </summary>
        [Fact]
        public async Task CreateHelloWorldProject_BuildAndRun_OutputsHelloWorld()
        {
            // ── Step 1: create project directory ─────────────────────────────────
            var dirResult = await _client.CreateDirectoryAsync("HelloWorld");
            Assert.Equal("create_directory", dirResult.Operation,
                StringComparer.OrdinalIgnoreCase);

            // ── Step 2: write project file ────────────────────────────────────────
            var projResult = await _client.WriteFileAsync(
                "HelloWorld/HelloWorld.csproj", ProjectFile);
            Assert.True(projResult.Success,
                $"Writing .csproj failed: {projResult.ErrorMessage}");

            // ── Step 3: write Program.cs ──────────────────────────────────────────
            var progResult = await _client.WriteFileAsync(
                "HelloWorld/Program.cs", ProgramCs);
            Assert.True(progResult.Success,
                $"Writing Program.cs failed: {progResult.ErrorMessage}");

            // ── Step 4: build ─────────────────────────────────────────────────────
            var buildResult = await _client.BuildAsync("HelloWorld/HelloWorld.csproj");
            Assert.True(
                buildResult.ExitCode == 0,
                $"Build failed (exit {buildResult.ExitCode}):\n{buildResult.CombinedOutput}");

            // ── Step 5: run (no rebuild needed) ──────────────────────────────────
            var runResult = await _client.RunCommandAsync(
                "dotnet run --project HelloWorld/HelloWorld.csproj --no-build -c Debug");
            Assert.True(
                runResult.ExitCode == 0,
                $"Run failed (exit {runResult.ExitCode}):\n{runResult.CombinedOutput}");

            // ── Step 6: verify output ─────────────────────────────────────────────
            Assert.Contains("Hello, World!", runResult.StandardOutput);
        }
    }
}

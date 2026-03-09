// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using Newtonsoft.Json.Linq;

namespace StewardessMCPService.IntegrationTests.Helpers;

/// <summary>
///     xUnit class fixture that spins up an in-process <see cref="McpTestServer" />
///     backed by a temporary repository containing the canonical Hello World
///     <c>Program.cs</c> file, then pre-builds the code index so that all Phase 1
///     and Phase 2 tool tests can run against a stable, predictable snapshot.
/// </summary>
/// <remarks>
///     <para>
///         The <c>Program.cs</c> content is identical to the file produced by the
///         <c>HelloWorldScenarioTests</c>, making the expected symbol/outline values
///         easy to reason about:
///         <code>
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
///     </para>
///     <para>
///         The fixture constructor runs synchronously; async work is completed via
///         <c>GetAwaiter().GetResult()</c> because xUnit fixture constructors cannot
///         be async.
///     </para>
/// </remarks>
public sealed class HelloWorldIndexedFixture : IDisposable
{
    // ── HelloWorld Program.cs content and its known structure ─────────────────

    /// <summary>File name of the source file written into the temp repo.</summary>
    public const string ProgramCsFileName = "Program.cs";

    /// <summary>
    ///     The exact content of <c>Program.cs</c>.  Line breaks are Unix-style
    ///     (<c>\n</c>) so line numbers are deterministic across platforms.
    /// </summary>
    public const string ProgramCsContent =
        "using System;\n" +
        "\n" +
        "class Program\n" +
        "{\n" +
        "    static void Main()\n" +
        "    {\n" +
        "        Console.WriteLine(\"Hello, World!\");\n" +
        "    }\n" +
        "}";

    // ── Known structural facts derived from ProgramCsContent ─────────────────

    /// <summary>The simple name of the class declared in <c>Program.cs</c>.</summary>
    public const string ClassName = "Program";

    /// <summary>The simple name of the method declared inside <see cref="ClassName" />.</summary>
    public const string MethodName = "Main";

    /// <summary>
    ///     1-based line number of the <c>class Program</c> declaration.
    ///     Line 3 in the content above.
    /// </summary>
    public const int ClassStartLine = 3;

    /// <summary>
    ///     1-based line number of the <c>static void Main()</c> declaration.
    ///     Line 5 in the content above.
    /// </summary>
    public const int MethodStartLine = 5;

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Creates the temporary repository, writes <see cref="ProgramCsContent" />
    ///     as <c>Program.cs</c>, starts the in-process test server, and builds the
    ///     code index synchronously.
    /// </summary>
    public HelloWorldIndexedFixture()
    {
        TempRepo = new TempTestRepository();

        File.WriteAllText(
            Path.Combine(TempRepo.Root, ProgramCsFileName),
            ProgramCsContent);

        Server = new McpTestServer(TempRepo.Root);
        Client = Server.CreateHttpClient();

        var (data, _) = Client
            .CallToolAsync("code_index.build", new { root_path = TempRepo.Root })
            .GetAwaiter()
            .GetResult();

        SnapshotId = data
            .GetValue("SnapshotId", StringComparison.OrdinalIgnoreCase)
            ?.Value<string>();
    }

    // ── Public surface ───────────────────────────────────────────────────────

    /// <summary>The temporary repository directory created for this fixture.</summary>
    internal TempTestRepository TempRepo { get; }

    /// <summary>In-process test server pointed at <see cref="TempRepo" />.</summary>
    public McpTestServer Server { get; }

    /// <summary>HTTP client wired to <see cref="Server" />.</summary>
    public McpRestClient Client { get; }

    /// <summary>Absolute path to the temporary repository root.</summary>
    public string RootPath => TempRepo.Root;

    /// <summary>
    ///     Snapshot ID returned by the <c>code_index.build</c> call during
    ///     fixture initialisation.  Null only if the build unexpectedly fails.
    /// </summary>
    public string? SnapshotId { get; }

    /// <summary>
    ///     Relative path of the sample C# file written into the repo.
    ///     Pass this as the <c>file_path</c> argument to outline/file tools.
    /// </summary>
    public string SampleFilePath => ProgramCsFileName;

    // ── IDisposable ──────────────────────────────────────────────────────────

    /// <summary>Disposes the in-process server and temporary repository.</summary>
    public void Dispose()
    {
        Server?.Dispose();
        TempRepo?.Dispose();
    }
}
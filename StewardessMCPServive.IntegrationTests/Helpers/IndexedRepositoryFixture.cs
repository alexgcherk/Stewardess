using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace StewardessMCPServive.IntegrationTests.Helpers
{
    /// <summary>
    /// xUnit class fixture that spins up an in-process <see cref="McpTestServer"/>
    /// backed by a temporary repository containing a known C# source file, then
    /// pre-builds the code index so that symbol query tests can run against a
    /// stable, pre-populated snapshot.
    /// </summary>
    /// <remarks>
    /// The fixture constructor runs synchronously; async work is completed via
    /// <c>GetAwaiter().GetResult()</c> because xUnit fixture constructors cannot
    /// be async.
    /// </remarks>
    public sealed class IndexedRepositoryFixture : IDisposable
    {
        // ── Known C# file written into the temp repo ─────────────────────────────
        // Designed to produce a predictable set of symbols for query tests.
        private const string SampleCsFileName = "Customer.cs";

        private const string SampleCsContent =
            "namespace TestApp.Domain\n" +
            "{\n" +
            "    public class Customer\n" +
            "    {\n" +
            "        public string Name { get; set; }\n" +
            "        public int Age { get; set; }\n" +
            "\n" +
            "        public Customer(string name, int age)\n" +
            "        {\n" +
            "            Name = name;\n" +
            "            Age = age;\n" +
            "        }\n" +
            "\n" +
            "        public string Greet() => $\"Hello, {Name}!\";\n" +
            "    }\n" +
            "\n" +
            "    public interface ICustomerRepository\n" +
            "    {\n" +
            "        Customer GetById(int id);\n" +
            "    }\n" +
            "\n" +
            "    public enum Status { Active, Inactive }\n" +
            "}\n";

        // ── Public surface ───────────────────────────────────────────────────────

        /// <summary>The temporary repository directory created for this fixture.</summary>
        internal TempTestRepository TempRepo { get; }

        /// <summary>In-process test server pointed at <see cref="TempRepo"/>.</summary>
        public McpTestServer Server { get; }

        /// <summary>HTTP client wired to <see cref="Server"/>.</summary>
        public McpRestClient Client { get; }

        /// <summary>Absolute path to the temporary repository root.</summary>
        public string RootPath => TempRepo.Root;

        /// <summary>
        /// Snapshot ID of the code index build that ran during fixture initialisation.
        /// Null if the build did not produce a snapshot (should not happen in practice).
        /// </summary>
        public string SnapshotId { get; }

        /// <summary>Relative path of the sample C# file written into the repo.</summary>
        public string SampleFilePath => SampleCsFileName;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the temporary repository, writes <see cref="SampleCsContent"/>,
        /// starts the in-process test server, and builds the code index.
        /// </summary>
        public IndexedRepositoryFixture()
        {
            TempRepo = new TempTestRepository();

            // Write the known C# file so the index has something to index.
            File.WriteAllText(
                Path.Combine(TempRepo.Root, SampleCsFileName),
                SampleCsContent);

            Server = new McpTestServer(TempRepo.Root);
            Client = Server.CreateHttpClient();

            // Build the index synchronously (fixture constructor cannot be async).
            var (data, _) = Client
                .CallToolAsync("code_index.build", new { root_path = TempRepo.Root })
                .GetAwaiter()
                .GetResult();

            SnapshotId = data.GetValue("SnapshotId", StringComparison.OrdinalIgnoreCase)
                           ?.Value<string>();
        }

        // ── IDisposable ──────────────────────────────────────────────────────────

        /// <summary>Disposes the in-process server and temporary repository.</summary>
        public void Dispose()
        {
            Server?.Dispose();
            TempRepo?.Dispose();
        }
    }
}

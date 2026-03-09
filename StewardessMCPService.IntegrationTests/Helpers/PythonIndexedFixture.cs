using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace StewardessMCPService.IntegrationTests.Helpers
{
    /// <summary>
    /// xUnit class fixture that provides a pre-built code index backed by a single
    /// Python file (<c>animals.py</c>) designed to exercise Phase 3 import-extraction
    /// tools and Python-specific structural outline behaviour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Python file content contains four import statements of different kinds
    /// and two class declarations, one of which inherits from the other:
    /// </para>
    /// <code>
    /// import os
    /// import sys as system
    /// from typing import List
    /// from .base import BaseAnimal
    ///
    /// class Animal:
    ///     def __init__(self, name: str):
    ///         self.name = name
    ///
    ///     def speak(self) -> str:
    ///         pass
    ///
    /// class Dog(Animal):
    ///     def bark(self) -> str:
    ///         return "Woof!"
    /// </code>
    /// <para>
    /// Imports produced:
    /// <list type="bullet">
    ///   <item><description><c>import os</c>                          — Kind="import"</description></item>
    ///   <item><description><c>import sys as system</c>               — Kind="import", Alias="system"</description></item>
    ///   <item><description><c>from typing import List</c>            — Kind="from-import", NormalizedTarget="typing"</description></item>
    ///   <item><description><c>from .base import BaseAnimal</c>       — Kind="relative-import", NormalizedTarget="base"</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Reference edges: the <c>PythonSymbolProjector</c> converts structural nodes
    /// into logical symbols, so reference hints ARE anchored and produce file-indexed
    /// edges.  The single inheritance <c>class Dog(Animal):</c> yields one
    /// <c>Inherits</c> edge with <c>ResolutionClass = ExactBound</c>.
    /// </para>
    /// </remarks>
    public sealed class PythonIndexedFixture : IDisposable
    {
        // ── Known file content ────────────────────────────────────────────────────

        /// <summary>File name of the Python source file written into the temp repo.</summary>
        public const string FileName = "animals.py";

        /// <summary>The exact content of <c>animals.py</c>.</summary>
        public const string FileContent =
            "import os\n" +
            "import sys as system\n" +
            "from typing import List\n" +
            "from .base import BaseAnimal\n" +
            "\n" +
            "class Animal:\n" +
            "    def __init__(self, name: str):\n" +
            "        self.name = name\n" +
            "\n" +
            "    def speak(self) -> str:\n" +
            "        pass\n" +
            "\n" +
            "class Dog(Animal):\n" +
            "    def bark(self) -> str:\n" +
            "        return \"Woof!\"\n";

        // ── Known import facts ────────────────────────────────────────────────────

        /// <summary>Total number of import directives in <see cref="FileContent"/>.</summary>
        public const int ImportCount = 4;

        // ── Known reference-edge facts ────────────────────────────────────────────

        /// <summary>
        /// Total number of reference edges from <see cref="FileContent"/>:
        /// exactly one — <c>Dog</c> → Inherits → <c>Animal</c>.
        /// </summary>
        public const int ExpectedEdgeCount = 1;

        /// <summary>Name of the base class in <see cref="FileContent"/>.</summary>
        public const string BaseClassName = "Animal";

        /// <summary>Name of the derived class in <see cref="FileContent"/>.</summary>
        public const string DerivedClassName = "Dog";

        /// <summary>
        /// Expected number of root-level outline nodes (one for each top-level class).
        /// </summary>
        public const int RootNodeCount = 2;

        // ── Public surface ────────────────────────────────────────────────────────

        /// <summary>The temporary repository directory created for this fixture.</summary>
        internal TempTestRepository TempRepo { get; }

        /// <summary>In-process test server pointed at <see cref="TempRepo"/>.</summary>
        public McpTestServer Server { get; }

        /// <summary>HTTP client wired to <see cref="Server"/>.</summary>
        public McpRestClient Client { get; }

        /// <summary>Absolute path to the temporary repository root.</summary>
        public string RootPath => TempRepo.Root;

        /// <summary>Snapshot ID returned by the index build during fixture initialisation.</summary>
        public string SnapshotId { get; }

        /// <summary>Relative path of the Python file written into the repo.</summary>
        public string SampleFilePath => FileName;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the temporary repository, writes <see cref="FileContent"/> as
        /// <c>animals.py</c>, starts the in-process test server, and builds the code index.
        /// </summary>
        public PythonIndexedFixture()
        {
            TempRepo = new TempTestRepository();

            File.WriteAllText(
                Path.Combine(TempRepo.Root, FileName),
                FileContent);

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

        // ── IDisposable ──────────────────────────────────────────────────────────

        /// <summary>Disposes the in-process server and temporary repository.</summary>
        public void Dispose()
        {
            Server?.Dispose();
            TempRepo?.Dispose();
        }
    }
}

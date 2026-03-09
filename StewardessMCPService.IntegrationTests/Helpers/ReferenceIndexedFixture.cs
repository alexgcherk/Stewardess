using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace StewardessMCPService.IntegrationTests.Helpers
{
    /// <summary>
    /// xUnit class fixture that provides a pre-built code index backed by a single
    /// <c>Orders.cs</c> file designed to exercise Phase 3 reference-extraction tools.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixture writes a C# source file that contains two <c>using</c> directives,
    /// three types (an interface, a base class, and a derived class), and several
    /// member declarations that produce a known set of reference edges:
    /// </para>
    /// <code>
    /// using System;
    /// using System.Collections.Generic;
    ///
    /// namespace TestApp.Orders
    /// {
    ///     public interface IEntity { int Id { get; } }
    ///
    ///     public class BaseOrder { public int Id { get; set; } }
    ///
    ///     public class Order : BaseOrder, IEntity
    ///     {
    ///         public int Id { get; set; }
    ///         public BaseOrder Parent { get; set; }        // ContainsPropertyOfType
    ///         public Order(int id) { Id = id; }
    ///         public BaseOrder GetParent()                 // ReturnsType
    ///             => new BaseOrder();                      // CreatesInstanceOf
    ///     }
    /// }
    /// </code>
    /// <para>
    /// This produces exactly <see cref="ExpectedEdgeCount"/> reference edges and
    /// <see cref="ImportCount"/> import entries, which are asserted by the integration
    /// tests in <c>CodeIndexReferenceTests</c>.
    /// </para>
    /// </remarks>
    public sealed class ReferenceIndexedFixture : IDisposable
    {
        // ── Known file content ────────────────────────────────────────────────────

        /// <summary>File name of the source file written into the temp repo.</summary>
        public const string FileName = "Orders.cs";

        /// <summary>The exact content of <c>Orders.cs</c>.</summary>
        public const string FileContent =
            "using System;\n" +
            "using System.Collections.Generic;\n" +
            "\n" +
            "namespace TestApp.Orders\n" +
            "{\n" +
            "    public interface IEntity\n" +
            "    {\n" +
            "        int Id { get; }\n" +
            "    }\n" +
            "\n" +
            "    public class BaseOrder\n" +
            "    {\n" +
            "        public int Id { get; set; }\n" +
            "    }\n" +
            "\n" +
            "    public class Order : BaseOrder, IEntity\n" +
            "    {\n" +
            "        public int Id { get; set; }\n" +
            "        public BaseOrder Parent { get; set; }\n" +
            "\n" +
            "        public Order(int id)\n" +
            "        {\n" +
            "            Id = id;\n" +
            "        }\n" +
            "\n" +
            "        public BaseOrder GetParent()\n" +
            "        {\n" +
            "            return new BaseOrder();\n" +
            "        }\n" +
            "    }\n" +
            "}\n";

        // ── Known import facts ────────────────────────────────────────────────────

        /// <summary>Number of <c>using</c> directives in <see cref="FileContent"/>.</summary>
        public const int ImportCount = 2;

        /// <summary>The <c>NormalizedTarget</c> of the first import (<c>using System;</c>).</summary>
        public const string FirstImportTarget = "System";

        /// <summary>The <c>NormalizedTarget</c> of the second import.</summary>
        public const string SecondImportTarget = "System.Collections.Generic";

        // ── Known reference-edge facts ────────────────────────────────────────────

        /// <summary>
        /// Total number of reference edges produced by <see cref="FileContent"/>:
        /// <list type="bullet">
        ///   <item><description><c>Order</c> → Inherits → <c>BaseOrder</c></description></item>
        ///   <item><description><c>Order</c> → Implements → <c>IEntity</c></description></item>
        ///   <item><description><c>Order</c> → ContainsPropertyOfType → <c>BaseOrder</c> (Parent property)</description></item>
        ///   <item><description><c>Order.GetParent</c> → ReturnsType → <c>BaseOrder</c></description></item>
        ///   <item><description><c>Order.GetParent</c> → CreatesInstanceOf → <c>BaseOrder</c></description></item>
        /// </list>
        /// </summary>
        public const int ExpectedEdgeCount = 5;

        /// <summary>Number of outgoing edges from the <c>Order</c> class symbol.</summary>
        public const int OrderOutgoingEdgeCount = 3;

        /// <summary>Number of incoming edges targeting the <c>BaseOrder</c> class symbol.</summary>
        public const int BaseOrderIncomingEdgeCount = 4;

        // ── Symbol names for lookup in tests ──────────────────────────────────────

        /// <summary>Simple name of the derived class.</summary>
        public const string DerivedClassName = "Order";

        /// <summary>Simple name of the base class.</summary>
        public const string BaseClassName = "BaseOrder";

        /// <summary>Simple name of the interface.</summary>
        public const string InterfaceName = "IEntity";

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

        /// <summary>Relative path of the C# source file written into the repo.</summary>
        public string SampleFilePath => FileName;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the temporary repository, writes <see cref="FileContent"/> as
        /// <c>Orders.cs</c>, starts the in-process test server, and builds the code index.
        /// </summary>
        public ReferenceIndexedFixture()
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

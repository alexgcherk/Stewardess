using System;
using System.IO;

namespace StewardessMCPServive.IntegrationTests.Helpers
{
    /// <summary>
    /// Creates a temporary directory that acts as the repository root during
    /// integration tests.  The directory is deleted automatically on disposal.
    /// </summary>
    internal sealed class TempTestRepository : IDisposable
    {
        /// <summary>Absolute path of the temporary repository root.</summary>
        public string Root { get; }

        /// <summary>Initialises the helper and creates the temporary directory.</summary>
        public TempTestRepository()
        {
            Root = Path.Combine(Path.GetTempPath(), "McpIntTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { /* best effort */ }
        }
    }
}

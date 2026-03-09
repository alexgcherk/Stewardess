// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

namespace StewardessMCPService.IntegrationTests.Helpers;

/// <summary>
///     Creates a temporary directory that acts as the repository root during
///     integration tests.  The directory is deleted automatically on disposal.
/// </summary>
internal sealed class TempTestRepository : IDisposable
{
    /// <summary>Initialises the helper and creates the temporary directory.</summary>
    public TempTestRepository()
    {
        Root = Path.Combine(Path.GetTempPath(), "McpIntTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>Absolute path of the temporary repository root.</summary>
    public string Root { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch
        {
            /* best effort */
        }
    }
}
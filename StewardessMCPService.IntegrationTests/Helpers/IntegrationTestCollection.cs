// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using Xunit;

namespace StewardessMCPService.IntegrationTests.Helpers
{
    /// <summary>
    /// Marks all integration tests as belonging to a single xUnit collection so
    /// they run sequentially.  This prevents multiple <see cref="McpTestServer"/>
    /// instances from overwriting the shared static <c>McpServiceSettings.Instance</c>
    /// concurrently.
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class IntegrationTestCollection : ICollectionFixture<object>
    {
        public const string Name = "Integration";
    }
}

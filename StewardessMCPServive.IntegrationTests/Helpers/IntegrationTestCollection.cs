using Xunit;

namespace StewardessMCPServive.IntegrationTests.Helpers
{
    /// <summary>
    /// Marks all integration tests as belonging to a single xUnit collection so
    /// they run sequentially.  This is required because <see cref="Infrastructure.ServiceLocator"/>
    /// is a static container — concurrent test classes calling Reset() would
    /// corrupt each other's service registrations.
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class IntegrationTestCollection : ICollectionFixture<object>
    {
        public const string Name = "Integration";
    }
}

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("StewardessMCPService.Tests")]
[assembly: InternalsVisibleTo("StewardessMCPService.IntegrationTests")]

// Makes Program accessible to WebApplicationFactory<Program> in integration tests.
public partial class Program { }

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("StewardessMCPServive.Tests")]
[assembly: InternalsVisibleTo("StewardessMCPServive.IntegrationTests")]

// Makes Program accessible to WebApplicationFactory<Program> in integration tests.
public partial class Program { }

// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("StewardessMCPService.Tests")]
[assembly: InternalsVisibleTo("StewardessMCPService.IntegrationTests")]

// Makes Program accessible to WebApplicationFactory<Program> in integration tests.
public partial class Program { }

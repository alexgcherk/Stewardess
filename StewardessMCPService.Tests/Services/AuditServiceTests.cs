// Copyright 2026 Alex Cherkasov
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Configuration;
using StewardessMCPService.Configuration;
using StewardessMCPService.Models;
using StewardessMCPService.Services;
using StewardessMCPService.Tests.Helpers;
using Xunit;

namespace StewardessMCPService.Tests.Services;

/// <summary>
///     Unit tests for <see cref="AuditService" />.
///     Tests write to an isolated temp directory that is deleted after each test.
/// </summary>
public sealed class AuditServiceTests : IDisposable
{
    private readonly TempRepository _repo;

    public AuditServiceTests()
    {
        _repo = new TempRepository();
    }

    public void Dispose()
    {
        _repo.Dispose();
    }

    // ── LogAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LogAsync_Entry_IsPersistedToDisk()
    {
        var (svc, logPath) = BuildEnabled(_repo.Root);
        using (svc)
        {
            var entry = MakeEntry();
            await svc.LogAsync(entry);
        }

        Assert.True(File.Exists(logPath));
        var text = await File.ReadAllTextAsync(logPath);
        Assert.Contains("\"OperationName\"", text);
    }

    [Fact]
    public async Task LogAsync_AutoGeneratesEntryId_WhenEmpty()
    {
        var (svc, logPath) = BuildEnabled(_repo.Root);
        using (svc)
        {
            var entry = MakeEntry();
            entry.EntryId = "";
            await svc.LogAsync(entry);
        }

        var text = await File.ReadAllTextAsync(logPath);
        // EntryId must be non-empty in the persisted JSON
        Assert.Contains("\"EntryId\"", text);
        var line = File.ReadAllLines(logPath)[0];
        var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<AuditEntry>(line)!;
        Assert.False(string.IsNullOrEmpty(deserialized.EntryId));
    }

    [Fact]
    public async Task LogAsync_AutoGeneratesTimestamp_WhenDefault()
    {
        var (svc, logPath) = BuildEnabled(_repo.Root);
        using (svc)
        {
            var entry = MakeEntry();
            entry.Timestamp = default;
            await svc.LogAsync(entry);
        }

        var line = File.ReadAllLines(logPath)[0];
        var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<AuditEntry>(line)!;
        Assert.NotEqual(default, deserialized.Timestamp);
    }

    [Fact]
    public async Task LogAsync_NullEntry_DoesNotThrow()
    {
        var (svc, _) = BuildEnabled(_repo.Root);
        using (svc)
        {
            // Should silently ignore null without throwing.
            await svc.LogAsync(null!);
        }
    }

    [Fact]
    public async Task LogAsync_DisabledAudit_DoesNotWriteFile()
    {
        var svc = BuildDisabled(_repo.Root, out var logPath);
        using (svc)
        {
            await svc.LogAsync(MakeEntry());
        }

        Assert.False(File.Exists(logPath));
    }

    [Fact]
    public async Task LogAsync_MultipleEntries_AllWritten()
    {
        var (svc, logPath) = BuildEnabled(_repo.Root);
        using (svc)
        {
            for (var i = 0; i < 3; i++)
                await svc.LogAsync(MakeEntry($"req-{i}"));
        }

        var lines = File.ReadAllLines(logPath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.Equal(3, lines.Length);
    }

    // ── LogOperationAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task LogOperationAsync_WritesEntryToDisk()
    {
        var (svc, logPath) = BuildEnabled(_repo.Root);
        using (svc)
        {
            await svc.LogOperationAsync(
                requestId: "req-1",
                sessionId: "sess-1",
                operationType: AuditOperationType.ReadFile,
                operationName: "read_file",
                clientIp: "127.0.0.1",
                targetPath: "src/Program.cs",
                outcome: AuditOutcome.Success,
                errorCode: null,
                description: "Read file content",
                elapsedMs: 42);
        }

        Assert.True(File.Exists(logPath));
        var line = File.ReadAllLines(logPath)[0];
        var entry = Newtonsoft.Json.JsonConvert.DeserializeObject<AuditEntry>(line)!;
        Assert.Equal("req-1", entry.RequestId);
        Assert.Equal("sess-1", entry.SessionId);
        Assert.Equal(AuditOperationType.ReadFile, entry.OperationType);
        Assert.Equal(AuditOutcome.Success, entry.Outcome);
        Assert.Equal("src/Program.cs", entry.TargetPath);
        Assert.Equal(42, entry.ElapsedMs);
        Assert.Equal("REST", entry.Source);
        Assert.False(string.IsNullOrEmpty(entry.EntryId));
    }

    [Fact]
    public async Task LogOperationAsync_SetsChangeReasonAndBackupPath()
    {
        var (svc, logPath) = BuildEnabled(_repo.Root);
        using (svc)
        {
            await svc.LogOperationAsync(
                requestId: "r",
                sessionId: null,
                operationType: AuditOperationType.WriteFile,
                operationName: "write_file",
                clientIp: "::1",
                targetPath: "foo.txt",
                outcome: AuditOutcome.Success,
                errorCode: null,
                description: "desc",
                elapsedMs: 10,
                changeReason: "automated refactor",
                backupPath: ".mcp_backups/foo.txt.bak");
        }

        var line = File.ReadAllLines(logPath)[0];
        var entry = Newtonsoft.Json.JsonConvert.DeserializeObject<AuditEntry>(line)!;
        Assert.Equal("automated refactor", entry.ChangeReason);
        Assert.Equal(".mcp_backups/foo.txt.bak", entry.BackupPath);
    }

    // ── QueryAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_EmptyLog_ReturnsEmptyList()
    {
        var (svc, _) = BuildEnabled(_repo.Root);
        using (svc)
        {
            var result = await svc.QueryAsync(new AuditLogQueryRequest());
            Assert.Empty(result.Entries);
            Assert.Equal(0, result.TotalCount);
            Assert.False(result.HasMore);
        }
    }

    [Fact]
    public async Task QueryAsync_DisabledAudit_ReturnsEmptyList()
    {
        var svc = BuildDisabled(_repo.Root, out _);
        using (svc)
        {
            await svc.LogAsync(MakeEntry());
            var result = await svc.QueryAsync(new AuditLogQueryRequest());
            Assert.Empty(result.Entries);
        }
    }

    [Fact]
    public async Task QueryAsync_ReturnsPersistedEntries()
    {
        var (svc, _) = BuildEnabled(_repo.Root);
        using (svc)
        {
            await svc.LogAsync(MakeEntry("req-1"));
            await svc.LogAsync(MakeEntry("req-2"));

            var result = await svc.QueryAsync(new AuditLogQueryRequest { PageSize = 100 });
            Assert.Equal(2, result.TotalCount);
            Assert.Equal(2, result.Entries.Count);
        }
    }

    [Fact]
    public async Task QueryAsync_OrdersDescendingByTimestamp()
    {
        var (svc, _) = BuildEnabled(_repo.Root);
        using (svc)
        {
            var older = MakeEntry("req-old");
            older.Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
            var newer = MakeEntry("req-new");
            newer.Timestamp = DateTimeOffset.UtcNow;

            await svc.LogAsync(older);
            await svc.LogAsync(newer);

            var result = await svc.QueryAsync(new AuditLogQueryRequest { PageSize = 100 });
            Assert.Equal("req-new", result.Entries[0].RequestId);
            Assert.Equal("req-old", result.Entries[1].RequestId);
        }
    }

    [Fact]
    public async Task QueryAsync_FiltersBySince()
    {
        var (svc, _) = BuildEnabled(_repo.Root);
        using (svc)
        {
            var cutoff = DateTimeOffset.UtcNow;

            var old = MakeEntry("req-old");
            old.Timestamp = cutoff.AddMinutes(-10);
            await svc.LogAsync(old);

            var fresh = MakeEntry("req-fresh");
            fresh.Timestamp = cutoff.AddMinutes(1);
            await svc.LogAsync(fresh);

            var result = await svc.QueryAsync(new AuditLogQueryRequest { Since = cutoff, PageSize = 100 });
            Assert.Single(result.Entries);
            Assert.Equal("req-fresh", result.Entries[0].RequestId);
        }
    }

    [Fact]
    public async Task QueryAsync_FiltersByUntil()
    {
        var (svc, _) = BuildEnabled(_repo.Root);
        using (svc)
        {
            var cutoff = DateTimeOffset.UtcNow;

            var old = MakeEntry("req-old");
            old.Timestamp = cutoff.AddMinutes(-5);
            await svc.LogAsync(old);

            var fresh = MakeEntry("req-fresh");
            fresh.Timestamp = cutoff.AddMinutes(5);
            await svc.LogAsync(fresh);

            var result = await svc.QueryAsync(new AuditLogQueryRequest { Until = cutoff, PageSize = 100 });
            Assert.Single(result.Entries);
            Assert.Equal("req-old", result.Entries[0].RequestId);
        }
    }

    [Fact]
    public async Task QueryAsync_FiltersByRequestId()
    {
        var (svc, _) = BuildEnabled(_repo.Root);
        using (svc)
        {
            await svc.LogAsync(MakeEntry("req-A"));
            await svc.LogAsync(MakeEntry("req-B"));

            var result = await svc.QueryAsync(new AuditLogQueryRequest { RequestId = "req-A", PageSize = 100 });
            Assert.Single(result.Entries);
            Assert.Equal("req-A", result.Entries[0].RequestId);
        }
    }

    [Fact]
    public async Task QueryAsync_FiltersBySessionId()
    {
        var (svc, _) = BuildEnabled(_repo.Root);
        using (svc)
        {
            var e1 = MakeEntry("r1");
            e1.SessionId = "session-X";
            await svc.LogAsync(e1);

            var e2 = MakeEntry("r2");
            e2.SessionId = "session-Y";
            await svc.LogAsync(e2);

            var result = await svc.QueryAsync(new AuditLogQueryRequest { SessionId = "session-X", PageSize = 100 });
            Assert.Single(result.Entries);
            Assert.Equal("session-X", result.Entries[0].SessionId);
        }
    }

    [Fact]
    public async Task QueryAsync_FiltersByTargetPath_CaseInsensitive()
    {
        var (svc, _) = BuildEnabled(_repo.Root);
        using (svc)
        {
            var e1 = MakeEntry("r1");
            e1.TargetPath = "src/Program.cs";
            await svc.LogAsync(e1);

            var e2 = MakeEntry("r2");
            e2.TargetPath = "tests/FooTests.cs";
            await svc.LogAsync(e2);

            // Case-insensitive contains match
            var result = await svc.QueryAsync(new AuditLogQueryRequest { TargetPath = "PROGRAM", PageSize = 100 });
            Assert.Single(result.Entries);
            Assert.Equal("r1", result.Entries[0].RequestId);
        }
    }

    [Fact]
    public async Task QueryAsync_FiltersByOperationType()
    {
        var (svc, _) = BuildEnabled(_repo.Root);
        using (svc)
        {
            var e1 = MakeEntry("r1");
            e1.OperationType = AuditOperationType.ReadFile;
            await svc.LogAsync(e1);

            var e2 = MakeEntry("r2");
            e2.OperationType = AuditOperationType.WriteFile;
            await svc.LogAsync(e2);

            var result = await svc.QueryAsync(
                new AuditLogQueryRequest { OperationType = AuditOperationType.WriteFile, PageSize = 100 });
            Assert.Single(result.Entries);
            Assert.Equal(AuditOperationType.WriteFile, result.Entries[0].OperationType);
        }
    }

    [Fact]
    public async Task QueryAsync_FiltersByOutcome()
    {
        var (svc, _) = BuildEnabled(_repo.Root);
        using (svc)
        {
            var ok = MakeEntry("r1");
            ok.Outcome = AuditOutcome.Success;
            await svc.LogAsync(ok);

            var fail = MakeEntry("r2");
            fail.Outcome = AuditOutcome.Failure;
            await svc.LogAsync(fail);

            var result = await svc.QueryAsync(
                new AuditLogQueryRequest { Outcome = AuditOutcome.Failure, PageSize = 100 });
            Assert.Single(result.Entries);
            Assert.Equal(AuditOutcome.Failure, result.Entries[0].Outcome);
        }
    }

    [Fact]
    public async Task QueryAsync_PaginatesResults()
    {
        var (svc, _) = BuildEnabled(_repo.Root);
        using (svc)
        {
            for (var i = 0; i < 5; i++)
                await svc.LogAsync(MakeEntry($"req-{i}"));

            var page0 = await svc.QueryAsync(new AuditLogQueryRequest { PageSize = 2, PageIndex = 0 });
            var page1 = await svc.QueryAsync(new AuditLogQueryRequest { PageSize = 2, PageIndex = 1 });
            var page2 = await svc.QueryAsync(new AuditLogQueryRequest { PageSize = 2, PageIndex = 2 });

            Assert.Equal(5, page0.TotalCount);
            Assert.Equal(2, page0.Entries.Count);
            Assert.True(page0.HasMore);

            Assert.Equal(2, page1.Entries.Count);
            Assert.True(page1.HasMore);

            Assert.Single(page2.Entries);
            Assert.False(page2.HasMore);
        }
    }

    [Fact]
    public async Task QueryAsync_SkipsMalformedLines()
    {
        var (svc, logPath) = BuildEnabled(_repo.Root);
        using (svc)
        {
            await svc.LogAsync(MakeEntry("req-good"));
        }

        // Append a corrupt line directly
        await File.AppendAllTextAsync(logPath, "NOT_VALID_JSON\n");

        var (svc2, _) = BuildEnabledWithPath(_repo.Root, logPath);
        using (svc2)
        {
            // Should not throw; the good entry is returned, the bad one is skipped.
            var result = await svc2.QueryAsync(new AuditLogQueryRequest { PageSize = 100 });
            Assert.Single(result.Entries);
            Assert.Equal("req-good", result.Entries[0].RequestId);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (AuditService service, string logPath) BuildEnabled(string root)
    {
        var logPath = Path.Combine(root, "audit-test.log");
        return (BuildEnabledWithPath(root, logPath).service, logPath);
    }

    private static (AuditService service, string logPath) BuildEnabledWithPath(string root, string logPath)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:RepositoryRoot"] = root,
                ["Mcp:EnableAuditLog"] = "true",
                ["Mcp:AuditLogPath"] = logPath
            })
            .Build();
        var settings = new McpServiceSettings(config);
        return (new AuditService(settings), logPath);
    }

    private static AuditService BuildDisabled(string root, out string logPath)
    {
        logPath = Path.Combine(root, "audit-disabled.log");
        var svc = new AuditService(McpServiceSettings.CreateForTesting(root));
        return svc;
    }

    private static AuditEntry MakeEntry(string requestId = "req-default") =>
        new()
        {
            EntryId = Guid.NewGuid().ToString("N"),
            RequestId = requestId,
            Timestamp = DateTimeOffset.UtcNow,
            OperationType = AuditOperationType.ReadFile,
            Source = "REST",
            OperationName = "read_file",
            ClientIp = "127.0.0.1",
            TargetPath = "src/File.cs",
            Outcome = AuditOutcome.Success,
            Description = "Test entry"
        };
}

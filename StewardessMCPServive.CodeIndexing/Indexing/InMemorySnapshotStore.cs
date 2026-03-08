using System.Collections.Concurrent;
using StewardessMCPServive.CodeIndexing.Model.Snapshots;
using StewardessMCPServive.CodeIndexing.Snapshots;

namespace StewardessMCPServive.CodeIndexing.Indexing;

/// <summary>
/// Thread-safe in-memory snapshot store.
/// Retains the most recent snapshot per root path, plus a global ID lookup.
/// </summary>
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    // rootPath (normalized) → latest snapshot
    private readonly ConcurrentDictionary<string, IndexSnapshot> _latestByRoot = new(StringComparer.OrdinalIgnoreCase);
    // snapshotId → snapshot
    private readonly ConcurrentDictionary<string, IndexSnapshot> _byId = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task SaveSnapshotAsync(IndexSnapshot snapshot, CancellationToken ct = default)
    {
        var root = NormalizeRoot(snapshot.Metadata.RootPath);
        _latestByRoot[root] = snapshot;
        _byId[snapshot.Metadata.SnapshotId] = snapshot;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IndexSnapshot?> GetLatestSnapshotAsync(string rootPath, CancellationToken ct = default)
    {
        _latestByRoot.TryGetValue(NormalizeRoot(rootPath), out var snapshot);
        return Task.FromResult(snapshot);
    }

    /// <inheritdoc/>
    public Task<IndexSnapshot?> GetSnapshotByIdAsync(string snapshotId, CancellationToken ct = default)
    {
        _byId.TryGetValue(snapshotId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SnapshotMetadata>> ListSnapshotsAsync(string rootPath, CancellationToken ct = default)
    {
        IReadOnlyList<SnapshotMetadata> result = _latestByRoot.TryGetValue(NormalizeRoot(rootPath), out var snap)
            ? [snap.Metadata]
            : [];
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task DeleteSnapshotAsync(string snapshotId, CancellationToken ct = default)
    {
        if (_byId.TryRemove(snapshotId, out var snapshot))
        {
            var root = NormalizeRoot(snapshot.Metadata.RootPath);
            if (_latestByRoot.TryGetValue(root, out var latest) &&
                latest.Metadata.SnapshotId == snapshotId)
            {
                _latestByRoot.TryRemove(root, out _);
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListRootPathsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> roots = _latestByRoot.Keys.ToList();
        return Task.FromResult(roots);
    }

    /// <inheritdoc/>
    public Task<int> ClearRepositoryAsync(string rootPath, CancellationToken ct = default)
    {
        var normalizedRoot = NormalizeRoot(rootPath);
        int removed = 0;

        if (_latestByRoot.TryRemove(normalizedRoot, out var snapshot))
        {
            _byId.TryRemove(snapshot.Metadata.SnapshotId, out _);
            removed++;
        }

        return Task.FromResult(removed);
    }

    private static string NormalizeRoot(string rootPath) =>
        rootPath.TrimEnd('/', '\\').Replace('\\', '/');
}

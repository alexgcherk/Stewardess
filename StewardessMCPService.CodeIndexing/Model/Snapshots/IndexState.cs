namespace StewardessMCPService.CodeIndexing.Model.Snapshots;

/// <summary>
/// Current operational state of the indexing engine for a repository root.
/// </summary>
public enum IndexState
{
    /// <summary>No index has been built for this root path.</summary>
    NotIndexed,

    /// <summary>An initial full build is in progress.</summary>
    Building,

    /// <summary>An incremental update is in progress.</summary>
    Updating,

    /// <summary>A consistent snapshot is published and available for queries.</summary>
    Ready,

    /// <summary>The last build or update failed.</summary>
    Failed,
}

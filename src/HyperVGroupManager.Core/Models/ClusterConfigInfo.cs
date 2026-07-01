namespace HyperVGroupManager.Core.Models;

public sealed record ClusterConfigInfo
{
    public required bool IsCluster { get; init; }
    public string? ConfigStoreRootPath { get; init; }
    public string? Message { get; init; }
}

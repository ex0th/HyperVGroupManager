namespace HyperVGroupManager.Core.Models;

/// <summary>
/// Beschreibt eine native Hyper-V-VM-Gruppe (MVP: ausschließlich VMCollectionType).
/// </summary>
public sealed record VmGroupInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string GroupType { get; init; }
    public int MemberCount { get; init; }
    public IReadOnlyList<Guid> MemberVmIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<string> MemberVmNames { get; init; } = Array.Empty<string>();
}

namespace HyperVGroupManager.Core.Models;

/// <summary>
/// Eine einzelne geplante Änderung in der Change-Queue (noch nicht angewendet).
/// Bei reinen Gruppenoperationen (CreateGroup/RenameGroup/DeleteGroup) bleibt VmId leer.
/// </summary>
public sealed record VmGroupMembershipChange
{
    public required VmGroupChangeType ChangeType { get; init; }
    public Guid VmId { get; init; }
    public string? VmName { get; init; }
    public required Guid GroupId { get; init; }
    public required string GroupName { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public required string Description { get; init; }
}

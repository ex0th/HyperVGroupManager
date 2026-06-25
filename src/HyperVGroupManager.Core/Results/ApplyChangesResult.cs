using HyperVGroupManager.Core.Models;

namespace HyperVGroupManager.Core.Results;

/// <summary>
/// Ergebnis einer einzelnen Änderung innerhalb eines Änderungslaufs (entspricht einem
/// Eintrag aus Invoke-HVGMChangeSet). VmId/GroupId erlauben es, das Ergebnis eindeutig
/// der ursprünglich geplanten VmGroupMembershipChange zuzuordnen.
/// </summary>
public sealed record ChangeApplicationResult
{
    public required VmGroupChangeType ChangeType { get; init; }
    public Guid VmId { get; init; }
    public Guid GroupId { get; init; }
    public required string Description { get; init; }
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Gesamtergebnis eines Änderungslaufs mit den Einzelergebnissen pro geplanter Änderung,
/// damit die UI anzeigen kann, welche Änderungen bereits angewendet wurden und welche nicht.
/// </summary>
public sealed record ApplyChangesResult
{
    public required bool Success { get; init; }
    public required IReadOnlyList<ChangeApplicationResult> Results { get; init; }
}

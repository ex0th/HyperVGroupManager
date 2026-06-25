namespace HyperVGroupManager.Core.Models;

public sealed record ConfigurationExport
{
    public required string TargetName { get; init; }
    public required DateTime ExportedAt { get; init; }
    public required IReadOnlyList<ConfigurationExportGroup> Groups { get; init; }
}

public sealed record ConfigurationExportGroup
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string GroupType { get; init; }
    public required IReadOnlyList<ConfigurationExportMember> Members { get; init; }
}

public sealed record ConfigurationExportMember
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}

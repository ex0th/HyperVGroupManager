namespace HyperVGroupManager.Core.Models;

/// <summary>
/// Beschreibt eine Hyper-V-VM, wie sie von einem Host oder Cluster gelesen wurde.
/// </summary>
public sealed record VirtualMachineInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string ComputerName { get; init; }

    // Im Einzelhost-Fall identisch mit ComputerName, im Cluster der aktuelle Owner-Node der VM.
    public required string OwnerNode { get; init; }

    public required string State { get; init; }
    public bool IsClustered { get; init; }
    public IReadOnlyList<string> GroupNames { get; init; } = Array.Empty<string>();
}

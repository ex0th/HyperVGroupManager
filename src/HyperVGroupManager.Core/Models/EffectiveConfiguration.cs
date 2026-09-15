namespace HyperVGroupManager.Core.Models;

/// <summary>
/// Der erwartete Zustand nach Anwendung aller geplanten Änderungen.
/// </summary>
public sealed record EffectiveConfiguration
{
    public IReadOnlyList<VirtualMachineInfo> VirtualMachines { get; init; } = Array.Empty<VirtualMachineInfo>();
    public IReadOnlyList<VmGroupInfo> Groups { get; init; } = Array.Empty<VmGroupInfo>();
}

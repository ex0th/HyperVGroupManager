using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Results;

namespace HyperVGroupManager.Core.Interfaces;

/// <summary>
/// Fachlogik-Abstraktion für die Verwaltung von Hyper-V-VM-Gruppen.
/// Die ViewModels kennen nur dieses Interface, nicht die konkrete (PowerShell-)Implementierung.
/// </summary>
public interface IHyperVGroupService
{
    Task<EnvironmentInfo> TestEnvironmentAsync(string targetName, CancellationToken cancellationToken);

    Task<IReadOnlyList<VirtualMachineInfo>> GetVirtualMachinesAsync(string targetName, CancellationToken cancellationToken);

    Task<IReadOnlyList<VmGroupInfo>> GetGroupsAsync(string targetName, CancellationToken cancellationToken);

    Task CreateGroupAsync(string targetName, string groupName, CancellationToken cancellationToken);

    Task RenameGroupAsync(string targetName, Guid groupId, string newName, CancellationToken cancellationToken);

    Task DeleteGroupAsync(string targetName, Guid groupId, CancellationToken cancellationToken);

    Task AddVmToGroupAsync(string targetName, Guid vmId, Guid groupId, CancellationToken cancellationToken);

    Task RemoveVmFromGroupAsync(string targetName, Guid vmId, Guid groupId, CancellationToken cancellationToken);

    Task<ApplyChangesResult> ApplyChangesAsync(string targetName, IReadOnlyList<VmGroupMembershipChange> changes, CancellationToken cancellationToken);
}

using HyperVGroupManager.Core.Models;

namespace HyperVGroupManager.Core.Services;

/// <summary>
/// Projiziert den zuletzt gelesenen Serverzustand plus Change-Queue auf einen erwarteten
/// Zustand. Die Berechnung verändert die eingehenden Snapshots nicht.
/// </summary>
public static class EffectiveConfigurationBuilder
{
    public static EffectiveConfiguration Build(
        IEnumerable<VirtualMachineInfo> virtualMachines,
        IEnumerable<VmGroupInfo> groups,
        IEnumerable<VmGroupMembershipChange> changes)
    {
        ArgumentNullException.ThrowIfNull(virtualMachines);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(changes);

        var vmById = virtualMachines.ToDictionary(vm => vm.Id);
        var groupById = groups.ToDictionary(group => group.Id);
        var memberIdsByGroup = groupById.ToDictionary(
            pair => pair.Key,
            pair => new HashSet<Guid>(pair.Value.MemberVmIds));

        // Ältere Backend-Antworten können nur GroupNames an den VMs enthalten. Diese
        // Information wird ebenfalls in IDs überführt, damit die Vorschau korrekt bleibt.
        foreach (var vm in vmById.Values)
        {
            foreach (var groupName in vm.GroupNames)
            {
                var matchingGroup = groupById.Values.FirstOrDefault(group =>
                    string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase));
                if (matchingGroup is not null)
                {
                    memberIdsByGroup[matchingGroup.Id].Add(vm.Id);
                }
            }
        }

        foreach (var change in changes.OrderBy(change => VmGroupChangeQueue.GetExecutionPriority(change.ChangeType)))
        {
            switch (change.ChangeType)
            {
                case VmGroupChangeType.CreateGroup:
                    groupById[change.GroupId] = new VmGroupInfo
                    {
                        Id = change.GroupId,
                        Name = change.GroupName,
                        GroupType = "VMCollectionType",
                    };
                    memberIdsByGroup[change.GroupId] = new HashSet<Guid>();
                    break;

                case VmGroupChangeType.RenameGroup when groupById.TryGetValue(change.GroupId, out var group):
                    groupById[change.GroupId] = group with { Name = change.GroupName };
                    break;

                case VmGroupChangeType.AddMembership when memberIdsByGroup.TryGetValue(change.GroupId, out var addMembers):
                    addMembers.Add(change.VmId);
                    break;

                case VmGroupChangeType.RemoveMembership when memberIdsByGroup.TryGetValue(change.GroupId, out var removeMembers):
                    removeMembers.Remove(change.VmId);
                    break;

                case VmGroupChangeType.DeleteGroup:
                    groupById.Remove(change.GroupId);
                    memberIdsByGroup.Remove(change.GroupId);
                    break;
            }
        }

        var effectiveGroups = groupById.Values
            .Select(group =>
            {
                var memberIds = memberIdsByGroup.GetValueOrDefault(group.Id) ?? new HashSet<Guid>();
                var validMemberIds = memberIds.Where(vmById.ContainsKey).OrderBy(id => id).ToArray();
                return group with
                {
                    MemberCount = validMemberIds.Length,
                    MemberVmIds = validMemberIds,
                    MemberVmNames = validMemberIds.Select(id => vmById[id].Name)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                };
            })
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var groupNamesByVm = effectiveGroups
            .SelectMany(group => group.MemberVmIds.Select(vmId => (vmId, group.Name)))
            .GroupBy(item => item.vmId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => item.Name)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        var effectiveVms = vmById.Values
            .Select(vm => vm with
            {
                GroupNames = groupNamesByVm.GetValueOrDefault(vm.Id) ?? Array.Empty<string>(),
            })
            .OrderBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new EffectiveConfiguration
        {
            VirtualMachines = effectiveVms,
            Groups = effectiveGroups,
        };
    }

}

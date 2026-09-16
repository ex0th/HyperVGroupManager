using HyperVGroupManager.Core.Models;

namespace HyperVGroupManager.App.ViewModels;

/// <summary>
/// UI projection of a VM including the delta between the last server snapshot and
/// the locally planned effective state.
/// </summary>
public sealed class VirtualMachineRowViewModel
{
    public VirtualMachineRowViewModel(
        VirtualMachineInfo source,
        IReadOnlyList<string> addedGroupNames,
        IReadOnlyList<string> removedGroupNames,
        IReadOnlyList<string> renamedGroups)
    {
        Source = source;
        AddedGroupNames = addedGroupNames;
        RemovedGroupNames = removedGroupNames;
        RenamedGroups = renamedGroups;
    }

    public VirtualMachineInfo Source { get; }
    public Guid Id => Source.Id;
    public string Name => Source.Name;
    public string State => Source.State;
    public string OwnerNode => Source.OwnerNode;
    public IReadOnlyList<string> GroupNames => Source.GroupNames;
    public IReadOnlyList<string> AddedGroupNames { get; }
    public IReadOnlyList<string> RemovedGroupNames { get; }
    public IReadOnlyList<string> RenamedGroups { get; }
    public bool HasAdditions => AddedGroupNames.Count > 0;
    public bool HasRemovals => RemovedGroupNames.Count > 0;
    public bool HasRenames => RenamedGroups.Count > 0;
    public bool IsModified => HasAdditions || HasRemovals || HasRenames;
    public string AddedGroupsDisplay => "+ " + string.Join(", ", AddedGroupNames);
    public string RemovedGroupsDisplay => "− " + string.Join(", ", RemovedGroupNames);
    public string RenamedGroupsDisplay => "↔ " + string.Join(", ", RenamedGroups);
}

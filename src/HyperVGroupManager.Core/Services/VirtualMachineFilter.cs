using HyperVGroupManager.Core.Models;

namespace HyperVGroupManager.Core.Services;

public enum VmFilterMode
{
    All,
    WithoutGroup,
    SelectedGroup,
    Running,
    Off,
}

/// <summary>
/// Reine, UI-unabhängige Filterlogik für die VM-Liste, damit sie ohne WPF testbar ist.
/// </summary>
public static class VirtualMachineFilter
{
    public static IEnumerable<VirtualMachineInfo> Apply(
        IEnumerable<VirtualMachineInfo> virtualMachines,
        VmFilterMode filterMode,
        string? searchText,
        VmGroupInfo? selectedGroup)
    {
        IEnumerable<VirtualMachineInfo> query = filterMode switch
        {
            VmFilterMode.WithoutGroup => virtualMachines.Where(vm => vm.GroupNames.Count == 0),
            VmFilterMode.SelectedGroup => selectedGroup is null
                ? Enumerable.Empty<VirtualMachineInfo>()
                : virtualMachines.Where(vm => vm.GroupNames.Contains(selectedGroup.Name, StringComparer.OrdinalIgnoreCase)),
            VmFilterMode.Running => virtualMachines.Where(vm => string.Equals(vm.State, "Running", StringComparison.OrdinalIgnoreCase)),
            VmFilterMode.Off => virtualMachines.Where(vm => string.Equals(vm.State, "Off", StringComparison.OrdinalIgnoreCase)),
            _ => virtualMachines,
        };

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var trimmedSearchText = searchText.Trim();
            query = query.Where(vm => vm.Name.Contains(trimmedSearchText, StringComparison.OrdinalIgnoreCase));
        }

        return query;
    }
}

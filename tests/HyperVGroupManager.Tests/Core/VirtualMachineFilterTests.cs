using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Services;

namespace HyperVGroupManager.Tests.Core;

public class VirtualMachineFilterTests
{
    private static VirtualMachineInfo Vm(string name, string state, params string[] groupNames) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ComputerName = "HOST01",
        OwnerNode = "HOST01",
        State = state,
        GroupNames = groupNames,
    };

    [Fact]
    public void Apply_WithoutGroup_ReturnsOnlyUngroupedVms()
    {
        var vms = new[]
        {
            Vm("VM1", "Running", "GroupA"),
            Vm("VM2", "Running"),
        };

        var result = VirtualMachineFilter.Apply(vms, VmFilterMode.WithoutGroup, null, null).ToList();

        Assert.Single(result);
        Assert.Equal("VM2", result[0].Name);
    }

    [Fact]
    public void Apply_Running_ReturnsOnlyRunningVms()
    {
        var vms = new[] { Vm("VM1", "Running"), Vm("VM2", "Off") };

        var result = VirtualMachineFilter.Apply(vms, VmFilterMode.Running, null, null).ToList();

        Assert.Single(result);
        Assert.Equal("VM1", result[0].Name);
    }

    [Fact]
    public void Apply_SelectedGroup_WithoutSelection_ReturnsEmpty()
    {
        var vms = new[] { Vm("VM1", "Running", "GroupA") };

        var result = VirtualMachineFilter.Apply(vms, VmFilterMode.SelectedGroup, null, selectedGroup: null);

        Assert.Empty(result);
    }

    [Fact]
    public void Apply_SelectedGroup_ReturnsOnlyMembersOfThatGroup()
    {
        var vms = new[] { Vm("VM1", "Running", "GroupA"), Vm("VM2", "Running", "GroupB") };
        var selectedGroup = new VmGroupInfo { Id = Guid.NewGuid(), Name = "GroupA", GroupType = "VMCollectionType" };

        var result = VirtualMachineFilter.Apply(vms, VmFilterMode.SelectedGroup, null, selectedGroup).ToList();

        Assert.Single(result);
        Assert.Equal("VM1", result[0].Name);
    }

    [Fact]
    public void Apply_SearchText_FiltersByNameCaseInsensitive()
    {
        var vms = new[] { Vm("DC01", "Running"), Vm("FileServer01", "Running") };

        var result = VirtualMachineFilter.Apply(vms, VmFilterMode.All, "dc", null).ToList();

        Assert.Single(result);
        Assert.Equal("DC01", result[0].Name);
    }
}

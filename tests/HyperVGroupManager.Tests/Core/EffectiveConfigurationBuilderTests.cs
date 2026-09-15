using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Services;

namespace HyperVGroupManager.Tests.Core;

public class EffectiveConfigurationBuilderTests
{
    [Fact]
    public void Build_CreateAndAddMembership_ProjectsExpectedState()
    {
        var vm = Vm("VM01");
        var groupId = Guid.NewGuid();
        var changes = new[]
        {
            Change(VmGroupChangeType.CreateGroup, groupId, "VEEAM_Daily"),
            Change(VmGroupChangeType.AddMembership, groupId, "VEEAM_Daily", vm),
        };

        var result = EffectiveConfigurationBuilder.Build(new[] { vm }, Array.Empty<VmGroupInfo>(), changes);

        var group = Assert.Single(result.Groups);
        Assert.Equal(1, group.MemberCount);
        Assert.Equal(vm.Id, Assert.Single(group.MemberVmIds));
        Assert.Equal("VEEAM_Daily", Assert.Single(result.VirtualMachines).GroupNames.Single());
    }

    [Fact]
    public void Build_RemoveRenameAndDelete_DoesNotMutateSource()
    {
        var vm = Vm("VM01", "OldName");
        var group = Group("OldName", vm);
        var changes = new[]
        {
            Change(VmGroupChangeType.RenameGroup, group.Id, "NewName"),
            Change(VmGroupChangeType.RemoveMembership, group.Id, "NewName", vm),
            Change(VmGroupChangeType.DeleteGroup, group.Id, "NewName"),
        };

        var result = EffectiveConfigurationBuilder.Build(new[] { vm }, new[] { group }, changes);

        Assert.Empty(result.Groups);
        Assert.Empty(Assert.Single(result.VirtualMachines).GroupNames);
        Assert.Equal("OldName", group.Name);
        Assert.Equal("OldName", Assert.Single(vm.GroupNames));
    }

    private static VirtualMachineInfo Vm(string name, params string[] groupNames) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ComputerName = "HV01",
        OwnerNode = "HV01",
        State = "Running",
        GroupNames = groupNames,
    };

    private static VmGroupInfo Group(string name, params VirtualMachineInfo[] members) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        GroupType = "VMCollectionType",
        MemberCount = members.Length,
        MemberVmIds = members.Select(vm => vm.Id).ToArray(),
        MemberVmNames = members.Select(vm => vm.Name).ToArray(),
    };

    private static VmGroupMembershipChange Change(
        VmGroupChangeType type,
        Guid groupId,
        string groupName,
        VirtualMachineInfo? vm = null) => new()
    {
        ChangeType = type,
        GroupId = groupId,
        GroupName = groupName,
        VmId = vm?.Id ?? Guid.Empty,
        VmName = vm?.Name,
        Description = type.ToString(),
    };
}

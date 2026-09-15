using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Services;

namespace HyperVGroupManager.Tests.Core;

public class ChangeSetValidatorTests
{
    [Fact]
    public void Validate_CreateThenAdd_IsValidForPlaceholderGroup()
    {
        var vm = Vm();
        var groupId = Guid.NewGuid();
        var changes = new[]
        {
            Change(VmGroupChangeType.CreateGroup, groupId),
            Change(VmGroupChangeType.AddMembership, groupId, vm.Id),
        };

        var result = ChangeSetValidator.Validate(new[] { vm }, Array.Empty<VmGroupInfo>(), changes);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_DeleteWithRemainingMember_IsRejected()
    {
        var vm = Vm();
        var group = Group(vm);

        var result = ChangeSetValidator.Validate(
            new[] { vm },
            new[] { group },
            new[] { Change(VmGroupChangeType.DeleteGroup, group.Id) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("verbleiben 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RemoveAllThenDelete_IsValid()
    {
        var vm = Vm();
        var group = Group(vm);
        var changes = new[]
        {
            Change(VmGroupChangeType.RemoveMembership, group.Id, vm.Id),
            Change(VmGroupChangeType.DeleteGroup, group.Id),
        };

        var result = ChangeSetValidator.Validate(new[] { vm }, new[] { group }, changes);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_DuplicateOperation_IsRejected()
    {
        var vm = Vm();
        var group = Group();
        var change = Change(VmGroupChangeType.AddMembership, group.Id, vm.Id);

        var result = ChangeSetValidator.Validate(new[] { vm }, new[] { group }, new[] { change, change });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("mehrfach", StringComparison.Ordinal));
    }

    private static VirtualMachineInfo Vm() => new()
    {
        Id = Guid.NewGuid(),
        Name = "VM01",
        ComputerName = "HV01",
        OwnerNode = "HV01",
        State = "Running",
    };

    private static VmGroupInfo Group(params VirtualMachineInfo[] members) => new()
    {
        Id = Guid.NewGuid(),
        Name = "VEEAM_Daily",
        GroupType = "VMCollectionType",
        MemberCount = members.Length,
        MemberVmIds = members.Select(vm => vm.Id).ToArray(),
        MemberVmNames = members.Select(vm => vm.Name).ToArray(),
    };

    private static VmGroupMembershipChange Change(VmGroupChangeType type, Guid groupId, Guid vmId = default) => new()
    {
        ChangeType = type,
        GroupId = groupId,
        GroupName = "VEEAM_Daily",
        VmId = vmId,
        VmName = vmId == Guid.Empty ? null : "VM01",
        Description = type.ToString(),
    };
}

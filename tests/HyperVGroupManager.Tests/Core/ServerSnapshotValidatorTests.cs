using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Services;

namespace HyperVGroupManager.Tests.Core;

public class ServerSnapshotValidatorTests
{
    [Fact]
    public void Validate_DuplicateVmIds_IsRejected()
    {
        var id = Guid.NewGuid();
        var vms = new[] { Vm(id, "VM01"), Vm(id, "VM02") };

        var result = ServerSnapshotValidator.Validate(vms, Array.Empty<VmGroupInfo>());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("doppelte VM-IDs", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DuplicateGroupNamesIgnoringCase_IsRejected()
    {
        var groups = new[] { Group("VEEAM_Daily"), Group("veeam_daily") };

        var result = ServerSnapshotValidator.Validate(Array.Empty<VirtualMachineInfo>(), groups);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_MemberCountMismatch_IsWarningNotError()
    {
        var vm = Vm(Guid.NewGuid(), "VM01");
        var group = Group("VEEAM_Daily") with { MemberCount = 2, MemberVmIds = new[] { vm.Id } };

        var result = ServerSnapshotValidator.Validate(new[] { vm }, new[] { group });

        Assert.True(result.IsValid);
        Assert.Single(result.Warnings);
    }

    private static VirtualMachineInfo Vm(Guid id, string name) => new()
    {
        Id = id,
        Name = name,
        ComputerName = "HV01",
        OwnerNode = "HV01",
        State = "Running",
    };

    private static VmGroupInfo Group(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        GroupType = "VMCollectionType",
    };
}

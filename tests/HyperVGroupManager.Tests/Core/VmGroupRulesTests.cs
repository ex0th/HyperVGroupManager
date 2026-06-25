using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Services;

namespace HyperVGroupManager.Tests.Core;

public class VmGroupRulesTests
{
    [Fact]
    public void CanDeleteGroup_EmptyGroup_ReturnsTrue()
    {
        var group = new VmGroupInfo
        {
            Id = Guid.NewGuid(),
            Name = "VEEAM_NoBackup",
            GroupType = "VMCollectionType",
            MemberCount = 0,
        };

        Assert.True(VmGroupRules.CanDeleteGroup(group));
    }

    [Fact]
    public void CanDeleteGroup_NonEmptyGroup_ReturnsFalse()
    {
        var group = new VmGroupInfo
        {
            Id = Guid.NewGuid(),
            Name = "VEEAM_Backup_Daily",
            GroupType = "VMCollectionType",
            MemberCount = 2,
            MemberVmIds = new[] { Guid.NewGuid(), Guid.NewGuid() },
        };

        Assert.False(VmGroupRules.CanDeleteGroup(group));
    }

    [Fact]
    public void BuildNonEmptyGroupDeletionMessage_ContainsGroupNameAndMemberCount()
    {
        var group = new VmGroupInfo
        {
            Id = Guid.NewGuid(),
            Name = "VEEAM_Backup_Daily",
            GroupType = "VMCollectionType",
            MemberCount = 3,
        };

        var message = VmGroupRules.BuildNonEmptyGroupDeletionMessage(group);

        Assert.Contains("VEEAM_Backup_Daily", message);
        Assert.Contains("3", message);
    }
}

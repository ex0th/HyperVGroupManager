using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Services;

namespace HyperVGroupManager.Tests.Core;

public class VmGroupChangeQueueTests
{
    private static VmGroupMembershipChange AddMembership(Guid vmId, Guid groupId, string vmName = "VM01", string groupName = "Group01") =>
        new()
        {
            ChangeType = VmGroupChangeType.AddMembership,
            VmId = vmId,
            VmName = vmName,
            GroupId = groupId,
            GroupName = groupName,
            Description = $"{vmName} zu {groupName} hinzufügen",
        };

    private static VmGroupMembershipChange RemoveMembership(Guid vmId, Guid groupId, string vmName = "VM01", string groupName = "Group01") =>
        new()
        {
            ChangeType = VmGroupChangeType.RemoveMembership,
            VmId = vmId,
            VmName = vmName,
            GroupId = groupId,
            GroupName = groupName,
            Description = $"{vmName} aus {groupName} entfernen",
        };

    [Fact]
    public void Add_IdenticalChangeTwice_IsIgnoredAsDuplicate()
    {
        var queue = new VmGroupChangeQueue();
        var vmId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var firstResult = queue.Add(AddMembership(vmId, groupId));
        var secondResult = queue.Add(AddMembership(vmId, groupId));

        Assert.Equal(ChangeQueueAddResult.Added, firstResult);
        Assert.Equal(ChangeQueueAddResult.DuplicateIgnored, secondResult);
        Assert.Single(queue.Changes);
    }

    [Fact]
    public void Add_AddThenRemoveSameVmAndGroup_CancelOutAndQueueIsEmpty()
    {
        var queue = new VmGroupChangeQueue();
        var vmId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        queue.Add(AddMembership(vmId, groupId));
        var result = queue.Add(RemoveMembership(vmId, groupId));

        Assert.Equal(ChangeQueueAddResult.CancelledOut, result);
        Assert.Empty(queue.Changes);
    }

    [Fact]
    public void Add_AddMembershipToTwoDifferentGroups_BothArePreserved()
    {
        var queue = new VmGroupChangeQueue();
        var vmId = Guid.NewGuid();
        var groupAId = Guid.NewGuid();
        var groupBId = Guid.NewGuid();

        queue.Add(AddMembership(vmId, groupAId, groupName: "GroupA"));
        queue.Add(AddMembership(vmId, groupBId, groupName: "GroupB"));

        Assert.Equal(2, queue.Changes.Count);
    }

    [Fact]
    public void Add_CreateGroupThenDeleteSameGroup_CancelOut()
    {
        var queue = new VmGroupChangeQueue();
        var groupId = Guid.NewGuid();

        queue.Add(new VmGroupMembershipChange
        {
            ChangeType = VmGroupChangeType.CreateGroup,
            GroupId = groupId,
            GroupName = "VEEAM_NewGroup",
            Description = "Gruppe VEEAM_NewGroup erstellen",
        });

        var result = queue.Add(new VmGroupMembershipChange
        {
            ChangeType = VmGroupChangeType.DeleteGroup,
            GroupId = groupId,
            GroupName = "VEEAM_NewGroup",
            Description = "Gruppe VEEAM_NewGroup löschen",
        });

        Assert.Equal(ChangeQueueAddResult.CancelledOut, result);
        Assert.Empty(queue.Changes);
    }

    [Fact]
    public void GetInExecutionOrder_ReturnsChangesInRecommendedOrder()
    {
        var queue = new VmGroupChangeQueue();
        var vmId = Guid.NewGuid();
        var groupForAddId = Guid.NewGuid();
        var groupForRemoveId = Guid.NewGuid();
        var otherGroupId = Guid.NewGuid();

        queue.Add(RemoveMembership(vmId, groupForRemoveId));
        queue.Add(new VmGroupMembershipChange
        {
            ChangeType = VmGroupChangeType.DeleteGroup,
            GroupId = otherGroupId,
            GroupName = "OldGroup",
            Description = "Gruppe OldGroup löschen",
        });
        queue.Add(AddMembership(vmId, groupForAddId));
        queue.Add(new VmGroupMembershipChange
        {
            ChangeType = VmGroupChangeType.CreateGroup,
            GroupId = Guid.NewGuid(),
            GroupName = "NewGroup",
            Description = "Gruppe NewGroup erstellen",
        });

        var ordered = queue.GetInExecutionOrder();

        Assert.Equal(VmGroupChangeType.CreateGroup, ordered[0].ChangeType);
        Assert.Equal(VmGroupChangeType.AddMembership, ordered[1].ChangeType);
        Assert.Equal(VmGroupChangeType.RemoveMembership, ordered[2].ChangeType);
        Assert.Equal(VmGroupChangeType.DeleteGroup, ordered[3].ChangeType);
    }
}

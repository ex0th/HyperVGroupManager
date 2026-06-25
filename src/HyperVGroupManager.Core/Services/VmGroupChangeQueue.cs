using HyperVGroupManager.Core.Models;

namespace HyperVGroupManager.Core.Services;

public enum ChangeQueueAddResult
{
    Added,
    DuplicateIgnored,
    CancelledOut,
}

/// <summary>
/// Sammelt geplante Änderungen (Add/Remove-Mitgliedschaft, Create/Rename/Delete-Gruppe),
/// bevor sie tatsächlich angewendet werden. Verhindert doppelte und sich
/// widersprechende Einträge in der Queue.
/// </summary>
public sealed class VmGroupChangeQueue
{
    private readonly List<VmGroupMembershipChange> _changes = new();

    public IReadOnlyList<VmGroupMembershipChange> Changes => _changes;

    public ChangeQueueAddResult Add(VmGroupMembershipChange change)
    {
        if (_changes.Any(existing => IsSameChange(existing, change)))
        {
            return ChangeQueueAddResult.DuplicateIgnored;
        }

        var opposite = FindOpposite(change);
        if (opposite is not null)
        {
            _changes.Remove(opposite);
            return ChangeQueueAddResult.CancelledOut;
        }

        _changes.Add(change);
        return ChangeQueueAddResult.Added;
    }

    public void Remove(VmGroupMembershipChange change) => _changes.Remove(change);

    public void Clear() => _changes.Clear();

    /// <summary>
    /// Liefert die Änderungen in der empfohlenen Ausführungsreihenfolge:
    /// Gruppen erstellen -> umbenennen -> Mitgliedschaften hinzufügen -> entfernen -> Gruppen löschen.
    /// </summary>
    public IReadOnlyList<VmGroupMembershipChange> GetInExecutionOrder()
    {
        return _changes
            .OrderBy(change => ExecutionOrder(change.ChangeType))
            .ToList();
    }

    private static int ExecutionOrder(VmGroupChangeType changeType) => changeType switch
    {
        VmGroupChangeType.CreateGroup => 0,
        VmGroupChangeType.RenameGroup => 1,
        VmGroupChangeType.AddMembership => 2,
        VmGroupChangeType.RemoveMembership => 3,
        VmGroupChangeType.DeleteGroup => 4,
        _ => int.MaxValue,
    };

    private static bool IsSameChange(VmGroupMembershipChange a, VmGroupMembershipChange b)
    {
        if (a.ChangeType != b.ChangeType)
        {
            return false;
        }

        return a.ChangeType switch
        {
            VmGroupChangeType.AddMembership or VmGroupChangeType.RemoveMembership =>
                a.VmId == b.VmId && a.GroupId == b.GroupId,
            VmGroupChangeType.CreateGroup or VmGroupChangeType.DeleteGroup or VmGroupChangeType.RenameGroup =>
                a.GroupId == b.GroupId,
            _ => false,
        };
    }

    private VmGroupMembershipChange? FindOpposite(VmGroupMembershipChange change)
    {
        var oppositeType = change.ChangeType switch
        {
            VmGroupChangeType.AddMembership => VmGroupChangeType.RemoveMembership,
            VmGroupChangeType.RemoveMembership => VmGroupChangeType.AddMembership,
            VmGroupChangeType.CreateGroup => VmGroupChangeType.DeleteGroup,
            VmGroupChangeType.DeleteGroup => VmGroupChangeType.CreateGroup,
            _ => (VmGroupChangeType?)null,
        };

        if (oppositeType is null)
        {
            return null;
        }

        return change.ChangeType switch
        {
            VmGroupChangeType.AddMembership or VmGroupChangeType.RemoveMembership =>
                _changes.FirstOrDefault(c => c.ChangeType == oppositeType && c.VmId == change.VmId && c.GroupId == change.GroupId),
            VmGroupChangeType.CreateGroup or VmGroupChangeType.DeleteGroup =>
                _changes.FirstOrDefault(c => c.ChangeType == oppositeType && c.GroupId == change.GroupId),
            _ => null,
        };
    }
}

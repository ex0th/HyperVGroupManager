using HyperVGroupManager.Core.Models;

namespace HyperVGroupManager.Core.Services;

public enum ChangeQueueAddResult
{
    Added,
    Updated,
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
        ArgumentNullException.ThrowIfNull(change);

        if (change.ChangeType == VmGroupChangeType.RenameGroup)
        {
            var existingRename = _changes.FirstOrDefault(existing =>
                existing.ChangeType == VmGroupChangeType.RenameGroup && existing.GroupId == change.GroupId);

            if (existingRename is not null)
            {
                if (string.Equals(existingRename.GroupName, change.GroupName, StringComparison.Ordinal))
                {
                    return ChangeQueueAddResult.DuplicateIgnored;
                }

                _changes[_changes.IndexOf(existingRename)] = change;
                return ChangeQueueAddResult.Updated;
            }
        }

        if (_changes.Any(existing => IsSameChange(existing, change)))
        {
            return ChangeQueueAddResult.DuplicateIgnored;
        }

        var opposite = FindOpposite(change);
        if (opposite is not null)
        {
            _changes.Remove(opposite);

            // Wird eine noch nicht angelegte Gruppe wieder verworfen, dürfen keine
            // abhängigen Änderungen mit ihrer clientseitigen Platzhalter-ID übrig bleiben.
            if (change.ChangeType == VmGroupChangeType.DeleteGroup &&
                opposite.ChangeType == VmGroupChangeType.CreateGroup)
            {
                _changes.RemoveAll(existing => existing.GroupId == change.GroupId);
            }

            return ChangeQueueAddResult.CancelledOut;
        }

        if (change.ChangeType == VmGroupChangeType.DeleteGroup)
        {
            // Umbenennen und Hinzufügen wären vor einem Löschen wirkungslos. Geplante
            // Entfernungen bleiben bestehen, da sie die Gruppe erst löschbar machen.
            _changes.RemoveAll(existing =>
                existing.GroupId == change.GroupId &&
                existing.ChangeType is VmGroupChangeType.RenameGroup or VmGroupChangeType.AddMembership);
        }

        _changes.Add(change);
        return ChangeQueueAddResult.Added;
    }

    public void Remove(VmGroupMembershipChange change) => _changes.Remove(change);

    public void Clear() => _changes.Clear();

    public void ReplaceAll(IEnumerable<VmGroupMembershipChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        _changes.Clear();
        foreach (var change in changes)
        {
            Add(change);
        }
    }

    /// <summary>
    /// Liefert die Änderungen in der empfohlenen Ausführungsreihenfolge:
    /// Gruppen erstellen -> umbenennen -> Mitgliedschaften hinzufügen -> entfernen -> Gruppen löschen.
    /// </summary>
    public IReadOnlyList<VmGroupMembershipChange> GetInExecutionOrder()
    {
        return _changes
            .OrderBy(change => GetExecutionPriority(change.ChangeType))
            .ToList();
    }

    public static int GetExecutionPriority(VmGroupChangeType changeType) => changeType switch
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

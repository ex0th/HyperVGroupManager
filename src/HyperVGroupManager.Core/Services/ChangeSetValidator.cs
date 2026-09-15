using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Results;

namespace HyperVGroupManager.Core.Services;

/// <summary>
/// Prüft einen Änderungssatz vollständig gegen den zuletzt gelesenen Zustand, bevor
/// irgendeine schreibende Operation das PowerShell-Backend erreicht.
/// </summary>
public static class ChangeSetValidator
{
    public const int MaximumChangeCount = 5000;

    public static ChangeSetValidationResult ValidateStructure(IReadOnlyList<VmGroupMembershipChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var errors = new List<string>();
        var warnings = new List<string>();

        if (changes.Count == 0)
        {
            errors.Add("Der Änderungssatz ist leer.");
        }

        if (changes.Count > MaximumChangeCount)
        {
            errors.Add($"Der Änderungssatz enthält {changes.Count} Einträge; maximal erlaubt sind {MaximumChangeCount}.");
        }

        var seenOperations = new HashSet<(VmGroupChangeType Type, Guid VmId, Guid GroupId)>();
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            if (change is null)
            {
                errors.Add($"Änderung {index + 1} ist leer.");
                continue;
            }

            var label = $"Änderung {index + 1} ({change.ChangeType})";

            if (change.GroupId == Guid.Empty)
            {
                errors.Add($"{label}: Die Gruppen-ID ist leer.");
            }

            if (string.IsNullOrWhiteSpace(change.GroupName))
            {
                errors.Add($"{label}: Der Gruppenname ist leer.");
            }
            else
            {
                var nameValidation = GroupNameValidator.Validate(change.GroupName, Array.Empty<string>());
                if (!nameValidation.IsValid)
                {
                    errors.Add($"{label}: {nameValidation.ErrorMessage}");
                }
                else if (!string.Equals(change.GroupName, change.GroupName.Trim(), StringComparison.Ordinal))
                {
                    errors.Add($"{label}: Der Gruppenname enthält führende oder nachgestellte Leerzeichen.");
                }
            }

            var isMembershipChange = change.ChangeType is
                VmGroupChangeType.AddMembership or VmGroupChangeType.RemoveMembership;
            if (isMembershipChange && change.VmId == Guid.Empty)
            {
                errors.Add($"{label}: Die VM-ID ist leer.");
            }
            else if (!isMembershipChange && change.VmId != Guid.Empty)
            {
                warnings.Add($"{label}: Die VM-ID wird bei einer Gruppenoperation ignoriert.");
            }

            if (string.IsNullOrWhiteSpace(change.Description))
            {
                warnings.Add($"{label}: Es fehlt eine lesbare Beschreibung.");
            }

            if (!seenOperations.Add((change.ChangeType, change.VmId, change.GroupId)))
            {
                errors.Add($"{label}: Die gleiche Operation ist mehrfach im Änderungssatz enthalten.");
            }
        }

        return new ChangeSetValidationResult { Errors = errors, Warnings = warnings };
    }

    public static ChangeSetValidationResult Validate(
        IEnumerable<VirtualMachineInfo> virtualMachines,
        IEnumerable<VmGroupInfo> groups,
        IReadOnlyList<VmGroupMembershipChange> changes)
    {
        ArgumentNullException.ThrowIfNull(virtualMachines);
        ArgumentNullException.ThrowIfNull(groups);

        var structuralResult = ValidateStructure(changes);
        var errors = structuralResult.Errors.ToList();
        var warnings = structuralResult.Warnings.ToList();
        if (!structuralResult.IsValid)
        {
            return new ChangeSetValidationResult { Errors = errors, Warnings = warnings };
        }

        var vmList = virtualMachines.ToList();
        var groupList = groups.ToList();
        var duplicateVmIds = vmList.GroupBy(vm => vm.Id).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        var duplicateGroupIds = groupList.GroupBy(group => group.Id).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();

        if (duplicateVmIds.Length > 0)
        {
            errors.Add("Der geladene Zustand enthält doppelte VM-IDs und ist nicht sicher verarbeitbar.");
        }

        if (duplicateGroupIds.Length > 0)
        {
            errors.Add("Der geladene Zustand enthält doppelte Gruppen-IDs und ist nicht sicher verarbeitbar.");
        }

        var duplicateGroupNames = groupList
            .GroupBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateGroupNames.Length > 0)
        {
            errors.Add($"Der geladene Zustand enthält nicht eindeutige Gruppennamen: {string.Join(", ", duplicateGroupNames)}.");
        }

        if (errors.Count > 0)
        {
            return new ChangeSetValidationResult { Errors = errors, Warnings = warnings };
        }

        var vmById = vmList.ToDictionary(vm => vm.Id);
        var groupById = groupList.ToDictionary(group => group.Id);
        var membersByGroup = groupById.ToDictionary(pair => pair.Key, pair => new HashSet<Guid>(pair.Value.MemberVmIds));

        foreach (var vm in vmList)
        {
            foreach (var groupName in vm.GroupNames)
            {
                var matchingGroup = groupList.FirstOrDefault(group =>
                    string.Equals(group.Name, groupName, StringComparison.OrdinalIgnoreCase));
                if (matchingGroup is not null)
                {
                    membersByGroup[matchingGroup.Id].Add(vm.Id);
                }
            }
        }

        foreach (var change in changes.OrderBy(change => VmGroupChangeQueue.GetExecutionPriority(change.ChangeType)))
        {
            switch (change.ChangeType)
            {
                case VmGroupChangeType.CreateGroup:
                    if (groupById.ContainsKey(change.GroupId))
                    {
                        errors.Add($"Gruppe '{change.GroupName}' kann nicht erstellt werden: Die ID wird bereits verwendet.");
                        break;
                    }

                    var createNameValidation = GroupNameValidator.Validate(change.GroupName, groupById.Values.Select(group => group.Name));
                    if (!createNameValidation.IsValid)
                    {
                        errors.Add($"Gruppe '{change.GroupName}' kann nicht erstellt werden: {createNameValidation.ErrorMessage}");
                        break;
                    }

                    groupById[change.GroupId] = NewGroup(change);
                    membersByGroup[change.GroupId] = new HashSet<Guid>();
                    break;

                case VmGroupChangeType.RenameGroup:
                    if (!groupById.TryGetValue(change.GroupId, out var renameGroup))
                    {
                        errors.Add($"Gruppe '{change.GroupName}' kann nicht umbenannt werden: Die Gruppe wurde nicht gefunden.");
                        break;
                    }

                    var renameValidation = GroupNameValidator.Validate(
                        change.GroupName,
                        groupById.Values.Where(group => group.Id != change.GroupId).Select(group => group.Name));
                    if (!renameValidation.IsValid)
                    {
                        errors.Add($"Gruppe '{renameGroup.Name}' kann nicht umbenannt werden: {renameValidation.ErrorMessage}");
                        break;
                    }

                    groupById[change.GroupId] = renameGroup with { Name = change.GroupName };
                    break;

                case VmGroupChangeType.AddMembership:
                    if (!TryResolveMembership(change, vmById, groupById, errors))
                    {
                        break;
                    }

                    if (!membersByGroup[change.GroupId].Add(change.VmId))
                    {
                        warnings.Add($"'{vmById[change.VmId].Name}' ist bereits Mitglied von '{groupById[change.GroupId].Name}'; Hinzufügen ist ein No-op.");
                    }
                    break;

                case VmGroupChangeType.RemoveMembership:
                    if (!TryResolveMembership(change, vmById, groupById, errors))
                    {
                        break;
                    }

                    if (!membersByGroup[change.GroupId].Remove(change.VmId))
                    {
                        warnings.Add($"'{vmById[change.VmId].Name}' ist kein Mitglied von '{groupById[change.GroupId].Name}'; Entfernen ist ein No-op.");
                    }
                    break;

                case VmGroupChangeType.DeleteGroup:
                    if (!groupById.TryGetValue(change.GroupId, out var deleteGroup))
                    {
                        errors.Add($"Gruppe '{change.GroupName}' kann nicht gelöscht werden: Die Gruppe wurde nicht gefunden.");
                        break;
                    }

                    var remainingMemberCount = membersByGroup[change.GroupId].Count;
                    if (remainingMemberCount > 0)
                    {
                        errors.Add($"Gruppe '{deleteGroup.Name}' kann nicht gelöscht werden: Nach den geplanten Änderungen verbleiben {remainingMemberCount} Mitglied(er).");
                        break;
                    }

                    groupById.Remove(change.GroupId);
                    membersByGroup.Remove(change.GroupId);
                    break;
            }
        }

        return new ChangeSetValidationResult { Errors = errors, Warnings = warnings };
    }

    private static bool TryResolveMembership(
        VmGroupMembershipChange change,
        IReadOnlyDictionary<Guid, VirtualMachineInfo> vmById,
        IReadOnlyDictionary<Guid, VmGroupInfo> groupById,
        ICollection<string> errors)
    {
        var valid = true;
        if (!vmById.ContainsKey(change.VmId))
        {
            errors.Add($"VM '{change.VmName ?? change.VmId.ToString()}' wurde im geladenen Zustand nicht gefunden.");
            valid = false;
        }

        if (!groupById.ContainsKey(change.GroupId))
        {
            errors.Add($"Gruppe '{change.GroupName}' wurde im geladenen Zustand nicht gefunden.");
            valid = false;
        }

        return valid;
    }

    private static VmGroupInfo NewGroup(VmGroupMembershipChange change) => new()
    {
        Id = change.GroupId,
        Name = change.GroupName,
        GroupType = "VMCollectionType",
    };

}

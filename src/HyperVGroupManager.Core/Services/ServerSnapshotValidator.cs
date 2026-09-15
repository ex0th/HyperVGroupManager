using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Results;

namespace HyperVGroupManager.Core.Services;

/// <summary>
/// Prüft vom Backend gelesene Daten, bevor sie Grundlage für schreibende Änderungen werden.
/// </summary>
public static class ServerSnapshotValidator
{
    public static ChangeSetValidationResult Validate(
        IReadOnlyList<VirtualMachineInfo> virtualMachines,
        IReadOnlyList<VmGroupInfo> groups)
    {
        ArgumentNullException.ThrowIfNull(virtualMachines);
        ArgumentNullException.ThrowIfNull(groups);
        var errors = new List<string>();
        var warnings = new List<string>();

        if (virtualMachines.Any(vm => vm is null) || groups.Any(group => group is null))
        {
            errors.Add("Der Serverzustand enthält leere Datensätze.");
            return new ChangeSetValidationResult { Errors = errors, Warnings = warnings };
        }

        if (virtualMachines.Any(vm => vm.Id == Guid.Empty))
        {
            errors.Add("Mindestens eine VM besitzt keine gültige ID.");
        }

        if (groups.Any(group => group.Id == Guid.Empty))
        {
            errors.Add("Mindestens eine VM-Gruppe besitzt keine gültige ID.");
        }

        if (virtualMachines.GroupBy(vm => vm.Id).Any(items => items.Count() > 1))
        {
            errors.Add("Der Server lieferte doppelte VM-IDs.");
        }

        if (groups.GroupBy(group => group.Id).Any(items => items.Count() > 1))
        {
            errors.Add("Der Server lieferte doppelte Gruppen-IDs.");
        }

        var duplicateGroupNames = groups
            .GroupBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Where(items => items.Count() > 1)
            .Select(items => items.Key)
            .ToArray();
        if (duplicateGroupNames.Length > 0)
        {
            errors.Add($"Der Server lieferte nicht eindeutige Gruppennamen: {string.Join(", ", duplicateGroupNames)}.");
        }

        foreach (var group in groups)
        {
            if (group.MemberVmIds is null || group.MemberVmNames is null)
            {
                errors.Add($"Gruppe '{group.Name}' enthält keine gültigen Mitgliederlisten.");
                continue;
            }

            var nameResult = GroupNameValidator.Validate(group.Name, Array.Empty<string>());
            if (!nameResult.IsValid)
            {
                errors.Add($"Gruppe mit ID '{group.Id}' hat einen ungültigen Namen: {nameResult.ErrorMessage}");
            }

            if (!string.Equals(group.GroupType, "VMCollectionType", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Gruppe '{group.Name}' hat den nicht unterstützten Typ '{group.GroupType}'.");
            }

            if (group.MemberCount < 0)
            {
                errors.Add($"Gruppe '{group.Name}' hat eine ungültige Mitgliederzahl.");
            }

            var distinctMemberCount = group.MemberVmIds.Distinct().Count();
            if (group.MemberVmIds.Count > 0 && group.MemberCount != distinctMemberCount)
            {
                warnings.Add($"Mitgliederzahl und Mitgliederliste von '{group.Name}' sind nicht konsistent.");
            }
        }

        var knownGroupNames = groups.Select(group => group.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (virtualMachines.Any(vm => vm.GroupNames is null))
        {
            errors.Add("Mindestens eine VM enthält keine gültige Gruppenliste.");
            return new ChangeSetValidationResult { Errors = errors, Warnings = warnings };
        }

        var unknownGroupNames = virtualMachines
            .SelectMany(vm => vm.GroupNames)
            .Where(name => !knownGroupNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknownGroupNames.Length > 0)
        {
            warnings.Add($"VMs verweisen auf nicht geladene Gruppen: {string.Join(", ", unknownGroupNames)}.");
        }

        var knownVmIds = virtualMachines.Select(vm => vm.Id).ToHashSet();
        var unknownMemberCount = groups.SelectMany(group => group.MemberVmIds).Distinct().Count(id => !knownVmIds.Contains(id));
        if (unknownMemberCount > 0)
        {
            warnings.Add($"Gruppen enthalten {unknownMemberCount} nicht geladene VM-Mitglied(er).");
        }

        return new ChangeSetValidationResult { Errors = errors, Warnings = warnings };
    }
}

using HyperVGroupManager.Core.Models;

namespace HyperVGroupManager.Core.Services;

/// <summary>
/// Fachliche Regeln rund um Gruppen, die unabhängig vom konkreten Backend gelten
/// (z. B. PowerShell), z. B. ob eine Gruppe gelöscht werden darf.
/// </summary>
public static class VmGroupRules
{
    public static bool CanDeleteGroup(VmGroupInfo group) => group.MemberCount == 0;

    public static string BuildNonEmptyGroupDeletionMessage(VmGroupInfo group) =>
        $"Die Gruppe \"{group.Name}\" enthält noch {group.MemberCount} Mitglied(er) und kann nicht gelöscht werden. " +
        "Entfernen Sie zuerst alle Mitgliedschaften.";
}

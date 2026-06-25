using System.Text.Json;
using HyperVGroupManager.Core.Models;

namespace HyperVGroupManager.Core.Services;

/// <summary>
/// Baut die JSON-Exportdarstellung der aktuellen Gruppenkonfiguration (camelCase, siehe
/// docs/architecture.md), unabhängig vom konkreten Speicherort (SaveFileDialog liegt in der View).
/// </summary>
public static class ConfigurationExportBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Build(string targetName, IReadOnlyList<VmGroupInfo> groups)
    {
        var export = new ConfigurationExport
        {
            TargetName = targetName,
            ExportedAt = DateTime.UtcNow,
            Groups = groups.Select(ToExportGroup).ToList(),
        };

        return JsonSerializer.Serialize(export, SerializerOptions);
    }

    private static ConfigurationExportGroup ToExportGroup(VmGroupInfo group) => new()
    {
        Id = group.Id,
        Name = group.Name,
        GroupType = group.GroupType,
        Members = group.MemberVmIds
            .Zip(group.MemberVmNames, (id, name) => new ConfigurationExportMember { Id = id, Name = name })
            .ToList(),
    };
}

using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Services;

namespace HyperVGroupManager.Tests.Core;

public class ConfigurationExportBuilderTests
{
    [Fact]
    public void Build_ProducesCamelCaseJsonWithGroupsAndMembers()
    {
        var vmId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var groups = new[]
        {
            new VmGroupInfo
            {
                Id = groupId,
                Name = "VEEAM_Backup_Daily",
                GroupType = "VMCollectionType",
                MemberCount = 1,
                MemberVmIds = new[] { vmId },
                MemberVmNames = new[] { "DC01" },
            },
        };

        var json = ConfigurationExportBuilder.Build("HVCL01", groups);

        Assert.Contains("\"targetName\": \"HVCL01\"", json);
        Assert.Contains("\"exportedAt\"", json);
        Assert.Contains("\"groupType\": \"VMCollectionType\"", json);
        Assert.Contains("\"name\": \"DC01\"", json);
        Assert.Contains(groupId.ToString(), json);
        Assert.Contains(vmId.ToString(), json);
    }

    [Fact]
    public void Build_NoGroups_ProducesEmptyGroupsArray()
    {
        var json = ConfigurationExportBuilder.Build("HV01", Array.Empty<VmGroupInfo>());

        Assert.Contains("\"groups\": []", json);
    }
}

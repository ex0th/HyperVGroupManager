using HyperVGroupManager.App.Services;
using HyperVGroupManager.Core.Exceptions;
using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Results;
using HyperVGroupManager.Tests.Fakes;

namespace HyperVGroupManager.Tests.App;

public class PowerShellExecutorParsingTests
{
    [Fact]
    public void ParseEnvelope_RawResultFailed_PropagatesErrorsWithoutParsing()
    {
        var rawResult = new PowerShellResult<string>
        {
            Success = false,
            Errors = new[] { "Boom" },
            ExitCode = 1,
        };

        var result = PowerShellExecutor.ParseEnvelope<VmGroupInfo>(rawResult, "Get-HVGMGroup", new FakeLogService());

        Assert.False(result.Success);
        Assert.Equal(new[] { "Boom" }, result.Errors);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void ParseEnvelope_ValidJson_MapsToCoreModel()
    {
        var groupId = Guid.NewGuid();
        var json = $$"""
            {"Success":true,"Data":{"Id":"{{groupId}}","Name":"VEEAM_Backup_Daily","GroupType":"VMCollectionType","MemberCount":1,"MemberVmIds":[],"MemberVmNames":[]},"Errors":[],"Warnings":[]}
            """;

        var rawResult = new PowerShellResult<string> { Success = true, Data = json, RawOutput = json, ExitCode = 0 };

        var result = PowerShellExecutor.ParseEnvelope<VmGroupInfo>(rawResult, "Get-HVGMGroup", new FakeLogService());

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(groupId, result.Data!.Id);
        Assert.Equal("VEEAM_Backup_Daily", result.Data.Name);
        Assert.Equal("VMCollectionType", result.Data.GroupType);
    }

    [Fact]
    public void ParseEnvelope_InvalidJson_ThrowsPowerShellExecutionException()
    {
        var rawResult = new PowerShellResult<string> { Success = true, Data = "kein-json", RawOutput = "kein-json", ExitCode = 0 };

        Assert.Throws<PowerShellExecutionException>(() =>
            PowerShellExecutor.ParseEnvelope<VmGroupInfo>(rawResult, "Get-HVGMGroup", new FakeLogService()));
    }
}

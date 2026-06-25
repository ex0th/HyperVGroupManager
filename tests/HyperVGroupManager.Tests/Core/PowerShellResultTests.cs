using HyperVGroupManager.Core.Results;

namespace HyperVGroupManager.Tests.Core;

public class PowerShellResultTests
{
    [Fact]
    public void SuccessResult_DefaultsErrorsAndWarnings_ToEmptyLists()
    {
        var result = new PowerShellResult<string>
        {
            Success = true,
            Data = "ok",
        };

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal(string.Empty, result.RawOutput);
    }

    [Fact]
    public void FailedResult_CarriesErrorsAndExitCode()
    {
        var result = new PowerShellResult<string>
        {
            Success = false,
            Errors = new[] { "Modul Hyper-V nicht gefunden." },
            ExitCode = 1,
        };

        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Equal(1, result.ExitCode);
    }
}

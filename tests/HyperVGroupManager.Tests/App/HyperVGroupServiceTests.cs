using HyperVGroupManager.App.Services;
using HyperVGroupManager.Core.Exceptions;
using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Results;
using HyperVGroupManager.Tests.Fakes;

namespace HyperVGroupManager.Tests.App;

public class HyperVGroupServiceTests
{
    [Fact]
    public async Task TestEnvironmentAsync_Success_ReturnsEnvironmentInfo()
    {
        var executor = new FakePowerShellExecutor();
        executor.SetResponse("Test-HVGMEnvironment", new PowerShellResult<EnvironmentInfo>
        {
            Success = true,
            Data = new EnvironmentInfo { TargetName = "HV01", TargetType = "Host", PowerShellVersion = "5.1.0.0" },
        });

        var service = new HyperVGroupService(executor, new FakeLogService());

        var result = await service.TestEnvironmentAsync("HV01", CancellationToken.None);

        Assert.Equal("HV01", result.TargetName);
    }

    [Fact]
    public async Task TestEnvironmentAsync_Failure_ThrowsHyperVModuleMissingException()
    {
        var executor = new FakePowerShellExecutor();
        executor.SetResponse("Test-HVGMEnvironment", new PowerShellResult<EnvironmentInfo>
        {
            Success = false,
            Errors = new[] { "Hyper-V-Modul fehlt." },
        });

        var service = new HyperVGroupService(executor, new FakeLogService());

        await Assert.ThrowsAsync<HyperVModuleMissingException>(() =>
            service.TestEnvironmentAsync("HV01", CancellationToken.None));
    }

    [Fact]
    public async Task GetVirtualMachinesAsync_Failure_ThrowsHyperVConnectionException()
    {
        var executor = new FakePowerShellExecutor();
        executor.SetResponse("Get-HVGMVirtualMachine", new PowerShellResult<IReadOnlyList<VirtualMachineInfo>>
        {
            Success = false,
            Errors = new[] { "Nicht erreichbar." },
        });

        var service = new HyperVGroupService(executor, new FakeLogService());

        await Assert.ThrowsAsync<HyperVConnectionException>(() =>
            service.GetVirtualMachinesAsync("HV01", CancellationToken.None));
    }

    [Fact]
    public async Task CreateGroupAsync_Failure_ThrowsVmGroupOperationException()
    {
        var executor = new FakePowerShellExecutor();
        executor.SetResponse("New-HVGMGroup", new PowerShellResult<object> { Success = false, Errors = new[] { "Gruppe existiert bereits." } });

        var service = new HyperVGroupService(executor, new FakeLogService());

        await Assert.ThrowsAsync<VmGroupOperationException>(() =>
            service.CreateGroupAsync("HV01", "VEEAM_Test", CancellationToken.None));
    }

    [Fact]
    public async Task CreateGroupAsync_Success_CallsExecutorWithExpectedCommand()
    {
        var executor = new FakePowerShellExecutor();
        executor.SetResponse("New-HVGMGroup", new PowerShellResult<object> { Success = true });

        var service = new HyperVGroupService(executor, new FakeLogService());

        await service.CreateGroupAsync("HV01", "VEEAM_Test", CancellationToken.None);

        Assert.Single(executor.Calls);
        Assert.Equal("New-HVGMGroup", executor.Calls[0].CommandName);
    }

    [Fact]
    public async Task ApplyChangesAsync_TotalFailure_ThrowsVmGroupOperationException()
    {
        // Kompletter Ausfall (z. B. Prozess-/JSON-Fehler): Data ist null, keine Pro-Änderung-Ergebnisse.
        var executor = new FakePowerShellExecutor();
        executor.SetResponse("Invoke-HVGMChangeSet", new PowerShellResult<IReadOnlyList<ChangeApplicationResult>>
        {
            Success = false,
            Errors = new[] { "PowerShell-Prozess fehlgeschlagen." },
        });

        var service = new HyperVGroupService(executor, new FakeLogService());

        var changes = new[]
        {
            new VmGroupMembershipChange
            {
                ChangeType = VmGroupChangeType.AddMembership,
                VmId = Guid.NewGuid(),
                GroupId = Guid.NewGuid(),
                GroupName = "VEEAM_Test",
                Description = "VM zu VEEAM_Test hinzufügen",
            },
        };

        await Assert.ThrowsAsync<VmGroupOperationException>(() =>
            service.ApplyChangesAsync("HV01", changes, CancellationToken.None));
    }

    [Fact]
    public async Task ApplyChangesAsync_PartialFailure_ReturnsPerChangeResultsWithoutThrowing()
    {
        // Eine Änderung schlägt fehl, eine vorherige wurde bereits erfolgreich angewendet.
        var vmId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var executor = new FakePowerShellExecutor();
        executor.SetResponse("Invoke-HVGMChangeSet", new PowerShellResult<IReadOnlyList<ChangeApplicationResult>>
        {
            Success = false,
            Data = new[]
            {
                new ChangeApplicationResult
                {
                    ChangeType = VmGroupChangeType.CreateGroup,
                    GroupId = groupId,
                    Description = "Gruppe 'VEEAM_Test' erstellen",
                    Success = true,
                },
                new ChangeApplicationResult
                {
                    ChangeType = VmGroupChangeType.AddMembership,
                    VmId = vmId,
                    GroupId = groupId,
                    Description = "VM zu VEEAM_Test hinzufügen",
                    Success = false,
                    Error = "VM mit ID '...' wurde nicht gefunden.",
                },
            },
            Errors = new[] { "VM mit ID '...' wurde nicht gefunden." },
        });

        var service = new HyperVGroupService(executor, new FakeLogService());

        var changes = new[]
        {
            new VmGroupMembershipChange { ChangeType = VmGroupChangeType.CreateGroup, GroupId = groupId, GroupName = "VEEAM_Test", Description = "Gruppe 'VEEAM_Test' erstellen" },
            new VmGroupMembershipChange { ChangeType = VmGroupChangeType.AddMembership, VmId = vmId, GroupId = groupId, GroupName = "VEEAM_Test", Description = "VM zu VEEAM_Test hinzufügen" },
        };

        var result = await service.ApplyChangesAsync("HV01", changes, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(2, result.Results.Count);
        Assert.True(result.Results[0].Success);
        Assert.False(result.Results[1].Success);
        Assert.Equal("VM mit ID '...' wurde nicht gefunden.", result.Results[1].Error);
    }
}

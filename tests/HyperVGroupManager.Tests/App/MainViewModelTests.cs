using HyperVGroupManager.App.Services;
using HyperVGroupManager.App.ViewModels;
using HyperVGroupManager.Core.Exceptions;
using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Results;
using HyperVGroupManager.Tests.Fakes;

namespace HyperVGroupManager.Tests.App;

public class MainViewModelTests
{
    private static MainViewModel CreateViewModel(FakeHyperVGroupService service) =>
        new(service, new FakeLogService(), new ApplicationOptions());

    [Fact]
    public async Task ConnectCommand_Success_SetsConnectedStateAndLoadsData()
    {
        var service = new FakeHyperVGroupService
        {
            VirtualMachines = new List<VirtualMachineInfo>
            {
                new() { Id = Guid.NewGuid(), Name = "VM1", ComputerName = "HOST01", OwnerNode = "HOST01", State = "Running" },
            },
            Groups = new List<VmGroupInfo>
            {
                new() { Id = Guid.NewGuid(), Name = "VEEAM_Backup_Daily", GroupType = "VMCollectionType" },
            },
        };

        var viewModel = CreateViewModel(service);
        viewModel.TargetName = "HV01";

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsConnected);
        Assert.Equal("Verbunden", viewModel.ConnectionStatus);
        Assert.Single(viewModel.VirtualMachines);
        Assert.Single(viewModel.Groups);
    }

    [Fact]
    public async Task ConnectCommand_Failure_SetsErrorState()
    {
        var service = new FakeHyperVGroupService
        {
            ExceptionOnTestEnvironment = new HyperVConnectionException("Nicht erreichbar."),
        };

        var viewModel = CreateViewModel(service);
        viewModel.TargetName = "HV01";

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsConnected);
        Assert.Equal("Fehler", viewModel.ConnectionStatus);
    }

    [Fact]
    public void CreateGroupCommand_ValidName_AddsGroupAndPendingChange()
    {
        var viewModel = CreateViewModel(new FakeHyperVGroupService());

        viewModel.CreateGroupCommand.Execute("VEEAM_Backup_Daily");

        Assert.Single(viewModel.Groups);
        Assert.Single(viewModel.PendingChanges);
        Assert.Equal(VmGroupChangeType.CreateGroup, viewModel.PendingChanges[0].ChangeType);
    }

    [Fact]
    public void CreateGroupCommand_DuplicateName_DoesNotAddGroup()
    {
        var viewModel = CreateViewModel(new FakeHyperVGroupService());

        viewModel.CreateGroupCommand.Execute("VEEAM_Backup_Daily");
        viewModel.CreateGroupCommand.Execute("VEEAM_Backup_Daily");

        Assert.Single(viewModel.Groups);
        Assert.Single(viewModel.PendingChanges);
    }

    [Fact]
    public void DeleteGroupCommand_NonEmptyGroup_DoesNotQueueChange()
    {
        var service = new FakeHyperVGroupService();
        var viewModel = CreateViewModel(service);

        viewModel.CreateGroupCommand.Execute("VEEAM_Backup_Daily");
        var pendingGroup = viewModel.Groups[0];

        // Simuliert eine bereits vorhandene, nicht leere Gruppe (nicht über CreateGroup geplant).
        viewModel.SelectedGroup = pendingGroup with { MemberCount = 2 };

        viewModel.DeleteGroupCommand.Execute(null);

        Assert.DoesNotContain(viewModel.PendingChanges, c => c.ChangeType == VmGroupChangeType.DeleteGroup);
    }

    [Fact]
    public void AddThenRemoveSelectedVmsFromSameGroup_CancelOutInPendingChanges()
    {
        var viewModel = CreateViewModel(new FakeHyperVGroupService());
        var vm = new VirtualMachineInfo { Id = Guid.NewGuid(), Name = "VM1", ComputerName = "HOST01", OwnerNode = "HOST01", State = "Running" };
        var group = new VmGroupInfo { Id = Guid.NewGuid(), Name = "VEEAM_Backup_Daily", GroupType = "VMCollectionType" };

        viewModel.Groups.Add(group);
        viewModel.SelectedGroup = group;
        viewModel.SelectedVirtualMachines.Add(vm);

        viewModel.AddSelectedVmsToGroupCommand.Execute(null);
        Assert.Single(viewModel.PendingChanges);

        viewModel.RemoveSelectedVmsFromGroupCommand.Execute(null);
        Assert.Empty(viewModel.PendingChanges);
    }

    [Fact]
    public async Task ApplyChangesCommand_Success_ClearsPendingChangesAndCallsService()
    {
        var service = new FakeHyperVGroupService();
        var viewModel = CreateViewModel(service);
        viewModel.TargetName = "HV01";

        viewModel.CreateGroupCommand.Execute("VEEAM_Backup_Daily");

        await viewModel.ApplyChangesCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.PendingChanges);
        Assert.NotNull(service.LastAppliedChanges);
        Assert.Single(service.LastAppliedChanges!);
    }

    [Fact]
    public async Task ApplyChangesCommand_PartialFailure_KeepsOnlyFailedChangeInQueue()
    {
        var service = new FakeHyperVGroupService();
        var viewModel = CreateViewModel(service);
        viewModel.TargetName = "HV01";

        viewModel.CreateGroupCommand.Execute("VEEAM_A");
        viewModel.CreateGroupCommand.Execute("VEEAM_B");

        var succeedingGroupId = viewModel.PendingChanges[0].GroupId;
        var failingGroupId = viewModel.PendingChanges[1].GroupId;

        service.ApplyChangesResultToReturn = new ApplyChangesResult
        {
            Success = false,
            Results = new[]
            {
                new ChangeApplicationResult
                {
                    ChangeType = VmGroupChangeType.CreateGroup,
                    GroupId = succeedingGroupId,
                    Description = "Gruppe 'VEEAM_A' erstellen",
                    Success = true,
                },
                new ChangeApplicationResult
                {
                    ChangeType = VmGroupChangeType.CreateGroup,
                    GroupId = failingGroupId,
                    Description = "Gruppe 'VEEAM_B' erstellen",
                    Success = false,
                    Error = "Eine Gruppe mit dem Namen 'VEEAM_B' existiert bereits.",
                },
            },
        };

        await viewModel.ApplyChangesCommand.ExecuteAsync(null);

        Assert.Single(viewModel.PendingChanges);
        Assert.Equal(failingGroupId, viewModel.PendingChanges[0].GroupId);
    }

    [Fact]
    public void DiscardChangesCommand_RemovesPendingGroupPlaceholderAndChanges()
    {
        var viewModel = CreateViewModel(new FakeHyperVGroupService());

        viewModel.CreateGroupCommand.Execute("VEEAM_Backup_Daily");
        Assert.Single(viewModel.Groups);

        viewModel.DiscardChangesCommand.Execute(null);

        Assert.Empty(viewModel.Groups);
        Assert.Empty(viewModel.PendingChanges);
    }
}

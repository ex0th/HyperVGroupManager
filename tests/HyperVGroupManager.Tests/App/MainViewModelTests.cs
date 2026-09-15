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
    public async Task AddThenRemoveSelectedVmsFromSameGroup_CancelOutInPendingChanges()
    {
        var vm = new VirtualMachineInfo { Id = Guid.NewGuid(), Name = "VM1", ComputerName = "HOST01", OwnerNode = "HOST01", State = "Running" };
        var group = new VmGroupInfo { Id = Guid.NewGuid(), Name = "VEEAM_Backup_Daily", GroupType = "VMCollectionType" };
        var service = new FakeHyperVGroupService
        {
            VirtualMachines = new List<VirtualMachineInfo> { vm },
            Groups = new List<VmGroupInfo> { group },
        };
        var viewModel = CreateViewModel(service);
        viewModel.TargetName = "HV01";
        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.SelectedGroup = viewModel.Groups[0];
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

        await viewModel.ConnectCommand.ExecuteAsync(null);

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

        await viewModel.ConnectCommand.ExecuteAsync(null);

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

    [Fact]
    public async Task RefreshCommand_WithPendingChanges_PreservesEffectiveState()
    {
        var service = new FakeHyperVGroupService();
        var viewModel = CreateViewModel(service);
        viewModel.TargetName = "HV01";
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.CreateGroupCommand.Execute("VEEAM_New");

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Single(viewModel.PendingChanges);
        Assert.Equal("VEEAM_New", Assert.Single(viewModel.Groups).Name);
    }

    [Fact]
    public async Task AddMembership_ToPendingNewGroup_IsPlannedInSameChangeSet()
    {
        var vm = new VirtualMachineInfo
        {
            Id = Guid.NewGuid(), Name = "VM01", ComputerName = "HV01", OwnerNode = "HV01", State = "Running",
        };
        var service = new FakeHyperVGroupService { VirtualMachines = new List<VirtualMachineInfo> { vm } };
        var viewModel = CreateViewModel(service);
        viewModel.TargetName = "HV01";
        await viewModel.ConnectCommand.ExecuteAsync(null);

        viewModel.CreateGroupCommand.Execute("VEEAM_New");
        viewModel.SelectedVirtualMachines.Add(vm);
        viewModel.AddSelectedVmsToGroupCommand.Execute(null);

        Assert.Equal(2, viewModel.PendingChanges.Count);
        Assert.Equal(1, Assert.Single(viewModel.Groups).MemberCount);
        Assert.Equal("VEEAM_New", Assert.Single(viewModel.VirtualMachines).GroupNames.Single());
    }

    [Fact]
    public async Task DeletePendingNewGroup_WithPlannedMember_CancelsAllDependentChanges()
    {
        var vm = new VirtualMachineInfo
        {
            Id = Guid.NewGuid(), Name = "VM01", ComputerName = "HV01", OwnerNode = "HV01", State = "Running",
        };
        var service = new FakeHyperVGroupService { VirtualMachines = new List<VirtualMachineInfo> { vm } };
        var viewModel = CreateViewModel(service);
        viewModel.TargetName = "HV01";
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.CreateGroupCommand.Execute("VEEAM_Temporary");
        viewModel.SelectedVirtualMachines.Add(vm);
        viewModel.AddSelectedVmsToGroupCommand.Execute(null);

        viewModel.DeleteGroupCommand.Execute(null);

        Assert.Empty(viewModel.PendingChanges);
        Assert.Empty(viewModel.Groups);
        Assert.Empty(Assert.Single(viewModel.VirtualMachines).GroupNames);
    }

    [Fact]
    public async Task ConnectCommand_DifferentTargetWithPendingChanges_IsBlocked()
    {
        var service = new FakeHyperVGroupService();
        var viewModel = CreateViewModel(service);
        viewModel.TargetName = "HV01";
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.CreateGroupCommand.Execute("VEEAM_New");

        viewModel.TargetName = "HV02";
        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(1, service.TestEnvironmentCallCount);
        Assert.Equal("HV01", viewModel.ConnectedTargetName);
        Assert.Single(viewModel.PendingChanges);
    }

    [Fact]
    public async Task ApplyChanges_UsesConnectedTargetEvenIfInputWasEdited()
    {
        var service = new FakeHyperVGroupService();
        var viewModel = CreateViewModel(service);
        viewModel.TargetName = "HV01";
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.CreateGroupCommand.Execute("VEEAM_New");
        viewModel.TargetName = "HV02";

        await viewModel.ApplyChangesCommand.ExecuteAsync(null);

        Assert.Equal("HV01", service.LastAppliedTargetName);
    }

    [Fact]
    public async Task ApplyChanges_TransportFailure_MarksStateUncertainAndBlocksRetry()
    {
        var service = new FakeHyperVGroupService
        {
            ExceptionOnApplyChanges = new PowerShellExecutionException("Verbindung abgebrochen."),
        };
        var viewModel = CreateViewModel(service);
        viewModel.TargetName = "HV01";
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.CreateGroupCommand.Execute("VEEAM_New");

        await viewModel.ApplyChangesCommand.ExecuteAsync(null);
        await viewModel.ApplyChangesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsStateUncertain);
        Assert.Equal(1, service.ApplyChangesCallCount);
        Assert.Single(viewModel.PendingChanges);
    }

    [Fact]
    public async Task RemoveLastMemberThenDelete_IsAcceptedUsingEffectiveMemberCount()
    {
        var vm = new VirtualMachineInfo
        {
            Id = Guid.NewGuid(), Name = "VM01", ComputerName = "HV01", OwnerNode = "HV01", State = "Off",
            GroupNames = new[] { "VEEAM_Old" },
        };
        var group = new VmGroupInfo
        {
            Id = Guid.NewGuid(), Name = "VEEAM_Old", GroupType = "VMCollectionType", MemberCount = 1,
            MemberVmIds = new[] { vm.Id }, MemberVmNames = new[] { vm.Name },
        };
        var service = new FakeHyperVGroupService
        {
            VirtualMachines = new List<VirtualMachineInfo> { vm }, Groups = new List<VmGroupInfo> { group },
        };
        var viewModel = CreateViewModel(service);
        viewModel.TargetName = "HV01";
        await viewModel.ConnectCommand.ExecuteAsync(null);
        viewModel.SelectedGroup = viewModel.Groups[0];
        viewModel.SelectedVirtualMachines.Add(viewModel.VirtualMachines[0]);

        viewModel.RemoveSelectedVmsFromGroupCommand.Execute(null);
        Assert.Equal(0, viewModel.SelectedGroup!.MemberCount);
        viewModel.DeleteGroupCommand.Execute(null);

        Assert.Empty(viewModel.Groups);
        Assert.Contains(viewModel.PendingChanges, change => change.ChangeType == VmGroupChangeType.RemoveMembership);
        Assert.Contains(viewModel.PendingChanges, change => change.ChangeType == VmGroupChangeType.DeleteGroup);
    }
}

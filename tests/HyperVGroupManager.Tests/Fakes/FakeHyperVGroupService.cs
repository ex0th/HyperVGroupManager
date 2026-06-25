using HyperVGroupManager.Core.Interfaces;
using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Results;

namespace HyperVGroupManager.Tests.Fakes;

public sealed class FakeHyperVGroupService : IHyperVGroupService
{
    public EnvironmentInfo? EnvironmentToReturn { get; set; }
    public Exception? ExceptionOnTestEnvironment { get; set; }
    public List<VirtualMachineInfo> VirtualMachines { get; set; } = new();
    public List<VmGroupInfo> Groups { get; set; } = new();
    public Exception? ExceptionOnApplyChanges { get; set; }

    // Wenn gesetzt, wird dieses Ergebnis statt der Standard-Erfolgsantwort zurückgegeben
    // (z. B. um teilweise fehlgeschlagene Änderungsläufe zu simulieren).
    public ApplyChangesResult? ApplyChangesResultToReturn { get; set; }

    public IReadOnlyList<VmGroupMembershipChange>? LastAppliedChanges { get; private set; }

    public Task<EnvironmentInfo> TestEnvironmentAsync(string targetName, CancellationToken cancellationToken)
    {
        if (ExceptionOnTestEnvironment is not null)
        {
            throw ExceptionOnTestEnvironment;
        }

        return Task.FromResult(EnvironmentToReturn ?? new EnvironmentInfo
        {
            TargetName = targetName,
            TargetType = "Host",
            PowerShellVersion = "5.1.0.0",
        });
    }

    public Task<IReadOnlyList<VirtualMachineInfo>> GetVirtualMachinesAsync(string targetName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VirtualMachineInfo>>(VirtualMachines);

    public Task<IReadOnlyList<VmGroupInfo>> GetGroupsAsync(string targetName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VmGroupInfo>>(Groups);

    public Task CreateGroupAsync(string targetName, string groupName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RenameGroupAsync(string targetName, Guid groupId, string newName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DeleteGroupAsync(string targetName, Guid groupId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AddVmToGroupAsync(string targetName, Guid vmId, Guid groupId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RemoveVmFromGroupAsync(string targetName, Guid vmId, Guid groupId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ApplyChangesResult> ApplyChangesAsync(string targetName, IReadOnlyList<VmGroupMembershipChange> changes, CancellationToken cancellationToken)
    {
        LastAppliedChanges = changes;

        if (ExceptionOnApplyChanges is not null)
        {
            throw ExceptionOnApplyChanges;
        }

        if (ApplyChangesResultToReturn is not null)
        {
            return Task.FromResult(ApplyChangesResultToReturn);
        }

        var results = changes.Select(change => new ChangeApplicationResult
        {
            ChangeType = change.ChangeType,
            VmId = change.VmId,
            GroupId = change.GroupId,
            Description = change.Description,
            Success = true,
        }).ToList();

        return Task.FromResult(new ApplyChangesResult { Success = true, Results = results });
    }
}

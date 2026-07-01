using HyperVGroupManager.Core.Exceptions;
using HyperVGroupManager.Core.Interfaces;
using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Results;

namespace HyperVGroupManager.App.Services;

/// <summary>
/// Bildet die Fachlogik-Schnittstelle (IHyperVGroupService) auf das PowerShell-Backend ab.
/// ViewModels kennen nur IHyperVGroupService, nicht diese Klasse - das Backend könnte
/// später ausgetauscht werden, ohne die ViewModels anzufassen.
/// </summary>
public sealed class HyperVGroupService : IHyperVGroupService
{
    private readonly IPowerShellExecutor _executor;
    private readonly ILogService _logService;

    public HyperVGroupService(IPowerShellExecutor executor, ILogService logService)
    {
        _executor = executor;
        _logService = logService;
    }

    public async Task<EnvironmentInfo> TestEnvironmentAsync(string targetName, CancellationToken cancellationToken)
    {
        _logService.LogInformation($"Prüfe Umgebung für Ziel '{targetName}'.");

        var result = await _executor.ExecuteAsync<EnvironmentInfo>(
            "Test-HVGMEnvironment",
            new { TargetName = targetName },
            cancellationToken).ConfigureAwait(false);

        if (!result.Success || result.Data is null)
        {
            // Test-HVGMEnvironment liefert Success=false ausschließlich, wenn das
            // Hyper-V-Modul selbst fehlt - alle anderen Probleme landen als Warnings.
            throw new HyperVModuleMissingException(JoinErrors(result.Errors));
        }

        foreach (var warning in result.Warnings)
        {
            _logService.LogWarning(warning);
        }

        return result.Data;
    }

    public async Task<IReadOnlyList<VirtualMachineInfo>> GetVirtualMachinesAsync(string targetName, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<IReadOnlyList<VirtualMachineInfo>>(
            "Get-HVGMVirtualMachine",
            new { TargetName = targetName },
            cancellationToken).ConfigureAwait(false);

        if (!result.Success || result.Data is null)
        {
            throw new HyperVConnectionException($"VMs konnten auf '{targetName}' nicht gelesen werden: {JoinErrors(result.Errors)}");
        }

        return result.Data;
    }

    public async Task<IReadOnlyList<VmGroupInfo>> GetGroupsAsync(string targetName, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<IReadOnlyList<VmGroupInfo>>(
            "Get-HVGMGroup",
            new { TargetName = targetName },
            cancellationToken).ConfigureAwait(false);

        if (!result.Success || result.Data is null)
        {
            throw new HyperVConnectionException($"VM-Gruppen konnten auf '{targetName}' nicht gelesen werden: {JoinErrors(result.Errors)}");
        }

        return result.Data;
    }

    public async Task CreateGroupAsync(string targetName, string groupName, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<object>(
            "New-HVGMGroup",
            new { TargetName = targetName, GroupName = groupName },
            cancellationToken).ConfigureAwait(false);

        ThrowIfFailed(result, $"Gruppe '{groupName}' konnte nicht erstellt werden");
    }

    public async Task RenameGroupAsync(string targetName, Guid groupId, string newName, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<object>(
            "Rename-HVGMGroup",
            new { TargetName = targetName, GroupId = groupId, NewName = newName },
            cancellationToken).ConfigureAwait(false);

        ThrowIfFailed(result, $"Gruppe konnte nicht in '{newName}' umbenannt werden");
    }

    public async Task DeleteGroupAsync(string targetName, Guid groupId, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<object>(
            "Remove-HVGMGroup",
            new { TargetName = targetName, GroupId = groupId },
            cancellationToken).ConfigureAwait(false);

        ThrowIfFailed(result, "Gruppe konnte nicht gelöscht werden");
    }

    public async Task AddVmToGroupAsync(string targetName, Guid vmId, Guid groupId, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<object>(
            "Add-HVGMGroupMember",
            new { TargetName = targetName, VmId = vmId, GroupId = groupId },
            cancellationToken).ConfigureAwait(false);

        ThrowIfFailed(result, "VM konnte der Gruppe nicht hinzugefügt werden");
    }

    public async Task RemoveVmFromGroupAsync(string targetName, Guid vmId, Guid groupId, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<object>(
            "Remove-HVGMGroupMember",
            new { TargetName = targetName, VmId = vmId, GroupId = groupId },
            cancellationToken).ConfigureAwait(false);

        ThrowIfFailed(result, "VM konnte nicht aus der Gruppe entfernt werden");
    }

    public async Task<ApplyChangesResult> ApplyChangesAsync(string targetName, IReadOnlyList<VmGroupMembershipChange> changes, CancellationToken cancellationToken)
    {
        var changePayload = changes.Select(change => new
        {
            ChangeType = change.ChangeType.ToString(),
            change.VmId,
            change.VmName,
            change.GroupId,
            change.GroupName,
            change.Description,
        });

        var result = await _executor.ExecuteAsync<IReadOnlyList<ChangeApplicationResult>>(
            "Invoke-HVGMChangeSet",
            new { TargetName = targetName, Changes = changePayload },
            cancellationToken).ConfigureAwait(false);

        if (result.Data is null)
        {
            // Kompletter Ausfall (z. B. Prozess-/JSON-Fehler) - hier gibt es keine
            // Pro-Änderung-Ergebnisse, daher als harte Exception statt Teilergebnis.
            var message = $"Änderungen konnten nicht angewendet werden: {JoinErrors(result.Errors)}";
            _logService.LogError(message);
            throw new VmGroupOperationException(message);
        }

        foreach (var item in result.Data)
        {
            if (item.Success)
            {
                foreach (var warning in item.Warnings)
                {
                    _logService.LogWarning(warning);
                }
            }
            else
            {
                _logService.LogError($"{item.Description}: {item.Error}");
            }
        }

        return new ApplyChangesResult { Success = result.Success, Results = result.Data };
    }

    public async Task<ClusterConfigInfo> GetClusterConfigAsync(string targetName, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<ClusterConfigInfo>(
            "Get-HVGMClusterConfig",
            new { TargetName = targetName },
            cancellationToken).ConfigureAwait(false);

        if (!result.Success || result.Data is null)
            throw new HyperVConnectionException($"Cluster-Konfiguration konnte nicht gelesen werden: {JoinErrors(result.Errors)}");

        return result.Data;
    }

    public async Task SetConfigStoreRootPathAsync(string targetName, string path, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<object>(
            "Set-HVGMConfigStoreRootPath",
            new { TargetName = targetName, Path = path },
            cancellationToken).ConfigureAwait(false);

        ThrowIfFailed(result, "ConfigStoreRootPath konnte nicht gesetzt werden");
    }

    private void ThrowIfFailed<T>(Core.Results.PowerShellResult<T> result, string operationDescription)
    {
        if (result.Success)
        {
            foreach (var warning in result.Warnings)
            {
                _logService.LogWarning(warning);
            }

            return;
        }

        var message = $"{operationDescription}: {JoinErrors(result.Errors)}";
        _logService.LogError(message);
        throw new VmGroupOperationException(message);
    }

    private static string JoinErrors(IReadOnlyList<string> errors) =>
        errors.Count > 0 ? string.Join("; ", errors) : "Unbekannter Fehler.";
}

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperVGroupManager.App.Services;
using HyperVGroupManager.Core.Exceptions;
using HyperVGroupManager.Core.Interfaces;
using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Results;
using HyperVGroupManager.Core.Services;

namespace HyperVGroupManager.App.ViewModels;

/// <summary>
/// Zentrales ViewModel des MainWindow. Enthält keine PowerShell-Aufrufe (nur über
/// IHyperVGroupService) und keine WPF-Typen, damit es unabhängig testbar bleibt.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IHyperVGroupService _groupService;
    private readonly ILogService _logService;
    private readonly ApplicationOptions _applicationOptions;
    private readonly VmGroupChangeQueue _changeQueue = new();

    // Gruppen, die lokal als "CreateGroup" geplant, aber noch nicht angewendet wurden.
    // Solche Gruppen haben noch keine echte, vom Server vergebene Id - Mitgliedschaften
    // können daher erst nach dem Anwenden hinzugefügt werden.
    private readonly HashSet<Guid> _pendingNewGroupIds = new();

    private List<VirtualMachineInfo> _allVirtualMachines = new();
    private List<VmGroupInfo> _allGroups = new();

    [ObservableProperty]
    private string _targetName = string.Empty;

    [ObservableProperty]
    private string _connectionStatus = "Nicht verbunden";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _groupSearchText = string.Empty;

    [ObservableProperty]
    private VmFilterMode _selectedFilterMode = VmFilterMode.All;

    [ObservableProperty]
    private VmGroupInfo? _selectedGroup;

    [ObservableProperty]
    private VirtualMachineInfo? _selectedVm;

    [ObservableProperty]
    private DateTime? _lastRefreshedAt;

    public ObservableCollection<VmGroupInfo> Groups { get; } = new();

    // Vom Suchfeld der linken Spalte gefilterte Sicht auf Groups; Groups selbst bleibt
    // die maßgebliche Liste (u. a. für Platzhalter-Gruppenverwaltung).
    public ObservableCollection<VmGroupInfo> FilteredGroups { get; } = new();
    public ObservableCollection<VirtualMachineInfo> VirtualMachines { get; } = new();
    public ObservableCollection<VmGroupMembershipChange> PendingChanges { get; } = new();
    public ObservableCollection<string> StatusMessages { get; } = new();

    // Wird von der View über eine Mehrfachauswahl-Bindung (DataGrid SelectedItems) befüllt.
    public ObservableCollection<VirtualMachineInfo> SelectedVirtualMachines { get; } = new();

    public string DefaultGroupPrefix => _applicationOptions.DefaultGroupPrefix;

    public int VirtualMachineCount => _allVirtualMachines.Count;

    public int GroupCount => _allGroups.Count;

    public int PendingChangeCount => PendingChanges.Count;

    private bool CanInteract => !IsBusy;

    // Die View entscheidet, wie ein kritischer Fehler angezeigt wird (z. B. Fehlerdetails-Dialog);
    // das ViewModel kennt keine WPF-Typen und löst nur ein Ereignis aus.
    public event EventHandler<string>? ErrorOccurred;

    // Ergebnis eines erfolgreichen Änderungslaufs, damit die View einen Ergebnis-Dialog zeigen kann.
    public event EventHandler<string>? ChangesApplied;

    public MainViewModel(IHyperVGroupService groupService, ILogService logService, ApplicationOptions applicationOptions)
    {
        _groupService = groupService;
        _logService = logService;
        _applicationOptions = applicationOptions;
    }

    partial void OnIsBusyChanged(bool value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        ApplyChangesCommand.NotifyCanExecuteChanged();
        ExportConfigurationCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => ApplyVmFilter();

    partial void OnGroupSearchTextChanged(string value) => ApplyGroupFilter();

    partial void OnSelectedFilterModeChanged(VmFilterMode value) => ApplyVmFilter();

    partial void OnSelectedGroupChanged(VmGroupInfo? value)
    {
        if (SelectedFilterMode == VmFilterMode.SelectedGroup)
        {
            ApplyVmFilter();
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(TargetName))
        {
            AddStatusMessage("Bitte geben Sie einen Host- oder Clusternamen ein.");
            return;
        }

        IsBusy = true;
        ConnectionStatus = "Verbinde";

        try
        {
            var environment = await _groupService.TestEnvironmentAsync(TargetName, cancellationToken);

            foreach (var warning in environment.Warnings)
            {
                AddStatusMessage($"Warnung: {warning}");
            }

            IsConnected = true;
            ConnectionStatus = "Verbunden";
            _logService.LogInformation(
                $"Verbindung zu '{TargetName}' aufgebaut ({environment.TargetType}, Nodes: {string.Join(", ", environment.Nodes)}).");

            await LoadDataAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HyperVConnectionException or HyperVModuleMissingException or PowerShellExecutionException)
        {
            IsConnected = false;
            ConnectionStatus = "Fehler";
            AddStatusMessage($"Verbindung fehlgeschlagen: {ex.Message}");
            _logService.LogError($"Verbindung zu '{TargetName}' fehlgeschlagen.", ex);
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            AddStatusMessage("Bitte zuerst verbinden.");
            return;
        }

        IsBusy = true;

        try
        {
            await LoadDataAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HyperVConnectionException or PowerShellExecutionException)
        {
            AddStatusMessage($"Aktualisierung fehlgeschlagen: {ex.Message}");
            _logService.LogError("Aktualisierung fehlgeschlagen.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        var vms = await _groupService.GetVirtualMachinesAsync(TargetName, cancellationToken);
        var groups = await _groupService.GetGroupsAsync(TargetName, cancellationToken);

        _allVirtualMachines = vms.ToList();
        _allGroups = groups.ToList();
        _pendingNewGroupIds.Clear();

        Groups.Clear();
        foreach (var group in _allGroups)
        {
            Groups.Add(group);
        }

        ApplyGroupFilter();
        ApplyVmFilter();
        LastRefreshedAt = DateTime.Now;
        OnPropertyChanged(nameof(VirtualMachineCount));
        OnPropertyChanged(nameof(GroupCount));
        AddStatusMessage($"{_allVirtualMachines.Count} VMs und {_allGroups.Count} Gruppen geladen.");
    }

    private void ApplyVmFilter()
    {
        var filtered = VirtualMachineFilter.Apply(_allVirtualMachines, SelectedFilterMode, SearchText, SelectedGroup);

        VirtualMachines.Clear();
        foreach (var vm in filtered)
        {
            VirtualMachines.Add(vm);
        }
    }

    private void ApplyGroupFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(GroupSearchText)
            ? _allGroups
            : _allGroups.Where(g => g.Name.Contains(GroupSearchText.Trim(), StringComparison.OrdinalIgnoreCase));

        FilteredGroups.Clear();
        foreach (var group in filtered)
        {
            FilteredGroups.Add(group);
        }
    }

    [RelayCommand]
    private void CreateGroup(string? groupName)
    {
        var validation = GroupNameValidator.Validate(groupName, _allGroups.Select(g => g.Name));

        if (!validation.IsValid)
        {
            AddStatusMessage($"Gruppe konnte nicht angelegt werden: {validation.ErrorMessage}");
            return;
        }

        var trimmedName = groupName!.Trim();
        var pendingGroupId = Guid.NewGuid();

        _changeQueue.Add(new VmGroupMembershipChange
        {
            ChangeType = VmGroupChangeType.CreateGroup,
            GroupId = pendingGroupId,
            GroupName = trimmedName,
            Description = $"Gruppe '{trimmedName}' erstellen",
        });

        _pendingNewGroupIds.Add(pendingGroupId);

        var placeholderGroup = new VmGroupInfo
        {
            Id = pendingGroupId,
            Name = trimmedName,
            GroupType = "VMCollectionType",
            MemberCount = 0,
        };

        _allGroups.Add(placeholderGroup);
        Groups.Add(placeholderGroup);
        ApplyGroupFilter();

        RefreshPendingChanges();
        AddStatusMessage($"Gruppe '{trimmedName}' geplant (noch nicht angewendet).");
    }

    [RelayCommand]
    private void RenameGroup(string? newName)
    {
        if (SelectedGroup is null)
        {
            AddStatusMessage("Bitte zuerst eine Gruppe auswählen.");
            return;
        }

        var group = SelectedGroup;
        var validation = GroupNameValidator.Validate(newName, _allGroups.Where(g => g.Id != group.Id).Select(g => g.Name));

        if (!validation.IsValid)
        {
            AddStatusMessage($"Gruppe konnte nicht umbenannt werden: {validation.ErrorMessage}");
            return;
        }

        var trimmedName = newName!.Trim();

        if (_pendingNewGroupIds.Contains(group.Id))
        {
            var pendingCreateChange = _changeQueue.Changes
                .FirstOrDefault(c => c.ChangeType == VmGroupChangeType.CreateGroup && c.GroupId == group.Id);

            if (pendingCreateChange is not null)
            {
                _changeQueue.Remove(pendingCreateChange);
                _changeQueue.Add(pendingCreateChange with { GroupName = trimmedName, Description = $"Gruppe '{trimmedName}' erstellen" });
            }
        }
        else
        {
            _changeQueue.Add(new VmGroupMembershipChange
            {
                ChangeType = VmGroupChangeType.RenameGroup,
                GroupId = group.Id,
                GroupName = trimmedName,
                Description = $"Gruppe '{group.Name}' in '{trimmedName}' umbenennen",
            });
        }

        UpdateLocalGroupName(group.Id, trimmedName);
        RefreshPendingChanges();
        AddStatusMessage($"Umbenennung von '{group.Name}' in '{trimmedName}' geplant.");
    }

    [RelayCommand]
    private void DeleteGroup()
    {
        if (SelectedGroup is null)
        {
            AddStatusMessage("Bitte zuerst eine Gruppe auswählen.");
            return;
        }

        var group = SelectedGroup;

        if (!VmGroupRules.CanDeleteGroup(group))
        {
            AddStatusMessage(VmGroupRules.BuildNonEmptyGroupDeletionMessage(group));
            return;
        }

        _changeQueue.Add(new VmGroupMembershipChange
        {
            ChangeType = VmGroupChangeType.DeleteGroup,
            GroupId = group.Id,
            GroupName = group.Name,
            Description = $"Gruppe '{group.Name}' löschen",
        });

        if (_pendingNewGroupIds.Remove(group.Id))
        {
            // CreateGroup + DeleteGroup derselben noch nicht angewendeten Gruppe heben sich
            // in der Queue automatisch auf - lokale Platzhalter-Gruppe ebenfalls entfernen.
            RemoveLocalGroup(group.Id);
        }

        SelectedGroup = null;
        RefreshPendingChanges();
        AddStatusMessage($"Löschen von '{group.Name}' geplant.");
    }

    [RelayCommand]
    private void AddSelectedVmsToGroup()
    {
        if (SelectedGroup is null)
        {
            AddStatusMessage("Bitte zuerst eine Gruppe auswählen.");
            return;
        }

        if (_pendingNewGroupIds.Contains(SelectedGroup.Id))
        {
            AddStatusMessage("Diese Gruppe ist noch nicht angewendet. Bitte zuerst 'Änderungen anwenden'.");
            return;
        }

        if (SelectedVirtualMachines.Count == 0)
        {
            AddStatusMessage("Bitte mindestens eine VM auswählen.");
            return;
        }

        foreach (var vm in SelectedVirtualMachines)
        {
            _changeQueue.Add(new VmGroupMembershipChange
            {
                ChangeType = VmGroupChangeType.AddMembership,
                VmId = vm.Id,
                VmName = vm.Name,
                GroupId = SelectedGroup.Id,
                GroupName = SelectedGroup.Name,
                Description = $"'{vm.Name}' zu '{SelectedGroup.Name}' hinzufügen",
            });
        }

        RefreshPendingChanges();
        AddStatusMessage($"{SelectedVirtualMachines.Count} VM(s) zu '{SelectedGroup.Name}' geplant.");
    }

    [RelayCommand]
    private void RemoveSelectedVmsFromGroup()
    {
        if (SelectedGroup is null)
        {
            AddStatusMessage("Bitte zuerst eine Gruppe auswählen.");
            return;
        }

        if (SelectedVirtualMachines.Count == 0)
        {
            AddStatusMessage("Bitte mindestens eine VM auswählen.");
            return;
        }

        foreach (var vm in SelectedVirtualMachines)
        {
            _changeQueue.Add(new VmGroupMembershipChange
            {
                ChangeType = VmGroupChangeType.RemoveMembership,
                VmId = vm.Id,
                VmName = vm.Name,
                GroupId = SelectedGroup.Id,
                GroupName = SelectedGroup.Name,
                Description = $"'{vm.Name}' aus '{SelectedGroup.Name}' entfernen",
            });
        }

        RefreshPendingChanges();
        AddStatusMessage($"Entfernen von {SelectedVirtualMachines.Count} VM(s) aus '{SelectedGroup.Name}' geplant.");
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task ApplyChangesAsync(CancellationToken cancellationToken)
    {
        if (_changeQueue.Changes.Count == 0)
        {
            AddStatusMessage("Keine Änderungen zum Anwenden vorhanden.");
            return;
        }

        IsBusy = true;

        try
        {
            var orderedChanges = _changeQueue.GetInExecutionOrder();
            var applyResult = await _groupService.ApplyChangesAsync(TargetName, orderedChanges, cancellationToken);

            ChangesApplied?.Invoke(this, BuildApplyResultSummary(applyResult));

            if (applyResult.Success)
            {
                AddStatusMessage($"{applyResult.Results.Count} Änderung(en) erfolgreich angewendet.");
                _logService.LogInformation($"{applyResult.Results.Count} Änderung(en) auf '{TargetName}' angewendet.");

                _changeQueue.Clear();
                _pendingNewGroupIds.Clear();
                RefreshPendingChanges();
            }
            else
            {
                var failedCount = applyResult.Results.Count(r => !r.Success);
                AddStatusMessage($"Anwenden der Änderungen teilweise fehlgeschlagen ({failedCount} von {applyResult.Results.Count}).");
                _logService.LogError($"Anwenden der Änderungen auf '{TargetName}' teilweise fehlgeschlagen.");

                // Bereits serverseitig erfolgreich ausgeführte Änderungen aus der Queue
                // entfernen; fehlgeschlagene/nicht ausgeführte bleiben für einen erneuten Versuch.
                RemoveAppliedChangesFromQueue(applyResult.Results);
            }

            await LoadDataAsync(cancellationToken);
        }
        catch (VmGroupOperationException ex)
        {
            AddStatusMessage($"Anwenden der Änderungen fehlgeschlagen: {ex.Message}");
            _logService.LogError("Anwenden der Änderungen fehlgeschlagen.", ex);
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildApplyResultSummary(ApplyChangesResult applyResult)
    {
        var header = applyResult.Success
            ? $"{applyResult.Results.Count} Änderung(en) erfolgreich angewendet:"
            : $"{applyResult.Results.Count(r => !r.Success)} von {applyResult.Results.Count} Änderung(en) fehlgeschlagen:";

        var lines = applyResult.Results.Select(r =>
            r.Success ? $"- OK: {r.Description}" : $"- FEHLER: {r.Description} ({r.Error})");

        return header + "\n" + string.Join("\n", lines);
    }

    private void RemoveAppliedChangesFromQueue(IReadOnlyList<ChangeApplicationResult> results)
    {
        foreach (var result in results.Where(r => r.Success))
        {
            var match = _changeQueue.Changes.FirstOrDefault(c =>
                c.ChangeType == result.ChangeType && c.VmId == result.VmId && c.GroupId == result.GroupId);

            if (match is null)
            {
                continue;
            }

            _changeQueue.Remove(match);

            if (match.ChangeType == VmGroupChangeType.CreateGroup)
            {
                _pendingNewGroupIds.Remove(match.GroupId);
            }
        }

        RefreshPendingChanges();
    }

    [RelayCommand]
    private void DiscardChanges()
    {
        foreach (var pendingGroupId in _pendingNewGroupIds)
        {
            RemoveLocalGroup(pendingGroupId);
        }

        _changeQueue.Clear();
        _pendingNewGroupIds.Clear();
        RefreshPendingChanges();
        AddStatusMessage("Geplante Änderungen verworfen.");
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task ExportConfigurationAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            var json = ConfigurationExportBuilder.Build(TargetName, _allGroups);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);

            AddStatusMessage($"Konfiguration exportiert nach '{filePath}'.");
            _logService.LogInformation($"Konfiguration exportiert nach '{filePath}'.");
        }
        catch (IOException ex)
        {
            AddStatusMessage($"Export fehlgeschlagen: {ex.Message}");
            _logService.LogError("Export der Konfiguration fehlgeschlagen.", ex);
        }
    }

    private void RefreshPendingChanges()
    {
        PendingChanges.Clear();
        foreach (var change in _changeQueue.Changes)
        {
            PendingChanges.Add(change);
        }

        OnPropertyChanged(nameof(PendingChangeCount));
    }

    private void UpdateLocalGroupName(Guid groupId, string newName)
    {
        var index = _allGroups.FindIndex(g => g.Id == groupId);
        if (index >= 0)
        {
            _allGroups[index] = _allGroups[index] with { Name = newName };
        }

        var displayedGroup = Groups.FirstOrDefault(g => g.Id == groupId);
        if (displayedGroup is not null)
        {
            var position = Groups.IndexOf(displayedGroup);
            var updatedGroup = displayedGroup with { Name = newName };
            Groups[position] = updatedGroup;

            if (SelectedGroup?.Id == groupId)
            {
                SelectedGroup = updatedGroup;
            }
        }

        ApplyGroupFilter();
    }

    private void RemoveLocalGroup(Guid groupId)
    {
        _allGroups.RemoveAll(g => g.Id == groupId);

        var displayedGroup = Groups.FirstOrDefault(g => g.Id == groupId);
        if (displayedGroup is not null)
        {
            Groups.Remove(displayedGroup);
        }

        ApplyGroupFilter();
    }

    private void AddStatusMessage(string message) => StatusMessages.Insert(0, $"{DateTime.Now:HH:mm:ss} {message}");
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperVGroupManager.App.Services;
using HyperVGroupManager.App.Localization;
using HyperVGroupManager.Core.Exceptions;
using HyperVGroupManager.Core.Interfaces;
using HyperVGroupManager.Core.Models;
using HyperVGroupManager.Core.Results;
using HyperVGroupManager.Core.Services;

namespace HyperVGroupManager.App.ViewModels;

/// <summary>
/// Zentrales, WPF-unabhängig testbares ViewModel. Der angezeigte Zustand ist immer die
/// Projektion aus dem letzten Server-Snapshot und allen geplanten Änderungen.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private const int MaximumStatusMessages = 100;

    private readonly IHyperVGroupService _groupService;
    private readonly ILogService _logService;
    private readonly ApplicationOptions _applicationOptions;
    private readonly VmGroupChangeQueue _changeQueue = new();
    private readonly HashSet<Guid> _pendingNewGroupIds = new();

    private List<VirtualMachineInfo> _serverVirtualMachines = new();
    private List<VmGroupInfo> _serverGroups = new();
    private List<VirtualMachineInfo> _allVirtualMachines = new();
    private List<VmGroupInfo> _allGroups = new();
    private string? _connectedTargetName;
    private bool _requiresRefreshBeforeApply;

    [ObservableProperty]
    private string _targetName = string.Empty;

    private string _connectionStatusKey = "Connection.Disconnected";

    public string ConnectionStatus => L(_connectionStatusKey);

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
    public ObservableCollection<VmGroupInfo> FilteredGroups { get; } = new();
    public ObservableCollection<VirtualMachineInfo> VirtualMachines { get; } = new();
    public ObservableCollection<VirtualMachineRowViewModel> VirtualMachineRows { get; } = new();
    public ObservableCollection<VmGroupMembershipChange> PendingChanges { get; } = new();
    public ObservableCollection<string> StatusMessages { get; } = new();
    public ObservableCollection<VirtualMachineInfo> SelectedVirtualMachines { get; } = new();

    public string DefaultGroupPrefix => _applicationOptions.DefaultGroupPrefix;
    public bool ConfirmBeforeApply => _applicationOptions.ConfirmBeforeApply;
    public string ConnectedTargetName => _connectedTargetName ?? "—";
    public int VirtualMachineCount => _allVirtualMachines.Count;
    public int GroupCount => _allGroups.Count;
    public int PendingChangeCount => PendingChanges.Count;
    public int UntaggedVirtualMachineCount => _allVirtualMachines.Count(vm => vm.GroupNames.Count == 0);
    public int EffectiveMembershipCount => _allGroups.Sum(group => group.MemberCount);
    public bool HasPendingChanges => PendingChanges.Count > 0;
    public bool IsStateUncertain => _requiresRefreshBeforeApply;
    public bool CanModifyPlan => IsConnected && !IsBusy && !_requiresRefreshBeforeApply;
    public bool CanUseConnectedTarget => IsConnected && !IsBusy;
    public bool CanApplyChanges => CanUseConnectedTarget && HasPendingChanges && !_requiresRefreshBeforeApply;
    public bool CanModifySelectedGroup => CanModifyPlan && SelectedGroup is not null;
    public bool CanChangeMembership => CanModifySelectedGroup && SelectedVirtualMachines.Count > 0;
    public bool IsConnectionError => _connectionStatusKey is "Connection.Error" or "Connection.VerifyState";
    public string LastRefreshedAtDisplay => LastRefreshedAt?.ToString("HH:mm:ss") ?? "—";
    public string EmptyGroupsMessage => L(IsConnected ? "Groups.EmptyConnected" : "Groups.EmptyDisconnected");
    public string EmptyVmsMessage => L(IsConnected ? "Vms.EmptyConnected" : "Vms.EmptyDisconnected");

    private bool CanInteract => !IsBusy;

    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<string>? ChangesApplied;

    public MainViewModel(IHyperVGroupService groupService, ILogService logService, ApplicationOptions applicationOptions)
    {
        _groupService = groupService;
        _logService = logService;
        _applicationOptions = applicationOptions;
        LocalizationService.Instance.PropertyChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Item[]" or nameof(LocalizationService.CurrentLanguageCode))
        {
            OnPropertyChanged(nameof(ConnectionStatus));
            OnPropertyChanged(nameof(EmptyGroupsMessage));
            OnPropertyChanged(nameof(EmptyVmsMessage));
            RefreshPendingChanges();
        }
    }

    private static string L(string key, params object?[] arguments) =>
        arguments.Length == 0
            ? LocalizationService.Instance.Get(key)
            : LocalizationService.Instance.Format(key, arguments);

    private void SetConnectionStatus(string key)
    {
        _connectionStatusKey = key;
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(IsConnectionError));
    }

    partial void OnIsBusyChanged(bool value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        ApplyChangesCommand.NotifyCanExecuteChanged();
        ExportConfigurationCommand.NotifyCanExecuteChanged();
        NotifyPlanningCommandStateChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUseConnectedTarget));
        OnPropertyChanged(nameof(CanApplyChanges));
        OnPropertyChanged(nameof(CanModifySelectedGroup));
        OnPropertyChanged(nameof(CanChangeMembership));
    }

    partial void OnIsConnectedChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ExportConfigurationCommand.NotifyCanExecuteChanged();
        ApplyChangesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUseConnectedTarget));
        OnPropertyChanged(nameof(CanApplyChanges));
        OnPropertyChanged(nameof(EmptyGroupsMessage));
        OnPropertyChanged(nameof(EmptyVmsMessage));
        NotifyPlanningCommandStateChanged();
    }

    partial void OnLastRefreshedAtChanged(DateTime? value) => OnPropertyChanged(nameof(LastRefreshedAtDisplay));

    partial void OnSearchTextChanged(string value) => ApplyVmFilter();
    partial void OnGroupSearchTextChanged(string value) => ApplyGroupFilter();
    partial void OnSelectedFilterModeChanged(VmFilterMode value) => ApplyVmFilter();

    partial void OnSelectedGroupChanged(VmGroupInfo? value)
    {
        RenameGroupCommand.NotifyCanExecuteChanged();
        DeleteGroupCommand.NotifyCanExecuteChanged();
        AddSelectedVmsToGroupCommand.NotifyCanExecuteChanged();
        RemoveSelectedVmsFromGroupCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanModifySelectedGroup));
        OnPropertyChanged(nameof(CanChangeMembership));
        if (SelectedFilterMode == VmFilterMode.SelectedGroup)
        {
            ApplyVmFilter();
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var requestedTarget = TargetName.Trim();
        if (requestedTarget.Length == 0)
        {
            AddStatusMessage(L("Status.EnterTarget"));
            return;
        }

        if (HasPendingChanges && _connectedTargetName is not null &&
            !string.Equals(requestedTarget, _connectedTargetName, StringComparison.OrdinalIgnoreCase))
        {
            var message = L("Status.TargetSwitchBlocked", _connectedTargetName, requestedTarget);
            AddStatusMessage(message);
            ErrorOccurred?.Invoke(this, message);
            return;
        }

        IsBusy = true;
        SetConnectionStatus("Connection.Connecting");

        try
        {
            var environment = await _groupService.TestEnvironmentAsync(requestedTarget, cancellationToken);
            foreach (var warning in environment.Warnings)
            {
                AddStatusMessage(L("Status.Warning", warning));
            }

            if (_connectedTargetName is not null &&
                !string.Equals(requestedTarget, _connectedTargetName, StringComparison.OrdinalIgnoreCase))
            {
                ClearPendingChanges();
            }

            await LoadDataAsync(requestedTarget, cancellationToken);
            _connectedTargetName = requestedTarget;
            TargetName = requestedTarget;
            IsConnected = true;
            SetConnectionStatus("Connection.Connected");
            OnPropertyChanged(nameof(ConnectedTargetName));
            _logService.LogInformation(
                $"Verbindung zu '{requestedTarget}' aufgebaut ({environment.TargetType}, Nodes: {string.Join(", ", environment.Nodes)}).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetConnectionStatus("Connection.Cancelled");
            AddStatusMessage(L("Status.ConnectCancelled"));
        }
        catch (Exception ex) when (ex is HyperVConnectionException or HyperVModuleMissingException or PowerShellExecutionException)
        {
            IsConnected = false;
            SetConnectionStatus("Connection.Error");
            AddStatusMessage(L("Status.ConnectFailed", ex.Message));
            _logService.LogError($"Verbindung zu '{requestedTarget}' fehlgeschlagen.", ex);
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseConnectedTarget))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!TryGetConnectedTarget(out var target))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await LoadDataAsync(target, cancellationToken);
            SetConnectionStatus("Connection.Connected");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AddStatusMessage(L("Status.RefreshCancelled"));
        }
        catch (Exception ex) when (ex is HyperVConnectionException or PowerShellExecutionException)
        {
            AddStatusMessage(L("Status.RefreshFailed", ex.Message));
            _logService.LogError("Aktualisierung fehlgeschlagen.", ex);
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDataAsync(string target, CancellationToken cancellationToken)
    {
        var vms = await _groupService.GetVirtualMachinesAsync(target, cancellationToken);
        var groups = await _groupService.GetGroupsAsync(target, cancellationToken);

        var snapshotValidation = ServerSnapshotValidator.Validate(vms, groups);
        if (!snapshotValidation.IsValid)
        {
            throw new HyperVConnectionException(
                "Der gelesene Serverzustand ist inkonsistent und wurde nicht übernommen: " +
                string.Join("; ", snapshotValidation.Errors));
        }

        foreach (var warning in snapshotValidation.Warnings)
        {
            AddStatusMessage(L("Status.DataCheck", warning));
            _logService.LogWarning($"Server-Snapshot '{target}': {warning}");
        }

        // Beide Ergebnisse werden erst gemeinsam übernommen. So entsteht kein halb
        // aktualisierter Snapshot, falls der zweite Lesevorgang fehlschlägt.
        _serverVirtualMachines = vms.ToList();
        _serverGroups = groups.ToList();
        _requiresRefreshBeforeApply = false;
        NotifyPlanningCommandStateChanged();
        RebuildEffectiveState();
        LastRefreshedAt = DateTime.Now;
        OnPropertyChanged(nameof(IsStateUncertain));
        AddStatusMessage(L("Status.Loaded", _serverVirtualMachines.Count, _serverGroups.Count));
    }

    [RelayCommand(CanExecute = nameof(CanModifyPlan))]
    private void CreateGroup(string? groupName)
    {
        var validation = GroupNameValidator.Validate(groupName, _allGroups.Select(group => group.Name));
        if (!validation.IsValid)
        {
            AddStatusMessage(L("Status.CreateInvalid", validation.ErrorMessage));
            return;
        }

        var trimmedName = groupName!.Trim();
        var pendingGroupId = Guid.NewGuid();
        _changeQueue.Add(new VmGroupMembershipChange
        {
            ChangeType = VmGroupChangeType.CreateGroup,
            GroupId = pendingGroupId,
            GroupName = trimmedName,
            Description = L("Change.DescriptionCreate", trimmedName),
        });

        QueueChanged();
        SelectedGroup = _allGroups.FirstOrDefault(group => group.Id == pendingGroupId);
        AddStatusMessage(L("Status.GroupPlanned", trimmedName));
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedGroup))]
    private void RenameGroup(string? newName)
    {
        if (SelectedGroup is null)
        {
            AddStatusMessage(L("Dialog.SelectGroup"));
            return;
        }

        var group = SelectedGroup;
        var validation = GroupNameValidator.Validate(newName, _allGroups.Where(item => item.Id != group.Id).Select(item => item.Name));
        if (!validation.IsValid)
        {
            AddStatusMessage(L("Status.RenameInvalid", validation.ErrorMessage));
            return;
        }

        var trimmedName = newName!.Trim();
        if (string.Equals(group.Name, trimmedName, StringComparison.Ordinal))
        {
            AddStatusMessage(L("Status.NameUnchanged"));
            return;
        }

        if (_pendingNewGroupIds.Contains(group.Id))
        {
            var pendingCreate = _changeQueue.Changes.First(change =>
                change.ChangeType == VmGroupChangeType.CreateGroup && change.GroupId == group.Id);
            _changeQueue.Remove(pendingCreate);
            _changeQueue.Add(pendingCreate with
            {
                GroupName = trimmedName,
                Description = L("Change.DescriptionCreate", trimmedName),
            });
        }
        else
        {
            _changeQueue.Add(new VmGroupMembershipChange
            {
                ChangeType = VmGroupChangeType.RenameGroup,
                GroupId = group.Id,
                GroupName = trimmedName,
                Description = L("Change.DescriptionRename", group.Name, trimmedName),
            });
        }

        QueueChanged(group.Id);
        AddStatusMessage(L("Status.RenamePlanned", group.Name, trimmedName));
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedGroup))]
    private void DeleteGroup()
    {
        if (SelectedGroup is null)
        {
            AddStatusMessage(L("Dialog.SelectGroup"));
            return;
        }

        var group = SelectedGroup;
        if (!_pendingNewGroupIds.Contains(group.Id) && !VmGroupRules.CanDeleteGroup(group))
        {
            AddStatusMessage(VmGroupRules.BuildNonEmptyGroupDeletionMessage(group));
            return;
        }

        _changeQueue.Add(new VmGroupMembershipChange
        {
            ChangeType = VmGroupChangeType.DeleteGroup,
            GroupId = group.Id,
            GroupName = group.Name,
            Description = L("Change.DescriptionDelete", group.Name),
        });

        SelectedGroup = null;
        QueueChanged();
        AddStatusMessage(L("Status.DeletePlanned", group.Name));
    }

    [RelayCommand(CanExecute = nameof(CanChangeMembership))]
    private void AddSelectedVmsToGroup()
    {
        if (!TryGetMembershipSelection(out var group, out var selectedVms))
        {
            return;
        }

        var added = 0;
        var skipped = 0;
        foreach (var selectedVm in selectedVms)
        {
            var currentVm = _allVirtualMachines.FirstOrDefault(vm => vm.Id == selectedVm.Id);
            if (currentVm is null || currentVm.GroupNames.Contains(group.Name, StringComparer.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var result = _changeQueue.Add(new VmGroupMembershipChange
            {
                ChangeType = VmGroupChangeType.AddMembership,
                VmId = currentVm.Id,
                VmName = currentVm.Name,
                GroupId = group.Id,
                GroupName = group.Name,
                Description = L("Change.DescriptionAdd", currentVm.Name, group.Name),
            });
            if (result is ChangeQueueAddResult.Added or ChangeQueueAddResult.Updated)
            {
                added++;
            }
            else
            {
                skipped++;
            }
        }

        QueueChanged(group.Id);
        AddStatusMessage(BuildMembershipStatus(added, skipped, L("Operation.AddedTo", group.Name)));
    }

    [RelayCommand(CanExecute = nameof(CanChangeMembership))]
    private void RemoveSelectedVmsFromGroup()
    {
        if (!TryGetMembershipSelection(out var group, out var selectedVms))
        {
            return;
        }

        var removed = 0;
        var skipped = 0;
        foreach (var selectedVm in selectedVms)
        {
            var currentVm = _allVirtualMachines.FirstOrDefault(vm => vm.Id == selectedVm.Id);
            if (currentVm is null || !currentVm.GroupNames.Contains(group.Name, StringComparer.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var result = _changeQueue.Add(new VmGroupMembershipChange
            {
                ChangeType = VmGroupChangeType.RemoveMembership,
                VmId = currentVm.Id,
                VmName = currentVm.Name,
                GroupId = group.Id,
                GroupName = group.Name,
                Description = L("Change.DescriptionRemove", currentVm.Name, group.Name),
            });
            if (result is ChangeQueueAddResult.Added or ChangeQueueAddResult.Updated)
            {
                removed++;
            }
            else
            {
                skipped++;
            }
        }

        QueueChanged(group.Id);
        AddStatusMessage(BuildMembershipStatus(removed, skipped, L("Operation.RemovedFrom", group.Name)));
    }

    [RelayCommand(CanExecute = nameof(CanApplyChanges))]
    private async Task ApplyChangesAsync(CancellationToken cancellationToken)
    {
        if (!TryGetConnectedTarget(out var target))
        {
            return;
        }

        if (_requiresRefreshBeforeApply)
        {
            var message = L("Status.StateUncertain");
            AddStatusMessage(message);
            ErrorOccurred?.Invoke(this, message);
            return;
        }

        if (!HasPendingChanges)
        {
            AddStatusMessage(L("Status.NoChanges"));
            return;
        }

        var orderedChanges = _changeQueue.GetInExecutionOrder();
        var validation = ChangeSetValidator.Validate(_serverVirtualMachines, _serverGroups, orderedChanges);
        foreach (var warning in validation.Warnings)
        {
            AddStatusMessage(L("Status.Preflight", warning));
        }

        if (!validation.IsValid)
        {
            var message = L("Status.PreflightBlocked", string.Join("\n- ", validation.Errors));
            AddStatusMessage(L("Status.PreflightFailed"));
            _logService.LogError(message);
            ErrorOccurred?.Invoke(this, message);
            return;
        }

        IsBusy = true;
        var serverMayHaveChanged = false;
        try
        {
            var applyResult = await _groupService.ApplyChangesAsync(target, orderedChanges, cancellationToken);
            serverMayHaveChanged = true;
            ChangesApplied?.Invoke(this, BuildApplyResultSummary(applyResult));

            if (applyResult.Success)
            {
                AddStatusMessage(L("Status.ApplySuccess", applyResult.Results.Count));
                _logService.LogInformation($"{applyResult.Results.Count} Änderung(en) auf '{target}' angewendet.");
                ClearPendingChanges();
            }
            else
            {
                var failedCount = applyResult.Results.Count(result => !result.Success);
                AddStatusMessage(L("Status.ApplyPartial", failedCount, applyResult.Results.Count));
                _logService.LogError($"Änderungslauf auf '{target}' teilweise fehlgeschlagen.");
                RemoveAppliedChangesFromQueue(applyResult.Results);
            }

            try
            {
                await LoadDataAsync(target, cancellationToken);
            }
            catch (Exception ex) when (ex is HyperVConnectionException or PowerShellExecutionException)
            {
                MarkStateUncertain(L("Status.PostReadFailed"), ex);
            }
        }
        catch (OperationCanceledException)
        {
            MarkStateUncertain(
                serverMayHaveChanged
                    ? L("Status.PostCheckCancelled")
                    : L("Status.ApplyCancelled"));
        }
        catch (Exception ex) when (ex is VmGroupOperationException or PowerShellExecutionException or HyperVConnectionException)
        {
            MarkStateUncertain(L("Status.ApplyUnknownFailure"), ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
    private void DiscardChanges()
    {
        ClearPendingChanges();
        RebuildEffectiveState();
        AddStatusMessage(L("Status.Discarded"));
    }

    [RelayCommand(CanExecute = nameof(CanUseConnectedTarget))]
    private async Task ExportConfigurationAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (!TryGetConnectedTarget(out var target))
        {
            return;
        }

        try
        {
            var json = ConfigurationExportBuilder.Build(target, _allGroups);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
            AddStatusMessage(L("Status.ExportSuccess", filePath));
            _logService.LogInformation($"Konfiguration exportiert nach '{filePath}'.");
        }
        catch (IOException ex)
        {
            AddStatusMessage(L("Status.ExportFailed", ex.Message));
            _logService.LogError("Export der Konfiguration fehlgeschlagen.", ex);
            ErrorOccurred?.Invoke(this, ex.Message);
        }
    }

    private void QueueChanged(Guid? selectedGroupId = null)
    {
        RefreshPendingChanges();
        RebuildEffectiveState(selectedGroupId);
    }

    private void RebuildEffectiveState(Guid? selectedGroupId = null)
    {
        selectedGroupId ??= SelectedGroup?.Id;
        var selectedVmIds = SelectedVirtualMachines.Select(vm => vm.Id).ToHashSet();
        var effectiveState = EffectiveConfigurationBuilder.Build(
            _serverVirtualMachines,
            _serverGroups,
            _changeQueue.GetInExecutionOrder());

        _allVirtualMachines = effectiveState.VirtualMachines.ToList();
        _allGroups = effectiveState.Groups.ToList();

        Groups.Clear();
        foreach (var group in _allGroups)
        {
            Groups.Add(group);
        }

        _pendingNewGroupIds.Clear();
        foreach (var groupId in _changeQueue.Changes
                     .Where(change => change.ChangeType == VmGroupChangeType.CreateGroup)
                     .Select(change => change.GroupId))
        {
            _pendingNewGroupIds.Add(groupId);
        }

        SelectedGroup = selectedGroupId.HasValue
            ? _allGroups.FirstOrDefault(group => group.Id == selectedGroupId.Value)
            : null;

        SelectedVirtualMachines.Clear();
        foreach (var vm in _allVirtualMachines.Where(vm => selectedVmIds.Contains(vm.Id)))
        {
            SelectedVirtualMachines.Add(vm);
        }

        ApplyGroupFilter();
        ApplyVmFilter();
        OnPropertyChanged(nameof(VirtualMachineCount));
        OnPropertyChanged(nameof(GroupCount));
        OnPropertyChanged(nameof(UntaggedVirtualMachineCount));
        OnPropertyChanged(nameof(EffectiveMembershipCount));
    }

    private void RefreshPendingChanges()
    {
        PendingChanges.Clear();
        foreach (var change in _changeQueue.Changes)
        {
            PendingChanges.Add(change);
        }

        OnPropertyChanged(nameof(PendingChangeCount));
        OnPropertyChanged(nameof(HasPendingChanges));
        DiscardChangesCommand.NotifyCanExecuteChanged();
        ApplyChangesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanApplyChanges));
    }

    private void ApplyVmFilter()
    {
        var filtered = VirtualMachineFilter.Apply(_allVirtualMachines, SelectedFilterMode, SearchText, SelectedGroup).ToList();
        VirtualMachines.Clear();
        VirtualMachineRows.Clear();
        foreach (var vm in filtered)
        {
            VirtualMachines.Add(vm);
            VirtualMachineRows.Add(BuildVirtualMachineRow(vm));
        }
    }

    private VirtualMachineRowViewModel BuildVirtualMachineRow(VirtualMachineInfo vm)
    {
        var vmChanges = _changeQueue.Changes.Where(change => change.VmId == vm.Id).ToArray();
        var additions = vmChanges
            .Where(change => change.ChangeType == VmGroupChangeType.AddMembership)
            .Select(change => change.GroupName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var removals = vmChanges
            .Where(change => change.ChangeType == VmGroupChangeType.RemoveMembership)
            .Select(change => change.GroupName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var renames = _changeQueue.Changes
            .Where(change => change.ChangeType == VmGroupChangeType.RenameGroup)
            .Select(change => new
            {
                Change = change,
                OriginalGroup = _serverGroups.FirstOrDefault(group => group.Id == change.GroupId),
            })
            .Where(item => item.OriginalGroup?.MemberVmIds.Contains(vm.Id) == true)
            .Select(item => $"{item.OriginalGroup!.Name} → {item.Change.GroupName}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new VirtualMachineRowViewModel(vm, additions, removals, renames);
    }

    public void NotifyVmSelectionChanged()
    {
        AddSelectedVmsToGroupCommand.NotifyCanExecuteChanged();
        RemoveSelectedVmsFromGroupCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanChangeMembership));
    }

    private void ApplyGroupFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(GroupSearchText)
            ? _allGroups
            : _allGroups.Where(group => group.Name.Contains(GroupSearchText.Trim(), StringComparison.OrdinalIgnoreCase));

        FilteredGroups.Clear();
        foreach (var group in filtered)
        {
            FilteredGroups.Add(group);
        }
    }

    private bool TryGetConnectedTarget(out string target)
    {
        target = _connectedTargetName ?? string.Empty;
        if (IsConnected && target.Length > 0)
        {
            return true;
        }

        AddStatusMessage(L("Status.ConnectTargetFirst"));
        return false;
    }

    private bool CanDiscardChanges() => HasPendingChanges && !IsBusy;

    private bool TryGetMembershipSelection(out VmGroupInfo group, out IReadOnlyList<VirtualMachineInfo> selectedVms)
    {
        group = SelectedGroup!;
        selectedVms = SelectedVirtualMachines.GroupBy(vm => vm.Id).Select(items => items.First()).ToArray();
        if (SelectedGroup is null)
        {
            AddStatusMessage(L("Dialog.SelectGroup"));
            return false;
        }

        group = SelectedGroup;
        if (selectedVms.Count == 0)
        {
            AddStatusMessage(L("Status.SelectVm"));
            return false;
        }

        return true;
    }

    private void RemoveAppliedChangesFromQueue(IReadOnlyList<ChangeApplicationResult> results)
    {
        foreach (var result in results.Where(result => result.Success))
        {
            var match = _changeQueue.Changes.FirstOrDefault(change =>
                change.ChangeType == result.ChangeType && change.VmId == result.VmId && change.GroupId == result.GroupId);
            if (match is not null)
            {
                _changeQueue.Remove(match);
            }
        }

        QueueChanged();
    }

    private void ClearPendingChanges()
    {
        _changeQueue.Clear();
        _pendingNewGroupIds.Clear();
        RefreshPendingChanges();
    }

    private void MarkStateUncertain(string message, Exception? exception = null)
    {
        _requiresRefreshBeforeApply = true;
        SetConnectionStatus("Connection.VerifyState");
        OnPropertyChanged(nameof(IsStateUncertain));
        NotifyPlanningCommandStateChanged();
        AddStatusMessage(message);
        _logService.LogError(message, exception);
        ErrorOccurred?.Invoke(this, message + (exception is null ? string.Empty : $"\n\n{L("Result.Details", exception.Message)}"));
    }

    private static string BuildApplyResultSummary(ApplyChangesResult applyResult)
    {
        var header = applyResult.Success
            ? L("Result.SuccessHeader", applyResult.Results.Count)
            : L("Result.FailedHeader", applyResult.Results.Count(result => !result.Success), applyResult.Results.Count);
        var lines = applyResult.Results.Select(result =>
            result.Success ? $"- OK: {result.Description}" : $"- {L("Result.Error")}: {result.Description} ({result.Error})");
        return header + "\n" + string.Join("\n", lines);
    }

    private void NotifyPlanningCommandStateChanged()
    {
        CreateGroupCommand.NotifyCanExecuteChanged();
        RenameGroupCommand.NotifyCanExecuteChanged();
        DeleteGroupCommand.NotifyCanExecuteChanged();
        AddSelectedVmsToGroupCommand.NotifyCanExecuteChanged();
        RemoveSelectedVmsFromGroupCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanModifyPlan));
        OnPropertyChanged(nameof(CanModifySelectedGroup));
        OnPropertyChanged(nameof(CanChangeMembership));
        OnPropertyChanged(nameof(CanApplyChanges));
        ApplyChangesCommand.NotifyCanExecuteChanged();
    }

    private static string BuildMembershipStatus(int changed, int skipped, string operation)
    {
        var suffix = skipped > 0 ? L("Status.AlreadyDesired", skipped) : string.Empty;
        return L("Status.MembershipPlanned", changed, operation, suffix);
    }

    private void AddStatusMessage(string message)
    {
        StatusMessages.Insert(0, $"{DateTime.Now:HH:mm:ss} {message}");
        while (StatusMessages.Count > MaximumStatusMessages)
        {
            StatusMessages.RemoveAt(StatusMessages.Count - 1);
        }
    }
}

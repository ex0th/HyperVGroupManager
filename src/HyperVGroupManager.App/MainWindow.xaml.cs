using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HyperVGroupManager.App.Localization;
using HyperVGroupManager.App.Services;
using HyperVGroupManager.App.ViewModels;
using HyperVGroupManager.App.Views;
using HyperVGroupManager.Core.Interfaces;
using Microsoft.Win32;

namespace HyperVGroupManager.App
{
    /// <summary>
    /// Reine UI-Orchestrierung: Dialoge anzeigen, Auswahl synchronisieren, Befehle des
    /// MainViewModel mit den gesammelten Eingaben aufrufen. Keine Business-Logik hier.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly IHyperVGroupService _hyperVGroupService;
        private readonly EmailReportService _emailReportService;
        private readonly SupportPackageService _supportPackageService;

        public MainWindow(
            MainViewModel viewModel,
            IHyperVGroupService hyperVGroupService,
            EmailReportService emailReportService,
            SupportPackageService supportPackageService)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _hyperVGroupService = hyperVGroupService;
            _emailReportService = emailReportService;
            _supportPackageService = supportPackageService;
            DataContext = _viewModel;

            _viewModel.ErrorOccurred += OnViewModelErrorOccurred;
            _viewModel.ChangesApplied += OnViewModelChangesApplied;
        }

        private static string L(string key, params object?[] arguments) =>
            arguments.Length == 0
                ? LocalizationService.Instance.Get(key)
                : LocalizationService.Instance.Format(key, arguments);

        private void OnViewModelErrorOccurred(object? sender, string message) =>
            new MessageDialog(L("Dialog.ErrorDetails"), message) { Owner = this }.ShowDialog();

        private void OnViewModelChangesApplied(object? sender, string summary) =>
            new MessageDialog(L("Dialog.ApplyResult"), summary) { Owner = this }.ShowDialog();

        private void HelpButton_Click(object sender, RoutedEventArgs e) => ShowHelp();

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.F1)
            {
                return;
            }

            ShowHelp();
            e.Handled = true;
        }

        private void ShowHelp() =>
            new HelpWindow(_supportPackageService, _viewModel.ConnectedTargetName) { Owner = this }.ShowDialog();

        private void VirtualMachinesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel.SelectedVirtualMachines.Clear();
            foreach (var item in VirtualMachinesGrid.SelectedItems.Cast<VirtualMachineRowViewModel>())
            {
                _viewModel.SelectedVirtualMachines.Add(item.Source);
            }

            _viewModel.NotifyVmSelectionChanged();
        }

        private void NewGroupButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NewGroupDialog(_viewModel.DefaultGroupPrefix) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _viewModel.CreateGroupCommand.Execute(dialog.GroupName);
            }
        }

        private void RenameGroupButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedGroup = _viewModel.SelectedGroup;
            if (selectedGroup is null)
            {
                MessageBox.Show(this, L("Dialog.SelectGroup"), L("Dialog.Information"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new RenameGroupDialog(selectedGroup.Name) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _viewModel.RenameGroupCommand.Execute(dialog.NewName);
            }
        }

        private void DeleteGroupButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedGroup = _viewModel.SelectedGroup;
            if (selectedGroup is null)
            {
                MessageBox.Show(this, L("Dialog.SelectGroup"), L("Dialog.Information"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new DeleteGroupConfirmationDialog(selectedGroup.Name, selectedGroup.MemberCount) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _viewModel.DeleteGroupCommand.Execute(null);
            }
        }

        private void ClusterConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.IsConnected)
            {
                MessageBox.Show(this, L("Dialog.ConnectFirst"), L("Dialog.NoConnection"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            new ClusterConfigDialog(_hyperVGroupService, _viewModel.ConnectedTargetName, _emailReportService) { Owner = this }.ShowDialog();
        }

        private async void ExportConfigurationButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.IsConnected)
            {
                MessageBox.Show(this, L("Dialog.ConnectFirst"), L("Dialog.NoConnection"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = L("Dialog.JsonFilter"),
                FileName = $"HyperVGroupManager-Export-{DateTime.Now:yyyy-MM-dd}.json",
            };

            if (saveFileDialog.ShowDialog(this) == true)
            {
                await _viewModel.ExportConfigurationCommand.ExecuteAsync(saveFileDialog.FileName);
            }
        }

        private async void ApplyChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.ApplyChangesCommand.CanExecute(null))
            {
                return;
            }

            if (_viewModel.ConfirmBeforeApply && _viewModel.PendingChangeCount > 0)
            {
                var confirmation = MessageBox.Show(
                    this,
                    L("Dialog.ApplyConfirm", _viewModel.PendingChangeCount, _viewModel.ConnectedTargetName),
                    L("Dialog.ApplyTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            await _viewModel.ApplyChangesCommand.ExecuteAsync(null);
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_viewModel.HasPendingChanges)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                L("Dialog.CloseConfirm", _viewModel.PendingChangeCount),
                L("Dialog.CloseTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            e.Cancel = result != MessageBoxResult.Yes;
        }
    }
}

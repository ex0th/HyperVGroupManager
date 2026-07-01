using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HyperVGroupManager.App.Services;
using HyperVGroupManager.App.ViewModels;
using HyperVGroupManager.App.Views;
using HyperVGroupManager.Core.Interfaces;
using HyperVGroupManager.Core.Models;
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

        public MainWindow(MainViewModel viewModel, IHyperVGroupService hyperVGroupService, EmailReportService emailReportService)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _hyperVGroupService = hyperVGroupService;
            _emailReportService = emailReportService;
            DataContext = _viewModel;

            _viewModel.ErrorOccurred += OnViewModelErrorOccurred;
            _viewModel.ChangesApplied += OnViewModelChangesApplied;
        }

        private void OnViewModelErrorOccurred(object? sender, string message) =>
            new MessageDialog("Fehlerdetails", message) { Owner = this }.ShowDialog();

        private void OnViewModelChangesApplied(object? sender, string summary) =>
            new MessageDialog("Ergebnis des Änderungslaufs", summary) { Owner = this }.ShowDialog();

        private void VirtualMachinesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel.SelectedVirtualMachines.Clear();
            foreach (var item in VirtualMachinesGrid.SelectedItems.Cast<VirtualMachineInfo>())
            {
                _viewModel.SelectedVirtualMachines.Add(item);
            }
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
                MessageBox.Show(this, "Bitte zuerst eine Gruppe auswählen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show(this, "Bitte zuerst eine Gruppe auswählen.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
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
            new ClusterConfigDialog(_hyperVGroupService, _viewModel.TargetName ?? "", _emailReportService) { Owner = this }.ShowDialog();
        }

        private async void ExportConfigurationButton_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "JSON-Datei (*.json)|*.json",
                FileName = $"HyperVGroupManager-Export-{DateTime.Now:yyyy-MM-dd}.json",
            };

            if (saveFileDialog.ShowDialog(this) == true)
            {
                await _viewModel.ExportConfigurationCommand.ExecuteAsync(saveFileDialog.FileName);
            }
        }
    }
}

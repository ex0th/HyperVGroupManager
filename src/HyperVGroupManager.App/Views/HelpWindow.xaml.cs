using System.Windows;
using HyperVGroupManager.App.Localization;
using HyperVGroupManager.App.Services;
using Microsoft.Win32;

namespace HyperVGroupManager.App.Views;

public partial class HelpWindow : Window
{
    private readonly SupportPackageService _supportPackageService;
    private readonly string? _connectedTarget;

    public HelpWindow(SupportPackageService supportPackageService, string? connectedTarget)
    {
        ArgumentNullException.ThrowIfNull(supportPackageService);
        _supportPackageService = supportPackageService;
        _connectedTarget = connectedTarget;
        InitializeComponent();
    }

    private static string L(string key, params object?[] arguments) =>
        arguments.Length == 0
            ? LocalizationService.Instance.Get(key)
            : LocalizationService.Instance.Format(key, arguments);

    private async void CreateSupportPackageButton_Click(object sender, RoutedEventArgs e)
    {
        var saveFileDialog = new SaveFileDialog
        {
            Filter = L("Dialog.ZipFilter"),
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"HyperVGroupManager-Support-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip",
        };

        if (saveFileDialog.ShowDialog(this) != true)
        {
            return;
        }

        CreateSupportPackageButton.IsEnabled = false;
        try
        {
            var result = await _supportPackageService.CreateAsync(
                saveFileDialog.FileName,
                _connectedTarget);
            MessageBox.Show(
                this,
                L("Support.PackageCreated", result.FilePath, result.IncludedLogFiles),
                L("Support.PackageTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                L("Support.PackageFailed", ex.Message),
                L("Support.PackageTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            CreateSupportPackageButton.IsEnabled = true;
        }
    }
}

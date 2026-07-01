using System.Windows;
using System.Windows.Media;
using HyperVGroupManager.Core.Interfaces;
using HyperVGroupManager.Core.Models;

namespace HyperVGroupManager.App.Views;

public partial class ClusterConfigDialog : Window
{
    private readonly IHyperVGroupService _service;
    private readonly string _targetName;
    private bool _isCluster;

    public ClusterConfigDialog(IHyperVGroupService service, string targetName)
    {
        InitializeComponent();
        _service = service;
        _targetName = targetName;
        Loaded += async (_, _) => await LoadConfigAsync();
    }

    private async Task LoadConfigAsync()
    {
        OkButton.IsEnabled = false;
        CurrentPathTextBlock.Text = "Wird geladen...";

        try
        {
            var config = await _service.GetClusterConfigAsync(_targetName, CancellationToken.None);
            ApplyConfig(config);
        }
        catch (Exception ex)
        {
            CurrentPathTextBlock.Text = $"Fehler: {ex.Message}";
            SetInfoBox(false, ex.Message);
        }
    }

    private void ApplyConfig(ClusterConfigInfo config)
    {
        _isCluster = config.IsCluster;

        CurrentPathTextBlock.Text = !string.IsNullOrEmpty(config.ConfigStoreRootPath)
            ? config.ConfigStoreRootPath
            : "(nicht gesetzt)";
        CurrentPathTextBlock.Foreground = !string.IsNullOrEmpty(config.ConfigStoreRootPath)
            ? Brushes.Black
            : Brushes.Gray;

        if (config.IsCluster)
        {
            PathTextBox.Text = config.ConfigStoreRootPath ?? string.Empty;
            OkButton.IsEnabled = true;
            SetInfoBox(true,
                "Dieser Pfad legt fest, wo Hyper-V die VM-Gruppen-Konfigurationsdateien des Clusters ablegt. " +
                "Eine Änderung wirkt sich auf alle Cluster-Knoten aus. " +
                "Stellen Sie sicher, dass der Pfad auf einem freigegebenen Cluster-Speicher liegt.");
        }
        else
        {
            PathTextBox.IsEnabled = false;
            OkButton.IsEnabled = false;
            SetInfoBox(false, config.Message ?? "Dieser Host ist kein Cluster-Knoten. ConfigStoreRootPath ist nur für Hyper-V-Failovercluster verfügbar.");
        }
    }

    private void SetInfoBox(bool isWarning, string text)
    {
        InfoTextBlock.Text = text;
        if (isWarning)
        {
            InfoBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xCD));
            InfoBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
            InfoTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x7B, 0x53, 0x00));
        }
        else
        {
            InfoBorder.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFD));
            InfoBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9));
            InfoTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x0D, 0x47, 0xA1));
        }
    }

    private async void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isCluster) return;

        var newPath = PathTextBox.Text.Trim();
        if (string.IsNullOrEmpty(newPath))
        {
            MessageBox.Show(this, "Bitte einen Pfad eingeben.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OkButton.IsEnabled = false;

        try
        {
            await _service.SetConfigStoreRootPathAsync(_targetName, newPath, CancellationToken.None);
            CurrentPathTextBlock.Text = newPath;
            CurrentPathTextBlock.Foreground = Brushes.Black;
            MessageBox.Show(this, $"ConfigStoreRootPath wurde erfolgreich auf\n\n{newPath}\n\ngesetzt.", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Fehler beim Setzen des Pfads:\n\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            OkButton.IsEnabled = true;
        }
    }
}

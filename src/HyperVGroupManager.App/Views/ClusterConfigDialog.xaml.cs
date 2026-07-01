using System.Text;
using System.Windows;
using System.Windows.Media;
using HyperVGroupManager.App.Services;
using HyperVGroupManager.Core.Interfaces;
using HyperVGroupManager.Core.Models;

namespace HyperVGroupManager.App.Views;

public partial class ClusterConfigDialog : Window
{
    // ── Cluster tab
    private readonly IHyperVGroupService _service;
    private readonly string _targetName;
    private bool _isCluster;

    // ── Email tabs
    private readonly EmailReportService _emailService;
    private bool _isBusy;

    public ClusterConfigDialog(IHyperVGroupService service, string targetName, EmailReportService emailService)
    {
        InitializeComponent();
        _service = service;
        _targetName = targetName;
        _emailService = emailService;
        Loaded += async (_, _) =>
        {
            await LoadClusterConfigAsync();
            await InitEmailAsync();
        };
    }

    // ── Cluster tab ──────────────────────────────────────────────────────────

    private async Task LoadClusterConfigAsync()
    {
        if (string.IsNullOrWhiteSpace(_targetName))
        {
            CurrentPathTextBlock.Text = "(kein Host verbunden)";
            CurrentPathTextBlock.Foreground = Brushes.Gray;
            SetInfoBox(false, "Bitte zuerst einen Host/Cluster in der Hauptansicht verbinden, um die Cluster-Einstellungen zu laden.");
            OkButton.IsEnabled = false;
            return;
        }

        OkButton.IsEnabled = false;
        CurrentPathTextBlock.Text = "Wird geladen...";
        try
        {
            var config = await _service.GetClusterConfigAsync(_targetName, CancellationToken.None);
            ApplyClusterConfig(config);
        }
        catch (Exception ex)
        {
            CurrentPathTextBlock.Text = $"Fehler: {ex.Message}";
            SetInfoBox(false, ex.Message);
        }
    }

    private void ApplyClusterConfig(ClusterConfigInfo config)
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
            InfoBorder.Background   = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xCD));
            InfoBorder.BorderBrush  = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
            InfoTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x7B, 0x53, 0x00));
        }
        else
        {
            InfoBorder.Background   = new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFD));
            InfoBorder.BorderBrush  = new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9));
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

    // ── Email tabs ───────────────────────────────────────────────────────────

    private async Task InitEmailAsync()
    {
        var config = _emailService.LoadConfig();
        if (string.IsNullOrWhiteSpace(config.TargetName) && !string.IsNullOrWhiteSpace(_targetName))
            config.TargetName = _targetName;
        PopulateControls(config);
        await RefreshTaskStatusAsync(config.TaskName);
    }

    private void PopulateControls(EmailReportConfig config)
    {
        SmtpHostTextBox.Text = config.SmtpHost;
        SmtpPortTextBox.Text = config.SmtpPort.ToString();

        SecurityNoneRadio.IsChecked     = config.SmtpSecurity == "None";
        SecurityStartTlsRadio.IsChecked = config.SmtpSecurity is "STARTTLS" or "";
        SecuritySslRadio.IsChecked      = config.SmtpSecurity == "SSL";

        UseAuthCheckBox.IsChecked = config.UseAuthentication;
        UsernameTextBox.Text      = config.Username;
        PasswordBox.Password      = config.Password;

        SenderAddressTextBox.Text     = config.SenderAddress;
        SenderDisplayNameTextBox.Text = config.SenderDisplayName;
        RecipientsTextBox.Text        = string.Join(Environment.NewLine, config.RecipientAddresses);
        BodyPrefixTextBox.Text        = config.BodyPrefix;

        TargetNameTextBox.Text   = config.TargetName;
        ScheduleTimeTextBox.Text = config.ScheduleTime;
        TaskNameTextBlock.Text   = config.TaskName;

        UpdateAuthFieldsState();
    }

    private EmailReportConfig ReadControls() => new()
    {
        SmtpHost = SmtpHostTextBox.Text.Trim(),
        SmtpPort = int.TryParse(SmtpPortTextBox.Text, out var port) ? port : 587,
        SmtpSecurity = SecuritySslRadio.IsChecked == true ? "SSL"
                     : SecurityNoneRadio.IsChecked == true ? "None"
                     : "STARTTLS",
        UseAuthentication = UseAuthCheckBox.IsChecked == true,
        Username = UsernameTextBox.Text.Trim(),
        Password = PasswordBox.Password,

        SenderAddress     = SenderAddressTextBox.Text.Trim(),
        SenderDisplayName = SenderDisplayNameTextBox.Text.Trim(),
        RecipientAddresses = RecipientsTextBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList(),
        BodyPrefix = BodyPrefixTextBox.Text,

        TargetName   = TargetNameTextBox.Text.Trim(),
        ScheduleTime = ScheduleTimeTextBox.Text.Trim(),
        TaskName     = "HyperVGroupManager_UntaggedVMsReport",
    };

    private void UseAuthCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateAuthFieldsState();

    private void UpdateAuthFieldsState()
    {
        var enabled = UseAuthCheckBox.IsChecked == true;
        UsernameTextBox.IsEnabled = enabled;
        PasswordBox.IsEnabled     = enabled;
        UsernameLabel.IsEnabled   = enabled;
        PasswordLabel.IsEnabled   = enabled;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _emailService.SaveConfig(ReadControls());
        MessageBox.Show(this, "Einstellungen wurden gespeichert.", "Gespeichert",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void SendNowButton_Click(object sender, RoutedEventArgs e)
    {
        var config = ReadControls();

        if (string.IsNullOrWhiteSpace(config.SmtpHost))
        {
            MessageBox.Show(this, "Bitte einen SMTP-Server angeben (Reiter 'SMTP-Server').",
                "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(config.SenderAddress))
        {
            MessageBox.Show(this, "Bitte eine Absender-Adresse angeben (Reiter 'E-Mail-Inhalt').",
                "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (config.RecipientAddresses.Count == 0)
        {
            MessageBox.Show(this, "Bitte mindestens eine Empfänger-Adresse angeben (Reiter 'E-Mail-Inhalt').",
                "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(config.TargetName))
        {
            MessageBox.Show(this, "Bitte einen Ziel-Host/Cluster angeben (Reiter 'Aufgabenplanung').",
                "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            var (success, message) = await _emailService.SendReportNowAsync(config, CancellationToken.None);
            MessageBox.Show(this, message, success ? "Erfolg" : "Fehler", MessageBoxButton.OK,
                success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private async void RegisterTaskButton_Click(object sender, RoutedEventArgs e)
    {
        var config = ReadControls();

        if (string.IsNullOrWhiteSpace(config.TargetName))
        {
            MessageBox.Show(this, "Bitte einen Ziel-Host/Cluster angeben.", "Hinweis",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(config.ScheduleTime, @"^\d{1,2}:\d{2}$"))
        {
            MessageBox.Show(this, "Bitte eine gültige Uhrzeit im Format HH:mm angeben (z.B. 08:00).",
                "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            var (success, message) = await _emailService.RegisterScheduledTaskAsync(config, CancellationToken.None);
            MessageBox.Show(this, message, success ? "Aufgabe registriert" : "Fehler", MessageBoxButton.OK,
                success ? MessageBoxImage.Information : MessageBoxImage.Error);
            if (success) await RefreshTaskStatusAsync(config.TaskName);
        }
        finally { SetBusy(false); }
    }

    private async void UnregisterTaskButton_Click(object sender, RoutedEventArgs e)
    {
        var config = ReadControls();
        SetBusy(true);
        try
        {
            var (success, message) = await _emailService.UnregisterScheduledTaskAsync(config.TaskName, CancellationToken.None);
            MessageBox.Show(this, message, success ? "Aufgabe entfernt" : "Fehler", MessageBoxButton.OK,
                success ? MessageBoxImage.Information : MessageBoxImage.Error);
            if (success) await RefreshTaskStatusAsync(config.TaskName);
        }
        finally { SetBusy(false); }
    }

    private async void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshTaskStatusAsync(ReadControls().TaskName);
    }

    private async Task RefreshTaskStatusAsync(string taskName)
    {
        SetStatusBox(null, "Status wird abgerufen...");
        try
        {
            var status = await _emailService.GetTaskStatusAsync(taskName, CancellationToken.None);

            if (status is null)
            {
                SetStatusBox(false, "Status konnte nicht abgerufen werden.");
                return;
            }
            if (!status.TaskExists)
            {
                SetStatusBox(false, "Keine Aufgabe registriert.");
                return;
            }

            var sb = new StringBuilder();
            sb.Append($"Status: {status.State}");
            if (status.NextRunTime is not null) sb.Append($"\nNächste Ausführung: {status.NextRunTime}");
            if (status.LastRunTime is not null) sb.Append($"\nLetzte Ausführung: {status.LastRunTime}");
            if (status.LastRunResult is not null) sb.Append($"\nLetztes Ergebnis (HRESULT): {status.LastRunResult}");

            SetStatusBox(true, sb.ToString());
        }
        catch (Exception ex)
        {
            SetStatusBox(false, $"Fehler beim Abrufen des Status: {ex.Message}");
        }
    }

    private void SetStatusBox(bool? active, string text)
    {
        TaskStatusTextBlock.Text = text;
        if (active == true)
        {
            StatusBorder.Background        = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
            StatusBorder.BorderBrush       = new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xA7));
            TaskStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));
        }
        else
        {
            StatusBorder.Background        = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
            StatusBorder.BorderBrush       = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            TaskStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        SendNowButton.IsEnabled        = !busy;
        SaveButton.IsEnabled           = !busy;
        RegisterTaskButton.IsEnabled   = !busy;
        UnregisterTaskButton.IsEnabled = !busy;
        RefreshStatusButton.IsEnabled  = !busy;
    }
}

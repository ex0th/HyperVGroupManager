using System.Text;
using System.Windows;
using System.Windows.Media;
using HyperVGroupManager.App.Services;

namespace HyperVGroupManager.App.Views;

public partial class EmailReportConfigDialog : Window
{
    private readonly EmailReportService _emailService;
    private readonly string? _targetNameHint;
    private bool _isBusy;

    public EmailReportConfigDialog(EmailReportService emailService, string? targetNameHint = null)
    {
        InitializeComponent();
        _emailService = emailService;
        _targetNameHint = targetNameHint;
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        var config = _emailService.LoadConfig();

        if (string.IsNullOrWhiteSpace(config.TargetName) && !string.IsNullOrWhiteSpace(_targetNameHint))
            config.TargetName = _targetNameHint;

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

        TargetNameTextBox.Text  = config.TargetName;
        ScheduleTimeTextBox.Text = config.ScheduleTime;
        TaskNameTextBlock.Text  = config.TaskName;

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
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
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
            StatusBorder.Background   = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
            StatusBorder.BorderBrush  = new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xA7));
            TaskStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));
        }
        else
        {
            StatusBorder.Background   = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
            StatusBorder.BorderBrush  = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            TaskStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        SendNowButton.IsEnabled       = !busy;
        SaveButton.IsEnabled          = !busy;
        RegisterTaskButton.IsEnabled  = !busy;
        UnregisterTaskButton.IsEnabled = !busy;
        RefreshStatusButton.IsEnabled = !busy;
    }
}

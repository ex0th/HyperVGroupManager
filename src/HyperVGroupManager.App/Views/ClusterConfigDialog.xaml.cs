using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using HyperVGroupManager.App.Localization;
using HyperVGroupManager.App.Services;
using HyperVGroupManager.Core.Interfaces;
using HyperVGroupManager.Core.Models;

namespace HyperVGroupManager.App.Views;

public partial class ClusterConfigDialog : Window
{
    private static string L(string key, params object?[] arguments) =>
        arguments.Length == 0
            ? LocalizationService.Instance.Get(key)
            : LocalizationService.Instance.Format(key, arguments);

    // ── Cluster tab
    private readonly IHyperVGroupService _service;
    private readonly string _targetName;
    private bool _isCluster;
    private string? _currentConfigStoreRootPath;

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
            CurrentPathTextBlock.Text = L("Settings.NoHost");
            CurrentPathTextBlock.Foreground = SystemColors.GrayTextBrush;
            SetInfoBox(false, L("Settings.ConnectForCluster"));
            OkButton.IsEnabled = false;
            return;
        }

        OkButton.IsEnabled = false;
        CurrentPathTextBlock.Text = L("Settings.Loading");
        try
        {
            var config = await _service.GetClusterConfigAsync(_targetName, CancellationToken.None);
            ApplyClusterConfig(config);
        }
        catch (Exception ex)
        {
            CurrentPathTextBlock.Text = L("Settings.ErrorValue", ex.Message);
            SetInfoBox(false, ex.Message);
        }
    }

    private void ApplyClusterConfig(ClusterConfigInfo config)
    {
        _isCluster = config.IsCluster;
        _currentConfigStoreRootPath = config.ConfigStoreRootPath;
        CurrentPathTextBlock.Text = !string.IsNullOrEmpty(config.ConfigStoreRootPath)
            ? config.ConfigStoreRootPath
            : L("Settings.NotSet");
        CurrentPathTextBlock.Foreground = !string.IsNullOrEmpty(config.ConfigStoreRootPath)
            ? SystemColors.WindowTextBrush
            : SystemColors.GrayTextBrush;

        if (config.IsCluster)
        {
            PathTextBox.Text = config.ConfigStoreRootPath ?? string.Empty;
            OkButton.IsEnabled = true;
            SetInfoBox(true, L("Settings.ClusterPathInfo"));
        }
        else
        {
            PathTextBox.IsEnabled = false;
            OkButton.IsEnabled = false;
            SetInfoBox(false, config.Message ?? L("Settings.NotCluster"));
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
            MessageBox.Show(this, L("Settings.EnterPath"), L("Dialog.Information"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (newPath.Length > 1024 || newPath.Any(char.IsControl) || !System.IO.Path.IsPathFullyQualified(newPath))
        {
            MessageBox.Show(this, L("Settings.InvalidPath"), L("Settings.InvalidPathTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.Equals(newPath.TrimEnd('\\'), _currentConfigStoreRootPath?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, L("Settings.PathUnchanged"), L("Settings.NoChange"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmation = MessageBox.Show(this,
            L("Settings.PathConfirm", _targetName, newPath),
            L("Settings.PathConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        OkButton.IsEnabled = false;
        try
        {
            await _service.SetConfigStoreRootPathAsync(_targetName, newPath, CancellationToken.None);
            _currentConfigStoreRootPath = newPath;
            CurrentPathTextBlock.Text = newPath;
            CurrentPathTextBlock.Foreground = SystemColors.WindowTextBrush;
            MessageBox.Show(this, L("Settings.PathSuccess", newPath), L("Settings.Success"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Settings.PathFailed", ex.Message), L("Settings.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
        try
        {
            _emailService.SaveConfig(ReadControls());
            MessageBox.Show(this, L("Settings.Saved"), L("Settings.SavedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            MessageBox.Show(this, L("Settings.SaveFailed", ex.Message), L("Settings.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SendNowButton_Click(object sender, RoutedEventArgs e)
    {
        var config = ReadControls();

        if (string.IsNullOrWhiteSpace(config.SmtpHost))
        {
            MessageBox.Show(this, L("Settings.EnterSmtp"), L("Dialog.Information"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(config.SenderAddress))
        {
            MessageBox.Show(this, L("Settings.EnterSender"), L("Dialog.Information"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (config.RecipientAddresses.Count == 0)
        {
            MessageBox.Show(this, L("Settings.EnterRecipient"), L("Dialog.Information"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(config.TargetName))
        {
            MessageBox.Show(this, L("Settings.EnterTargetTab"), L("Dialog.Information"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            var (success, message) = await _emailService.SendReportNowAsync(config, CancellationToken.None);
            MessageBox.Show(this, message, success ? L("Settings.Success") : L("Settings.Error"), MessageBoxButton.OK,
                success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Settings.SendFailed", ex.Message), L("Settings.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private async void RegisterTaskButton_Click(object sender, RoutedEventArgs e)
    {
        var config = ReadControls();

        if (string.IsNullOrWhiteSpace(config.TargetName))
        {
            MessageBox.Show(this, L("Settings.EnterTargetShort"), L("Dialog.Information"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(config.ScheduleTime, @"^\d{1,2}:\d{2}$"))
        {
            MessageBox.Show(this, L("Settings.InvalidTime"), L("Dialog.Information"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            var (success, message) = await _emailService.RegisterScheduledTaskAsync(config, CancellationToken.None);
            MessageBox.Show(this, message, success ? L("Settings.TaskRegistered") : L("Settings.Error"), MessageBoxButton.OK,
                success ? MessageBoxImage.Information : MessageBoxImage.Error);
            if (success) await RefreshTaskStatusAsync(config.TaskName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Settings.TaskRegisterFailed", ex.Message), L("Settings.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(this, message, success ? L("Settings.TaskRemoved") : L("Settings.Error"), MessageBoxButton.OK,
                success ? MessageBoxImage.Information : MessageBoxImage.Error);
            if (success) await RefreshTaskStatusAsync(config.TaskName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L("Settings.TaskRemoveFailed", ex.Message), L("Settings.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private async void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshTaskStatusAsync(ReadControls().TaskName);
    }

    private async Task RefreshTaskStatusAsync(string taskName)
    {
        SetStatusBox(null, L("Settings.StatusLoading"));
        try
        {
            var status = await _emailService.GetTaskStatusAsync(taskName, CancellationToken.None);

            if (status is null)
            {
                SetStatusBox(false, L("Settings.StatusUnavailable"));
                return;
            }
            if (!status.TaskExists)
            {
                SetStatusBox(false, L("Settings.NoTask"));
                return;
            }

            var sb = new StringBuilder();
            sb.Append(L("Settings.StatusState", status.State));
            if (status.NextRunTime is not null) sb.Append($"\n{L("Settings.NextRun", status.NextRunTime)}");
            if (status.LastRunTime is not null) sb.Append($"\n{L("Settings.LastRun", status.LastRunTime)}");
            if (status.LastRunResult is not null) sb.Append($"\n{L("Settings.LastResult", status.LastRunResult)}");

            SetStatusBox(true, sb.ToString());
        }
        catch (Exception ex)
        {
            SetStatusBox(false, L("Settings.StatusFailed", ex.Message));
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

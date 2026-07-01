using System.IO;
using System.Text.Json;
using HyperVGroupManager.Core.Interfaces;

namespace HyperVGroupManager.App.Services;

public sealed class EmailReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HyperVGroupManager");

    private static readonly string ConfigFilePath = Path.Combine(ConfigDir, "email-report-config.json");

    private readonly IPowerShellExecutor _executor;

    public EmailReportService(IPowerShellExecutor executor) => _executor = executor;

    public EmailReportConfig LoadConfig()
    {
        if (!File.Exists(ConfigFilePath))
            return new EmailReportConfig();

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<EmailReportConfig>(json, JsonOptions) ?? new EmailReportConfig();
        }
        catch
        {
            return new EmailReportConfig();
        }
    }

    public void SaveConfig(EmailReportConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(config, JsonOptions));
    }

    public async Task<(bool Success, string Message)> SendReportNowAsync(
        EmailReportConfig config, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<string>(
            "Send-HVGMUntaggedVMsReport",
            BuildSendParams(config),
            cancellationToken);

        return result.Success
            ? (true, result.Data ?? "E-Mail-Bericht wurde erfolgreich gesendet.")
            : (false, string.Join("\n", result.Errors ?? Array.Empty<string>()));
    }

    public async Task<(bool Success, string Message)> RegisterScheduledTaskAsync(
        EmailReportConfig config, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<string>(
            "Register-HVGMEmailReportTask",
            BuildRegisterParams(config),
            cancellationToken);

        return result.Success
            ? (true, result.Data ?? "Aufgabe wurde erfolgreich registriert.")
            : (false, string.Join("\n", result.Errors ?? Array.Empty<string>()));
    }

    public async Task<(bool Success, string Message)> UnregisterScheduledTaskAsync(
        string taskName, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<string>(
            "Unregister-HVGMEmailReportTask",
            new { TaskName = taskName },
            cancellationToken);

        return result.Success
            ? (true, result.Data ?? "Aufgabe wurde erfolgreich entfernt.")
            : (false, string.Join("\n", result.Errors ?? Array.Empty<string>()));
    }

    public async Task<EmailTaskStatus?> GetTaskStatusAsync(
        string taskName, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync<EmailTaskStatus>(
            "Get-HVGMEmailReportTaskStatus",
            new { TaskName = taskName },
            cancellationToken);

        return result.Success ? result.Data : null;
    }

    private static object BuildSendParams(EmailReportConfig c) => new
    {
        TargetName        = c.TargetName,
        SmtpHost          = c.SmtpHost,
        SmtpPort          = c.SmtpPort,
        SmtpSecurity      = c.SmtpSecurity,
        UseAuthentication = c.UseAuthentication,
        Username          = c.Username,
        Password          = c.Password,
        SenderAddress     = c.SenderAddress,
        SenderDisplayName = c.SenderDisplayName,
        RecipientAddresses = c.RecipientAddresses,
        BodyPrefix        = c.BodyPrefix,
    };

    private static object BuildRegisterParams(EmailReportConfig c) => new
    {
        AppDir            = AppContext.BaseDirectory,
        TaskName          = c.TaskName,
        TriggerTime       = c.ScheduleTime,
        TargetName        = c.TargetName,
        SmtpHost          = c.SmtpHost,
        SmtpPort          = c.SmtpPort,
        SmtpSecurity      = c.SmtpSecurity,
        UseAuthentication = c.UseAuthentication,
        Username          = c.Username,
        Password          = c.Password,
        SenderAddress     = c.SenderAddress,
        SenderDisplayName = c.SenderDisplayName,
        RecipientAddresses = c.RecipientAddresses,
        BodyPrefix        = c.BodyPrefix,
    };
}

public record EmailTaskStatus
{
    public bool TaskExists { get; init; }
    public string State { get; init; } = "";
    public string? NextRunTime { get; init; }
    public string? LastRunTime { get; init; }
    public string? LastRunResult { get; init; }
}

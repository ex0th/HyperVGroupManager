using System.IO;
using System.Security.Cryptography;
using System.Text;
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
    private static readonly byte[] PasswordEntropy = Encoding.UTF8.GetBytes("HyperVGroupManager.EmailReport.v1");

    private readonly IPowerShellExecutor _executor;

    public EmailReportService(IPowerShellExecutor executor) => _executor = executor;

    public EmailReportConfig LoadConfig()
    {
        if (!File.Exists(ConfigFilePath))
            return new EmailReportConfig();

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            var config = JsonSerializer.Deserialize<EmailReportConfig>(json, JsonOptions) ?? new EmailReportConfig();
            config.Password = UnprotectPassword(config.ProtectedPassword);

            // Migration aus Versionen, die das Kennwort als Klartext gespeichert haben.
            using var document = JsonDocument.Parse(json);
            if (config.Password.Length == 0 &&
                document.RootElement.TryGetProperty("Password", out var oldPasswordProperty) &&
                oldPasswordProperty.ValueKind == JsonValueKind.String)
            {
                config.Password = oldPasswordProperty.GetString() ?? string.Empty;
                SaveConfig(config);
            }

            return config;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException)
        {
            return new EmailReportConfig();
        }
    }

    public void SaveConfig(EmailReportConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Directory.CreateDirectory(ConfigDir);
        config.ProtectedPassword = ProtectPassword(config.Password);
        File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(config, JsonOptions));
    }

    public async Task<(bool Success, string Message)> SendReportNowAsync(
        EmailReportConfig config, CancellationToken cancellationToken)
    {
        var validation = EmailReportConfigValidator.ValidateForSend(config);
        if (!validation.IsValid)
        {
            return (false, string.Join("\n", validation.Errors));
        }

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
        var validation = EmailReportConfigValidator.ValidateForScheduledTask(config);
        if (!validation.IsValid)
        {
            return (false, string.Join("\n", validation.Errors));
        }

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

    internal static string ProtectPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return string.Empty;
        }

        var clearBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return Convert.ToBase64String(ProtectedData.Protect(clearBytes, PasswordEntropy, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    internal static string UnprotectPassword(string protectedPassword)
    {
        if (string.IsNullOrWhiteSpace(protectedPassword))
        {
            return string.Empty;
        }

        var encryptedBytes = Convert.FromBase64String(protectedPassword);
        var clearBytes = ProtectedData.Unprotect(encryptedBytes, PasswordEntropy, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(clearBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
            CryptographicOperations.ZeroMemory(encryptedBytes);
        }
    }
}

public record EmailTaskStatus
{
    public bool TaskExists { get; init; }
    public string State { get; init; } = "";
    public string? NextRunTime { get; init; }
    public string? LastRunTime { get; init; }
    public string? LastRunResult { get; init; }
}

using System.IO;
using System.Text.Json;

namespace HyperVGroupManager.App.Services;

public sealed class AppSettings
{
    public PowerShellOptions PowerShell { get; init; } = new();
    public ApplicationOptions Application { get; init; } = new();
}

/// <summary>
/// Lädt appsettings.json ohne zusätzliches Konfigurations-NuGet-Paket. Fehlt die Datei
/// oder ist sie ungültig, werden sinnvolle Standardwerte verwendet, die Anwendung startet weiter.
/// </summary>
public static class AppSettingsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public static AppSettings Load(string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        var path = Path.Combine(basePath, "appsettings.json");

        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings());
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var powerShell = settings.PowerShell ?? new PowerShellOptions();
        return new AppSettings
        {
            PowerShell = new PowerShellOptions
            {
                ExecutablePath = string.IsNullOrWhiteSpace(powerShell.ExecutablePath)
                    ? "powershell.exe"
                    : powerShell.ExecutablePath.Trim(),
                ExecutionPolicy = NormalizeExecutionPolicy(powerShell.ExecutionPolicy),
                TimeoutSeconds = Math.Clamp(powerShell.TimeoutSeconds, 10, 3600),
            },
            Application = settings.Application ?? new ApplicationOptions(),
        };
    }

    private static string NormalizeExecutionPolicy(string? value)
    {
        var policy = string.IsNullOrWhiteSpace(value) ? "Bypass" : value.Trim();
        return policy is "AllSigned" or "Bypass" or "Default" or "RemoteSigned" or "Restricted" or "Undefined" or "Unrestricted"
            ? policy
            : "Bypass";
    }
}

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
        var path = Path.Combine(basePath, "appsettings.json");

        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }
}

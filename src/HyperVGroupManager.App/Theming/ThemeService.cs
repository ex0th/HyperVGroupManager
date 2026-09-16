using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

#pragma warning disable WPF0001 // .NET 10 native Fluent theme API; intentionally isolated here.

namespace HyperVGroupManager.App.Theming;

/// <summary>
/// Applies the native WPF Fluent theme at application level and persists only the
/// selected mode in the current user's local application data.
/// </summary>
public sealed class ThemeService : INotifyPropertyChanged
{
    private static readonly HashSet<string> SupportedCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "dark", "light",
    };

    private static readonly string PreferenceFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HyperVGroupManager",
        "ui-theme.txt");

    private string _currentThemeCode = "system";

    public static ThemeService Instance { get; } = new();

    public string CurrentThemeCode
    {
        get => _currentThemeCode;
        set => SetTheme(value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private ThemeService()
    {
    }

    public void Initialize()
    {
        var themeCode = "system";
        try
        {
            if (File.Exists(PreferenceFilePath))
            {
                themeCode = NormalizeThemeCode(File.ReadAllText(PreferenceFilePath));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Theme preferences are optional and must never prevent application startup.
        }

        SetTheme(themeCode, persist: false);
    }

    public void SetTheme(string? themeCode, bool persist = true)
    {
        var normalizedCode = NormalizeThemeCode(themeCode);
        var changed = !string.Equals(_currentThemeCode, normalizedCode, StringComparison.OrdinalIgnoreCase);
        _currentThemeCode = normalizedCode;

        if (Application.Current is { } application)
        {
            var themeMode = ResolveThemeMode(normalizedCode);
            application.ThemeMode = themeMode;
            foreach (Window window in application.Windows)
            {
                window.ThemeMode = themeMode;
            }
        }

        if (changed)
        {
            OnPropertyChanged(nameof(CurrentThemeCode));
        }

        if (persist)
        {
            TrySavePreference(normalizedCode);
        }
    }

    internal static string NormalizeThemeCode(string? themeCode)
    {
        var normalizedCode = themeCode?.Trim().ToLowerInvariant();
        return normalizedCode is not null && SupportedCodes.Contains(normalizedCode)
            ? normalizedCode
            : "system";
    }

    internal static ThemeMode ResolveThemeMode(string themeCode) => NormalizeThemeCode(themeCode) switch
    {
        "dark" => ThemeMode.Dark,
        "light" => ThemeMode.Light,
        _ => ThemeMode.System,
    };

    private static void TrySavePreference(string themeCode)
    {
        try
        {
            var directory = Path.GetDirectoryName(PreferenceFilePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(PreferenceFilePath, themeCode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Runtime switching remains valid when the preference cannot be persisted.
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

#pragma warning restore WPF0001

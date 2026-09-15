using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HyperVGroupManager.App.Localization;

public sealed record LanguageOption(string Code, string DisplayName);

/// <summary>
/// Lädt eingebettete JSON-Sprachressourcen, aktualisiert alle Loc-Bindings zur Laufzeit
/// und speichert ausschließlich den gewählten Sprachcode im Benutzerprofil.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly HashSet<string> SupportedCodes = new(StringComparer.OrdinalIgnoreCase) { "de", "en" };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string PreferenceFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HyperVGroupManager",
        "ui-language.txt");

    private IReadOnlyDictionary<string, string> _translations = new Dictionary<string, string>();
    private string _currentLanguageCode = "de";

    public static LocalizationService Instance { get; } = new();

    public IReadOnlyList<LanguageOption> SupportedLanguages { get; } = new[]
    {
        new LanguageOption("de", "Deutsch"),
        new LanguageOption("en", "English"),
    };

    public string CurrentLanguageCode
    {
        get => _currentLanguageCode;
        set => SetLanguage(value);
    }

    public string this[string key] => Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    private LocalizationService()
    {
        LoadLanguage("de");
    }

    public void Initialize()
    {
        var systemLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var language = SupportedCodes.Contains(systemLanguage) ? systemLanguage : "de";

        try
        {
            if (File.Exists(PreferenceFilePath))
            {
                var savedLanguage = File.ReadAllText(PreferenceFilePath).Trim();
                if (SupportedCodes.Contains(savedLanguage))
                {
                    language = savedLanguage;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Die Sprachauswahl ist Komfortzustand; ein Lesefehler darf den Start nicht blockieren.
        }

        SetLanguage(language, persist: false);
    }

    public void SetLanguage(string? languageCode, bool persist = true)
    {
        var normalizedCode = languageCode?.Trim().ToLowerInvariant();
        if (normalizedCode is null || !SupportedCodes.Contains(normalizedCode))
        {
            normalizedCode = "de";
        }

        var languageChanged = !string.Equals(
            _currentLanguageCode,
            normalizedCode,
            StringComparison.OrdinalIgnoreCase);
        if (languageChanged || _translations.Count == 0)
        {
            LoadLanguage(normalizedCode);
        }

        _currentLanguageCode = normalizedCode;
        var culture = CultureInfo.GetCultureInfo(normalizedCode);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (languageChanged)
        {
            OnPropertyChanged(nameof(CurrentLanguageCode));
            OnPropertyChanged("Item[]");
        }

        if (persist)
        {
            TrySavePreference(normalizedCode);
        }
    }

    public string Get(string key)
    {
        if (_translations.TryGetValue(key, out var value))
        {
            return value;
        }

        return $"[{key}]";
    }

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    private void LoadLanguage(string languageCode)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"HyperVGroupManager.App.Localization.strings.{languageCode}.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded language resource '{resourceName}' was not found.");
        _translations = JsonSerializer.Deserialize<Dictionary<string, string>>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Embedded language resource '{resourceName}' is empty.");
    }

    private static void TrySavePreference(string languageCode)
    {
        try
        {
            var directory = Path.GetDirectoryName(PreferenceFilePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(PreferenceFilePath, languageCode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Die laufende Sprachumschaltung bleibt auch ohne persistierte Auswahl gültig.
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

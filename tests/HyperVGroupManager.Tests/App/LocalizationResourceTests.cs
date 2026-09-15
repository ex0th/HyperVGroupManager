using System.Reflection;
using System.Text.Json;
using HyperVGroupManager.App.Localization;

namespace HyperVGroupManager.Tests.App;

public sealed class LocalizationResourceTests
{
    [Fact]
    public void GermanAndEnglishResources_HaveIdenticalNonEmptyKeys()
    {
        var assembly = typeof(LocalizationService).Assembly;
        var german = ReadResource(assembly, "HyperVGroupManager.App.Localization.strings.de.json");
        var english = ReadResource(assembly, "HyperVGroupManager.App.Localization.strings.en.json");

        Assert.NotEmpty(german);
        Assert.Equal(german.Keys.Order(), english.Keys.Order());
        Assert.All(german.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.All(english.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.Equal("Hilfe", german["Common.Help"]);
        Assert.Equal("Help", english["Common.Help"]);
    }

    private static Dictionary<string, string> ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
               ?? throw new InvalidOperationException($"Resource '{resourceName}' is empty.");
    }
}

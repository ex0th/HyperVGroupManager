using System.Windows;
using HyperVGroupManager.App.Theming;

#pragma warning disable WPF0001 // Verifies the isolated .NET 10 native Fluent theme mapping.

namespace HyperVGroupManager.Tests.App;

public sealed class ThemeServiceTests
{
    [Theory]
    [InlineData("system", "system")]
    [InlineData(" SYSTEM ", "system")]
    [InlineData("dark", "dark")]
    [InlineData("LIGHT", "light")]
    [InlineData("unsupported", "system")]
    [InlineData(null, "system")]
    public void NormalizeThemeCode_UsesSupportedValuesOrSystemFallback(string? input, string expected) =>
        Assert.Equal(expected, ThemeService.NormalizeThemeCode(input));

    [Fact]
    public void ResolveThemeMode_MapsEverySupportedMode()
    {
        Assert.Equal(ThemeMode.System, ThemeService.ResolveThemeMode("system"));
        Assert.Equal(ThemeMode.Dark, ThemeService.ResolveThemeMode("dark"));
        Assert.Equal(ThemeMode.Light, ThemeService.ResolveThemeMode("light"));
    }
}

#pragma warning restore WPF0001

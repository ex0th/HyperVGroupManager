namespace HyperVGroupManager.Core.Interfaces;

/// <summary>
/// Einfache Logging-Abstraktion, damit ViewModels und Services nicht direkt von
/// Microsoft.Extensions.Logging oder einer konkreten Logdatei abhängen.
/// </summary>
public interface ILogService
{
    void LogInformation(string message);

    void LogWarning(string message);

    void LogError(string message, Exception? exception = null);
}

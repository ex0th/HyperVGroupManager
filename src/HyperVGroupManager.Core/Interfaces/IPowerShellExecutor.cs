using HyperVGroupManager.Core.Results;

namespace HyperVGroupManager.Core.Interfaces;

/// <summary>
/// Führt eine einzelne PowerShell-Funktion in einem externen powershell.exe-Prozess aus
/// und liefert das Ergebnis strukturiert zurück. Diese Abstraktion erlaubt es, das
/// PowerShell-Backend später durch eine andere Implementierung zu ersetzen.
/// </summary>
public interface IPowerShellExecutor
{
    Task<PowerShellResult<T>> ExecuteAsync<T>(string commandName, object? parameters, CancellationToken cancellationToken);

    Task<PowerShellResult<string>> ExecuteRawAsync(string commandName, object? parameters, CancellationToken cancellationToken);
}

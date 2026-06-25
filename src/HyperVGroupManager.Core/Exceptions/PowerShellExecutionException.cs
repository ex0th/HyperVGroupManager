namespace HyperVGroupManager.Core.Exceptions;

/// <summary>
/// Wird ausgelöst, wenn der externe powershell.exe-Prozess fehlschlägt, einen
/// Timeout erreicht oder kein gültiges JSON-Ergebnis liefert.
/// </summary>
public sealed class PowerShellExecutionException : Exception
{
    public PowerShellExecutionException(string message) : base(message) { }

    public PowerShellExecutionException(string message, Exception innerException) : base(message, innerException) { }
}

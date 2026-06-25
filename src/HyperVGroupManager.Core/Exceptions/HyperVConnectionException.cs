namespace HyperVGroupManager.Core.Exceptions;

/// <summary>
/// Wird ausgelöst, wenn ein Hyper-V-Host oder -Cluster nicht erreichbar ist
/// oder die Umgebungsprüfung fehlschlägt.
/// </summary>
public sealed class HyperVConnectionException : Exception
{
    public HyperVConnectionException(string message) : base(message) { }

    public HyperVConnectionException(string message, Exception innerException) : base(message, innerException) { }
}

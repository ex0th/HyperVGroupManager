namespace HyperVGroupManager.Core.Exceptions;

/// <summary>
/// Wird ausgelöst, wenn das Hyper-V- oder FailoverClusters-PowerShell-Modul
/// auf dem Zielsystem fehlt.
/// </summary>
public sealed class HyperVModuleMissingException : Exception
{
    public HyperVModuleMissingException(string message) : base(message) { }

    public HyperVModuleMissingException(string message, Exception innerException) : base(message, innerException) { }
}

namespace HyperVGroupManager.Core.Exceptions;

/// <summary>
/// Wird ausgelöst, wenn eine Gruppenoperation gegen eine fachliche Regel verstößt,
/// z. B. Löschen einer nicht leeren Gruppe oder ein bereits vergebener Gruppenname.
/// </summary>
public sealed class VmGroupOperationException : Exception
{
    public VmGroupOperationException(string message) : base(message) { }

    public VmGroupOperationException(string message, Exception innerException) : base(message, innerException) { }
}

namespace HyperVGroupManager.Core.Results;

/// <summary>
/// Ergebnis der lokalen Vorabprüfung eines Änderungssatzes. Fehler verhindern die
/// Ausführung; Warnungen beschreiben sichere No-op-Operationen oder auffällige Daten.
/// </summary>
public sealed record ChangeSetValidationResult
{
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool IsValid => Errors.Count == 0;
}

namespace HyperVGroupManager.Core.Results;

/// <summary>
/// Strukturiertes Ergebnis eines PowerShell-Aufrufs. C# deserialisiert ausschließlich
/// dieses Vertragsformat, niemals rohe PowerShell-Objekte.
/// </summary>
public sealed record PowerShellResult<T>
{
    public required bool Success { get; init; }
    public T? Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string RawOutput { get; init; } = string.Empty;
    public int ExitCode { get; init; }
}

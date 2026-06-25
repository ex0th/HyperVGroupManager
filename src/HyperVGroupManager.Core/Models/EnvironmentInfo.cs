namespace HyperVGroupManager.Core.Models;

/// <summary>
/// Ergebnis der Umgebungsprüfung (Einzelhost oder Cluster) beim Verbindungsaufbau.
/// </summary>
public sealed record EnvironmentInfo
{
    public required string TargetName { get; init; }

    // "Host" oder "Cluster".
    public required string TargetType { get; init; }

    public bool IsCluster { get; init; }
    public IReadOnlyList<string> Nodes { get; init; } = Array.Empty<string>();
    public required string PowerShellVersion { get; init; }
    public bool HyperVModuleAvailable { get; init; }
    public bool FailoverClustersModuleAvailable { get; init; }
    public bool IsAdministrator { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

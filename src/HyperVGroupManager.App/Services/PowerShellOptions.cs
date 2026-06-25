namespace HyperVGroupManager.App.Services;

public sealed class PowerShellOptions
{
    public string ExecutablePath { get; init; } = "powershell.exe";
    public string ExecutionPolicy { get; init; } = "Bypass";
    public int TimeoutSeconds { get; init; } = 120;
}

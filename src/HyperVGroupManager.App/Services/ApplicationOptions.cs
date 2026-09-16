namespace HyperVGroupManager.App.Services;

public sealed class ApplicationOptions
{
    public string DefaultGroupPrefix { get; init; } = "VEEAM_";
    public bool ConfirmBeforeApply { get; init; } = true;
    public int LogRetentionDays { get; init; } = 30;
    public int MaximumLogFileSizeMegabytes { get; init; } = 10;
}

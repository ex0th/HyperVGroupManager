namespace HyperVGroupManager.App.Services;

public sealed class ApplicationOptions
{
    public string DefaultGroupPrefix { get; init; } = "VEEAM_";
    public bool ConfirmBeforeApply { get; init; } = true;
}

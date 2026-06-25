using HyperVGroupManager.Core.Interfaces;

namespace HyperVGroupManager.Tests.Fakes;

public sealed class FakeLogService : ILogService
{
    public List<string> InformationMessages { get; } = new();
    public List<string> WarningMessages { get; } = new();
    public List<string> ErrorMessages { get; } = new();

    public void LogInformation(string message) => InformationMessages.Add(message);

    public void LogWarning(string message) => WarningMessages.Add(message);

    public void LogError(string message, Exception? exception = null) => ErrorMessages.Add(message);
}

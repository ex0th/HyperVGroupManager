using HyperVGroupManager.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace HyperVGroupManager.App.Services;

/// <summary>
/// Adapter zwischen der projektinternen ILogService-Abstraktion (Core) und
/// Microsoft.Extensions.Logging, damit ViewModels/Services nicht direkt von einem
/// konkreten Logging-Framework abhängen.
/// </summary>
public sealed class LogService : ILogService
{
    private readonly ILogger<LogService> _logger;

    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
    }

    public void LogInformation(string message) => _logger.LogInformation("{Message}", message);

    public void LogWarning(string message) => _logger.LogWarning("{Message}", message);

    public void LogError(string message, Exception? exception = null) => _logger.LogError(exception, "{Message}", message);
}

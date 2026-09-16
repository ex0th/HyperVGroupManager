using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HyperVGroupManager.App.Services;

/// <summary>
/// Writes size-limited daily logs and removes files outside the configured retention period.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const string LogFilePattern = "HyperVGroupManager-*.log";

    private readonly string? _logDirectory;
    private readonly int _retentionDays;
    private readonly long _maximumLogFileBytes;
    private readonly object _writeLock = new();
    private bool _isDisabled;

    public FileLoggerProvider(int retentionDays = 30, int maximumLogFileSizeMegabytes = 10)
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HyperVGroupManager",
                "Logs"),
            retentionDays,
            checked((long)Math.Clamp(maximumLogFileSizeMegabytes, 1, 100) * 1024 * 1024))
    {
    }

    internal FileLoggerProvider(string logDirectory, int retentionDays, long maximumLogFileBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        _retentionDays = Math.Clamp(retentionDays, 1, 365);
        _maximumLogFileBytes = Math.Max(1024, maximumLogFileBytes);

        try
        {
            _logDirectory = logDirectory;
            Directory.CreateDirectory(_logDirectory);
            DeleteExpiredLogs(DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logDirectory = null;
            _isDisabled = true;
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
    }

    internal void WriteLine(string line)
    {
        if (_isDisabled || _logDirectory is null)
        {
            return;
        }

        lock (_writeLock)
        {
            try
            {
                var filePath = GetWritableLogFilePath(DateTime.Now);
                File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _isDisabled = true;
            }
        }
    }

    private string GetWritableLogFilePath(DateTime localNow)
    {
        var baseName = $"HyperVGroupManager-{localNow:yyyy-MM-dd}";
        for (var index = 0; index < 1000; index++)
        {
            var suffix = index == 0 ? string.Empty : $".{index}";
            var candidate = Path.Combine(_logDirectory!, $"{baseName}{suffix}.log");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length < _maximumLogFileBytes)
            {
                return candidate;
            }
        }

        throw new IOException("The daily log rotation limit was exceeded.");
    }

    private void DeleteExpiredLogs(DateTime utcNow)
    {
        var cutoff = utcNow.AddDays(-_retentionDays);
        foreach (var filePath in Directory.EnumerateFiles(_logDirectory!, LogFilePattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(filePath) < cutoff)
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // One locked log file must not disable logging for the current session.
            }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FileLoggerProvider _provider;

        public FileLogger(string categoryName, FileLoggerProvider provider)
        {
            _categoryName = categoryName;
            _provider = provider;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_categoryName}: {message}";

            if (exception is not null)
            {
                line += $" | {exception}";
            }

            _provider.WriteLine(line);
        }
    }
}

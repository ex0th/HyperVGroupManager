using System.IO;
using HyperVGroupManager.App.Services;
using Microsoft.Extensions.Logging;

namespace HyperVGroupManager.Tests.App;

public sealed class FileLoggerProviderTests
{
    [Fact]
    public void Constructor_RemovesExpiredLogs()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var expired = Path.Combine(directory, "HyperVGroupManager-2020-01-01.log");
            var unrelated = Path.Combine(directory, "keep.txt");
            File.WriteAllText(expired, "expired");
            File.WriteAllText(unrelated, "keep");
            File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-31));

            using var provider = new FileLoggerProvider(directory, retentionDays: 30, maximumLogFileBytes: 1024);

            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Logger_RotatesAfterConfiguredSizeLimit()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var provider = new FileLoggerProvider(directory, retentionDays: 30, maximumLogFileBytes: 1024);
            var logger = provider.CreateLogger("test");

            logger.LogInformation("{Message}", new string('x', 1200));
            logger.LogInformation("second entry");

            var files = Directory.GetFiles(directory, "HyperVGroupManager-*.log");
            Assert.Equal(2, files.Length);
            Assert.Contains(files, path => path.EndsWith(".1.log", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hvgm-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

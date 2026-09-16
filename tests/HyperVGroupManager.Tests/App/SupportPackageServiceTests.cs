using System.IO;
using System.IO.Compression;
using HyperVGroupManager.App.Services;

namespace HyperVGroupManager.Tests.App;

public sealed class SupportPackageServiceTests
{
    [Fact]
    public async Task CreateAsync_WritesDiagnosticsAndRedactedLogsWithoutConfiguration()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var applicationData = Path.Combine(root, "app-data");
            var logsDirectory = Path.Combine(applicationData, "Logs");
            Directory.CreateDirectory(logsDirectory);
            var connectedTarget = "hv01.customer.local";
            var logContents = string.Join(
                " | ",
                Environment.UserName,
                Environment.MachineName,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                connectedTarget,
                "admin@example.com",
                "192.168.10.25");
            File.WriteAllText(Path.Combine(logsDirectory, "HyperVGroupManager-2026-09-16.log"), logContents);
            File.WriteAllText(Path.Combine(applicationData, "crash.log"), "crash from " + connectedTarget);
            File.WriteAllText(Path.Combine(applicationData, "email-report.json"), "must-not-be-included");

            var destination = Path.Combine(root, "support.zip");
            var service = new SupportPackageService(applicationData);
            var result = await service.CreateAsync(destination, connectedTarget);

            Assert.Equal(2, result.IncludedLogFiles);
            using var archive = ZipFile.OpenRead(destination);
            Assert.Contains(archive.Entries, entry => entry.FullName == "README.txt");
            Assert.Contains(archive.Entries, entry => entry.FullName == "diagnostics.json");
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("email-report", StringComparison.OrdinalIgnoreCase));

            var logEntry = Assert.Single(
                archive.Entries,
                entry => entry.FullName.EndsWith("HyperVGroupManager-2026-09-16.log", StringComparison.Ordinal));
            using var reader = new StreamReader(logEntry.Open());
            var redactedLog = await reader.ReadToEndAsync();

            Assert.DoesNotContain(connectedTarget, redactedLog, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("admin@example.com", redactedLog, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("192.168.10.25", redactedLog, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<connected-target>", redactedLog);
            Assert.Contains("<redacted-email>", redactedLog);
            Assert.Contains("<ip-address>", redactedLog);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Redact_ReplacesCaseInsensitiveSensitiveValues()
    {
        var result = SupportPackageService.Redact(
            "Target HV01.CUSTOMER.LOCAL contacted User@Example.com from 10.0.0.1",
            "hv01.customer.local");

        Assert.Equal(
            "Target <connected-target> contacted <redacted-email> from <ip-address>",
            result);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hvgm-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

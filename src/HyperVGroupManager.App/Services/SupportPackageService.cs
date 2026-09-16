using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HyperVGroupManager.App.Services;

public sealed record SupportPackageResult(string FilePath, int IncludedLogFiles);

/// <summary>
/// Creates a bounded, redacted diagnostic archive without including application or mail settings.
/// </summary>
public sealed class SupportPackageService
{
    private const int MaximumLogFiles = 14;
    private const long MaximumBytesPerLog = 5 * 1024 * 1024;
    private const long MaximumTotalLogBytes = 20 * 1024 * 1024;

    private static readonly Regex EmailPattern = new(
        @"(?<![\w.+-])[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}(?![\w.-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex IpAddressPattern = new(
        @"(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private readonly string _applicationDataDirectory;

    public SupportPackageService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HyperVGroupManager"))
    {
    }

    internal SupportPackageService(string applicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        _applicationDataDirectory = applicationDataDirectory;
    }

    public async Task<SupportPackageResult> CreateAsync(
        string destinationPath,
        string? connectedTarget,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new IOException("The support package destination has no parent directory.");
        Directory.CreateDirectory(destinationDirectory);

        try
        {
            await using var output = new FileStream(
                fullDestinationPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);

            await WriteEntryAsync(
                archive,
                "README.txt",
                "Hyper-V VM Group Manager support package\r\n\r\n" +
                "Logs are automatically redacted for common user names, computer names, paths, " +
                "email addresses, IP addresses, and the connected target. No application, SMTP, " +
                "or credential configuration is included. Review the archive before sharing it.\r\n",
                cancellationToken).ConfigureAwait(false);

            var diagnostics = BuildDiagnostics();
            await WriteEntryAsync(
                archive,
                "diagnostics.json",
                JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken).ConfigureAwait(false);

            var includedLogFiles = 0;
            var totalLogBytes = 0L;
            foreach (var filePath in GetCandidateLogFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var bytesToRead = Math.Min(fileInfo.Length, MaximumBytesPerLog);
                    if (totalLogBytes + bytesToRead > MaximumTotalLogBytes)
                    {
                        continue;
                    }

                    var contents = await ReadTailAsync(filePath, MaximumBytesPerLog, cancellationToken).ConfigureAwait(false);
                    contents = Redact(contents, connectedTarget);
                    await WriteEntryAsync(
                        archive,
                        $"logs/{Path.GetFileName(filePath)}",
                        contents,
                        cancellationToken).ConfigureAwait(false);
                    totalLogBytes += bytesToRead;
                    includedLogFiles++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
                {
                    // A locked, deleted, or non-redactable log must not block the remaining package.
                }
            }

            return new SupportPackageResult(fullDestinationPath, includedLogFiles);
        }
        catch
        {
            TryDeleteFile(fullDestinationPath);
            throw;
        }
    }

    private object BuildDiagnostics()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SupportPackageService).Assembly;
        var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        var version = assembly.GetName().Version;

        return new
        {
            CreatedUtc = DateTimeOffset.UtcNow,
            ApplicationVersion = version is null ? "unknown" : version.ToString(3),
            FileVersion = string.IsNullOrWhiteSpace(executablePath)
                ? "unknown"
                : FileVersionInfo.GetVersionInfo(executablePath).FileVersion ?? "unknown",
            Signature = GetSignatureStatus(executablePath),
            OperatingSystem = RuntimeInformation.OSDescription,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Framework = RuntimeInformation.FrameworkDescription,
            Culture = CultureInfo.CurrentCulture.Name,
            UiCulture = CultureInfo.CurrentUICulture.Name,
            IsElevated = IsProcessElevated(),
            Distribution = IsInstalledUnderProgramFiles(executablePath) ? "installed" : "portable-or-development",
        };
    }

    private IEnumerable<string> GetCandidateLogFiles()
    {
        var logsDirectory = Path.Combine(_applicationDataDirectory, "Logs");
        var candidates = new List<FileInfo>();
        try
        {
            if (Directory.Exists(logsDirectory))
            {
                candidates.AddRange(Directory.EnumerateFiles(
                        logsDirectory,
                        "HyperVGroupManager-*.log",
                        SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path)));
            }

            var crashLog = Path.Combine(_applicationDataDirectory, "crash.log");
            if (File.Exists(crashLog))
            {
                candidates.Add(new FileInfo(crashLog));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        return candidates
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaximumLogFiles)
            .Select(file => file.FullName);
    }

    private static async Task<string> ReadTailAsync(
        string filePath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            useAsync: true);
        if (stream.Length > maximumBytes)
        {
            stream.Seek(-maximumBytes, SeekOrigin.End);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string entryName,
        string contents,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(contents.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    internal static string Redact(string value, string? connectedTarget = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var result = value;
        result = ReplaceIfPresent(result, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
        result = ReplaceIfPresent(result, AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "%APPDIR%");
        result = ReplaceIfPresent(result, Environment.UserName, "<user>");
        result = ReplaceIfPresent(result, Environment.MachineName, "<local-computer>");
        result = ReplaceIfPresent(result, connectedTarget, "<connected-target>");
        result = EmailPattern.Replace(result, "<redacted-email>");
        result = IpAddressPattern.Replace(result, "<ip-address>");
        return result;
    }

    private static string ReplaceIfPresent(string value, string? sensitiveValue, string replacement) =>
        string.IsNullOrWhiteSpace(sensitiveValue)
            ? value
            : value.Replace(sensitiveValue, replacement, StringComparison.OrdinalIgnoreCase);

    private static string GetSignatureStatus(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return "unknown";
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(executablePath);
            return string.IsNullOrWhiteSpace(certificate.Thumbprint) ? "unsigned" : "signed";
        }
        catch (CryptographicException)
        {
            return "unsigned";
        }
    }

    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsInstalledUnderProgramFiles(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var normalizedRoot = Path.GetFullPath(programFiles).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedExecutable = Path.GetFullPath(executablePath);
        return normalizedExecutable.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup of an incomplete archive.
        }
    }
}

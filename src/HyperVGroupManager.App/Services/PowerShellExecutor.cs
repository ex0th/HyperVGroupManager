using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HyperVGroupManager.Core.Exceptions;
using HyperVGroupManager.Core.Interfaces;
using HyperVGroupManager.Core.Results;

namespace HyperVGroupManager.App.Services;

/// <summary>
/// Führt genau eine bekannte PowerShell-Funktion in einem externen powershell.exe-Prozess aus.
/// Parameter werden als JSON-Datei übergeben, niemals als String-verkettetes PowerShell-Code.
/// </summary>
public sealed class PowerShellExecutor : IPowerShellExecutor
{
    // JsonStringEnumConverter, damit z. B. ChangeApplicationResult.ChangeType (von PowerShell
    // als String wie "AddMembership" geliefert) in das C#-Enum deserialisiert werden kann.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Schützt davor, dass jemals ein beliebiger, nicht im Modul vorhandener Funktionsname
    // an "& $CommandName" im Bootstrap-Skript übergeben wird.
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "Test-HVGMEnvironment",
        "Get-HVGMVirtualMachine",
        "Get-HVGMGroup",
        "New-HVGMGroup",
        "Rename-HVGMGroup",
        "Remove-HVGMGroup",
        "Add-HVGMGroupMember",
        "Remove-HVGMGroupMember",
        "Invoke-HVGMChangeSet",
        "Export-HVGMConfiguration",
    };

    private readonly PowerShellOptions _options;
    private readonly ILogService _logService;
    private readonly string _moduleManifestPath;
    private readonly string _bootstrapScriptPath;

    public PowerShellExecutor(PowerShellOptions options, ILogService logService)
    {
        _options = options;
        _logService = logService;
        _moduleManifestPath = Path.Combine(AppContext.BaseDirectory, "PowerShell", "HyperVGroupManager.psd1");
        _bootstrapScriptPath = Path.Combine(AppContext.BaseDirectory, "PowerShell", "Invoke-HVGMCommand.ps1");
    }

    public async Task<PowerShellResult<T>> ExecuteAsync<T>(string commandName, object? parameters, CancellationToken cancellationToken)
    {
        var rawResult = await ExecuteRawAsync(commandName, parameters, cancellationToken).ConfigureAwait(false);
        return ParseEnvelope<T>(rawResult, commandName, _logService);
    }

    public async Task<PowerShellResult<string>> ExecuteRawAsync(string commandName, object? parameters, CancellationToken cancellationToken)
    {
        if (!AllowedCommands.Contains(commandName))
        {
            throw new PowerShellExecutionException($"Unbekannter PowerShell-Befehl '{commandName}'.");
        }

        var parametersFilePath = Path.Combine(Path.GetTempPath(), $"hvgm-{Guid.NewGuid():N}.json");
        var parametersJson = JsonSerializer.Serialize(parameters ?? new object());
        await File.WriteAllTextAsync(parametersFilePath, parametersJson, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        try
        {
            return await RunProcessAsync(commandName, parametersFilePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(parametersFilePath);
        }
    }

    /// <summary>
    /// Reine JSON-Vertrag-Verarbeitung, getrennt von der Prozessausführung, damit sie ohne
    /// echten powershell.exe-Aufruf unit-testbar ist (internal + InternalsVisibleTo Tests).
    /// </summary>
    internal static PowerShellResult<T> ParseEnvelope<T>(PowerShellResult<string> rawResult, string commandName, ILogService logService)
    {
        if (!rawResult.Success)
        {
            return new PowerShellResult<T>
            {
                Success = false,
                Errors = rawResult.Errors,
                Warnings = rawResult.Warnings,
                RawOutput = rawResult.RawOutput,
                ExitCode = rawResult.ExitCode,
            };
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<PowerShellResult<T>>(rawResult.RawOutput, SerializerOptions);

            if (envelope is null)
            {
                throw new PowerShellExecutionException($"PowerShell-Befehl '{commandName}' lieferte kein gültiges Ergebnis.");
            }

            return envelope with { RawOutput = rawResult.RawOutput, ExitCode = rawResult.ExitCode };
        }
        catch (JsonException ex)
        {
            logService.LogError($"Ungültiges JSON von PowerShell-Befehl '{commandName}'.", ex);
            throw new PowerShellExecutionException(
                $"Die Antwort von PowerShell-Befehl '{commandName}' konnte nicht verarbeitet werden (ungültiges JSON).", ex);
        }
    }

    private async Task<PowerShellResult<string>> RunProcessAsync(string commandName, string parametersFilePath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add(_options.ExecutionPolicy);
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(_bootstrapScriptPath);
        startInfo.ArgumentList.Add("-ModuleManifestPath");
        startInfo.ArgumentList.Add(_moduleManifestPath);
        startInfo.ArgumentList.Add("-CommandName");
        startInfo.ArgumentList.Add(commandName);
        startInfo.ArgumentList.Add("-ParametersFilePath");
        startInfo.ArgumentList.Add(parametersFilePath);

        using var process = new Process { StartInfo = startInfo };

        var stdOutBuilder = new StringBuilder();
        var stdErrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOutBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErrBuilder.AppendLine(e.Data); };

        _logService.LogInformation($"Starte PowerShell-Befehl '{commandName}'.");

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);

            if (cancellationToken.IsCancellationRequested)
            {
                _logService.LogWarning($"PowerShell-Befehl '{commandName}' wurde abgebrochen.");
                throw;
            }

            _logService.LogError($"PowerShell-Befehl '{commandName}' hat das Timeout von {_options.TimeoutSeconds}s überschritten.");
            throw new PowerShellExecutionException(
                $"Der Vorgang '{commandName}' hat das Zeitlimit von {_options.TimeoutSeconds} Sekunden überschritten und wurde abgebrochen.");
        }

        var stdOut = stdOutBuilder.ToString().Trim();
        var stdErr = stdErrBuilder.ToString().Trim();

        _logService.LogInformation($"PowerShell-Befehl '{commandName}' beendet mit Exit-Code {process.ExitCode}.");

        if (!string.IsNullOrWhiteSpace(stdErr))
        {
            _logService.LogWarning($"PowerShell StandardError ('{commandName}'): {stdErr}");
        }

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdOut))
        {
            var message = !string.IsNullOrWhiteSpace(stdErr)
                ? stdErr
                : $"PowerShell-Befehl '{commandName}' wurde mit Exit-Code {process.ExitCode} beendet und lieferte keine Ausgabe.";

            return new PowerShellResult<string>
            {
                Success = false,
                Errors = new[] { message },
                RawOutput = stdOut,
                ExitCode = process.ExitCode,
            };
        }

        return new PowerShellResult<string>
        {
            Success = true,
            Data = stdOut,
            RawOutput = stdOut,
            ExitCode = process.ExitCode,
        };
    }

    private void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Fehler beim Beenden des PowerShell-Prozesses nach Timeout.", ex);
        }
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
        catch (IOException)
        {
            // Best effort - temporäre Datei kann ignoriert werden, falls sie noch gesperrt ist.
        }
    }
}

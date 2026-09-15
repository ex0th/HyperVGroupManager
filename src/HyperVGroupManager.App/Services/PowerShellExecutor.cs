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
    private const int MaximumParameterBytes = 10 * 1024 * 1024;
    private const int MaximumCapturedOutputCharacters = 10 * 1024 * 1024;

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
        "Get-HVGMClusterConfig",
        "Set-HVGMConfigStoreRootPath",
        "Send-HVGMUntaggedVMsReport",
        "Register-HVGMEmailReportTask",
        "Unregister-HVGMEmailReportTask",
        "Get-HVGMEmailReportTaskStatus",
    };

    private readonly PowerShellOptions _options;
    private readonly ILogService _logService;
    private readonly string _moduleManifestPath;
    private readonly string _bootstrapScriptPath;
    private readonly int _timeoutSeconds;

    public PowerShellExecutor(PowerShellOptions options, ILogService logService)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _timeoutSeconds = Math.Clamp(options.TimeoutSeconds, 10, 3600);
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

        if (!File.Exists(_moduleManifestPath))
        {
            throw new PowerShellExecutionException($"Das PowerShell-Modul wurde nicht gefunden: '{_moduleManifestPath}'.");
        }

        if (!File.Exists(_bootstrapScriptPath))
        {
            throw new PowerShellExecutionException($"Das PowerShell-Startskript wurde nicht gefunden: '{_bootstrapScriptPath}'.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var parametersFilePath = Path.Combine(Path.GetTempPath(), $"hvgm-{Guid.NewGuid():N}.json");
        var parametersJson = JsonSerializer.Serialize(parameters ?? new object());
        if (Encoding.UTF8.GetByteCount(parametersJson) > MaximumParameterBytes)
        {
            throw new PowerShellExecutionException("Die PowerShell-Parameter überschreiten das Sicherheitslimit von 10 MB.");
        }

        try
        {
            await File.WriteAllTextAsync(parametersFilePath, parametersJson, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            return await RunProcessAsync(commandName, parametersFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PowerShellExecutionException("Die temporäre PowerShell-Parameterdatei konnte nicht sicher verarbeitet werden.", ex);
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

            return envelope with
            {
                Errors = envelope.Errors ?? Array.Empty<string>(),
                Warnings = envelope.Warnings ?? Array.Empty<string>(),
                RawOutput = rawResult.RawOutput,
                ExitCode = rawResult.ExitCode,
            };
        }
        catch (JsonException ex)
        {
            logService.LogError($"Ungültiges JSON von PowerShell-Befehl '{commandName}'. Raw output: {rawResult.RawOutput}", ex);
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
        var outputWasTruncated = false;

        process.OutputDataReceived += (_, e) => AppendLimited(stdOutBuilder, e.Data, ref outputWasTruncated);
        process.ErrorDataReceived += (_, e) => AppendLimited(stdErrBuilder, e.Data, ref outputWasTruncated);

        _logService.LogInformation($"Starte PowerShell-Befehl '{commandName}'.");

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new PowerShellExecutionException(
                $"PowerShell konnte nicht gestartet werden ('{_options.ExecutablePath}').", ex);
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

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

            _logService.LogError($"PowerShell-Befehl '{commandName}' hat das Timeout von {_timeoutSeconds}s überschritten.");
            throw new PowerShellExecutionException(
                $"Der Vorgang '{commandName}' hat das Zeitlimit von {_timeoutSeconds} Sekunden überschritten und wurde abgebrochen.");
        }

        // Falls PS trotz -Compress mehrere Zeilen schreibt (z.B. weil eine Fehlermeldung
        // einen unescapten Zeilenumbruch enthielt), die letzte nicht-leere JSON-Zeile nehmen.
        var rawLines = stdOutBuilder.ToString()
            .Split('\n')
            .Select(l => l.Trim('\r', ' ', '﻿'))
            .Where(l => l.Length > 0)
            .ToArray();

        var stdOut = rawLines.Length > 1
            ? (rawLines.LastOrDefault(l => l.StartsWith("{") || l.StartsWith("[")) ?? string.Join(string.Empty, rawLines))
            : (rawLines.FirstOrDefault() ?? string.Empty);

        // Manche Cmdlets (insb. Cluster-/WMI-Aufrufe) schreiben Warning-/Verbose-/Progress-Text
        // asynchron auf denselben stdout-Handle und können ihn mitten in die JSON-Zeile mischen.
        // Auf das äußerste {...} bzw. [...] zuschneiden, um führenden/nachgestellten Fremdtext
        // zu entfernen; Text, der mitten in die JSON-Struktur gemischt wurde, bleibt davon
        // unberührt und führt weiterhin zu einem Parse-Fehler (siehe ParseEnvelope).
        stdOut = TrimToJsonEnvelope(stdOut);

        var stdErr = stdErrBuilder.ToString().Trim();

        if (outputWasTruncated)
        {
            _logService.LogError($"PowerShell-Ausgabe von '{commandName}' überschritt das Sicherheitslimit.");
            throw new PowerShellExecutionException(
                $"Die Ausgabe von '{commandName}' überschritt das Sicherheitslimit von 10 MB und wurde verworfen.");
        }

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

    /// <summary>
    /// Schneidet eine Zeile auf den Bereich vom ersten '{' bzw. '[' bis zum letzten passenden
    /// '}' bzw. ']' zu, damit führender/nachgestellter Fremdtext (z. B. eine WARNING-Zeile ohne
    /// eigenen Zeilenumbruch) nicht die JSON-Deserialisierung verhindert.
    /// </summary>
    private static string TrimToJsonEnvelope(string line)
    {
        var start = line.IndexOfAny(new[] { '{', '[' });
        if (start < 0)
        {
            return line;
        }

        var closing = line[start] == '{' ? '}' : ']';
        var end = line.LastIndexOf(closing);
        if (end < start)
        {
            return line;
        }

        return line.Substring(start, end - start + 1);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort - temporäre Datei kann ignoriert werden, falls sie noch gesperrt ist.
        }
    }

    private static void AppendLimited(StringBuilder builder, string? line, ref bool wasTruncated)
    {
        if (line is null || wasTruncated)
        {
            return;
        }

        if (builder.Length + line.Length + Environment.NewLine.Length > MaximumCapturedOutputCharacters)
        {
            wasTruncated = true;
            return;
        }

        builder.AppendLine(line);
    }
}

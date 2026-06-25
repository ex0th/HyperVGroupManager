using HyperVGroupManager.Core.Interfaces;
using HyperVGroupManager.Core.Results;

namespace HyperVGroupManager.Tests.Fakes;

/// <summary>
/// Fake-Implementierung für Tests, damit PowerShell in Unit-Tests nie wirklich ausgeführt wird.
/// </summary>
public sealed class FakePowerShellExecutor : IPowerShellExecutor
{
    private readonly Dictionary<string, object?> _responses = new();

    public List<(string CommandName, object? Parameters)> Calls { get; } = new();

    public void SetResponse<T>(string commandName, PowerShellResult<T> result) => _responses[commandName] = result;

    public Task<PowerShellResult<T>> ExecuteAsync<T>(string commandName, object? parameters, CancellationToken cancellationToken)
    {
        Calls.Add((commandName, parameters));

        if (_responses.TryGetValue(commandName, out var response) && response is PowerShellResult<T> typed)
        {
            return Task.FromResult(typed);
        }

        throw new InvalidOperationException($"Keine Fake-Antwort für '{commandName}' konfiguriert.");
    }

    public Task<PowerShellResult<string>> ExecuteRawAsync(string commandName, object? parameters, CancellationToken cancellationToken)
    {
        Calls.Add((commandName, parameters));
        return Task.FromResult(new PowerShellResult<string> { Success = true, Data = string.Empty });
    }
}

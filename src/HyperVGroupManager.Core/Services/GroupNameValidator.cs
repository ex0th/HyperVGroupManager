namespace HyperVGroupManager.Core.Services;

public sealed record GroupNameValidationResult
{
    public required bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }

    public static GroupNameValidationResult Valid() => new() { IsValid = true };

    public static GroupNameValidationResult Invalid(string errorMessage) =>
        new() { IsValid = false, ErrorMessage = errorMessage };
}

/// <summary>
/// Prüft Gruppennamen gegen die fachlichen Regeln: nicht leer, keine Duplikate.
/// </summary>
public static class GroupNameValidator
{
    public const int MaxLength = 256;

    public static GroupNameValidationResult Validate(string? name, IEnumerable<string> existingGroupNames)
    {
        ArgumentNullException.ThrowIfNull(existingGroupNames);

        if (string.IsNullOrWhiteSpace(name))
        {
            return GroupNameValidationResult.Invalid("Der Gruppenname darf nicht leer sein.");
        }

        var trimmedName = name.Trim();

        if (trimmedName.Length > MaxLength)
        {
            return GroupNameValidationResult.Invalid($"Der Gruppenname darf höchstens {MaxLength} Zeichen lang sein.");
        }

        if (trimmedName.Any(char.IsControl))
        {
            return GroupNameValidationResult.Invalid("Der Gruppenname darf keine Steuerzeichen enthalten.");
        }

        var isDuplicate = existingGroupNames.Any(existing =>
            string.Equals(existing, trimmedName, StringComparison.OrdinalIgnoreCase));

        if (isDuplicate)
        {
            return GroupNameValidationResult.Invalid($"Eine Gruppe mit dem Namen \"{trimmedName}\" existiert bereits.");
        }

        return GroupNameValidationResult.Valid();
    }
}

using System.Globalization;
using System.Net.Mail;

namespace HyperVGroupManager.App.Services;

public sealed record EmailReportValidationResult
{
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Zentrale Eingabeprüfung für Sofortversand und Aufgabenplanung. Dadurch gelten
/// dieselben Regeln unabhängig davon, von welchem Dialog die Services aufgerufen werden.
/// </summary>
public static class EmailReportConfigValidator
{
    public static EmailReportValidationResult ValidateForSend(EmailReportConfig config) =>
        Validate(config, requireSchedule: false);

    public static EmailReportValidationResult ValidateForScheduledTask(EmailReportConfig config) =>
        Validate(config, requireSchedule: true);

    private static EmailReportValidationResult Validate(EmailReportConfig config, bool requireSchedule)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = new List<string>();

        ValidateRequiredText(config.TargetName, "Ziel-Host/Cluster", 255, errors);
        ValidateRequiredText(config.SmtpHost, "SMTP-Server", 255, errors);
        if (config.SmtpPort is < 1 or > 65535)
        {
            errors.Add("Der SMTP-Port muss zwischen 1 und 65535 liegen.");
        }

        if (config.SmtpSecurity is not ("None" or "STARTTLS" or "SSL"))
        {
            errors.Add("Die SMTP-Verschlüsselung muss None, STARTTLS oder SSL sein.");
        }

        if (config.UseAuthentication)
        {
            ValidateRequiredText(config.Username, "SMTP-Benutzername", 512, errors);
            if (string.IsNullOrEmpty(config.Password))
            {
                errors.Add("Für die SMTP-Authentifizierung fehlt das Kennwort.");
            }
            else if (config.Password.Length > 4096)
            {
                errors.Add("Das SMTP-Kennwort ist ungewöhnlich lang und wurde abgelehnt.");
            }
        }

        if (!IsEmailAddress(config.SenderAddress))
        {
            errors.Add("Die Absender-Adresse ist ungültig.");
        }

        var recipients = (config.RecipientAddresses ?? new List<string>())
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (recipients.Length == 0)
        {
            errors.Add("Es muss mindestens eine Empfänger-Adresse angegeben werden.");
        }
        else if (recipients.Length > 100)
        {
            errors.Add("Es sind höchstens 100 Empfänger-Adressen erlaubt.");
        }

        foreach (var recipient in recipients.Where(address => !IsEmailAddress(address)))
        {
            errors.Add($"Die Empfänger-Adresse '{recipient}' ist ungültig.");
        }

        if ((config.BodyPrefix?.Length ?? 0) > 10_000)
        {
            errors.Add("Der Nachrichtenvorspann darf höchstens 10.000 Zeichen enthalten.");
        }

        if (requireSchedule)
        {
            if (!TimeOnly.TryParseExact(config.ScheduleTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                errors.Add("Die Sendezeit muss eine gültige Uhrzeit im Format HH:mm sein.");
            }

            ValidateRequiredText(config.TaskName, "Aufgabenname", 238, errors);
            if (config.TaskName?.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            {
                errors.Add("Der Aufgabenname enthält unzulässige Zeichen.");
            }
        }

        return new EmailReportValidationResult { Errors = errors };
    }

    private static bool IsEmailAddress(string? value) =>
        !string.IsNullOrWhiteSpace(value) && MailAddress.TryCreate(value.Trim(), out var address) &&
        string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void ValidateRequiredText(string? value, string displayName, int maximumLength, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{displayName} darf nicht leer sein.");
            return;
        }

        if (value.Length > maximumLength || value.Any(char.IsControl))
        {
            errors.Add($"{displayName} enthält unzulässige Zeichen oder ist zu lang.");
        }
    }
}

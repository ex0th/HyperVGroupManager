using System.Globalization;
using System.Net.Mail;
using HyperVGroupManager.App.Localization;

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
    private static string L(string key, params object?[] arguments) =>
        arguments.Length == 0
            ? LocalizationService.Instance.Get(key)
            : LocalizationService.Instance.Format(key, arguments);

    public static EmailReportValidationResult ValidateForSend(EmailReportConfig config) =>
        Validate(config, requireSchedule: false);

    public static EmailReportValidationResult ValidateForScheduledTask(EmailReportConfig config) =>
        Validate(config, requireSchedule: true);

    private static EmailReportValidationResult Validate(EmailReportConfig config, bool requireSchedule)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = new List<string>();

        ValidateRequiredText(config.TargetName, L("Email.TargetName"), 255, errors);
        ValidateRequiredText(config.SmtpHost, L("Email.SmtpName"), 255, errors);
        if (config.SmtpPort is < 1 or > 65535)
        {
            errors.Add(L("Email.InvalidPort"));
        }

        if (config.SmtpSecurity is not ("None" or "STARTTLS" or "SSL"))
        {
            errors.Add(L("Email.InvalidSecurity"));
        }

        if (config.UseAuthentication)
        {
            ValidateRequiredText(config.Username, L("Email.Username"), 512, errors);
            if (string.IsNullOrEmpty(config.Password))
            {
                errors.Add(L("Email.PasswordMissing"));
            }
            else if (config.Password.Length > 4096)
            {
                errors.Add(L("Email.PasswordLong"));
            }
        }

        if (!IsEmailAddress(config.SenderAddress))
        {
            errors.Add(L("Email.InvalidSender"));
        }

        var recipients = (config.RecipientAddresses ?? new List<string>())
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (recipients.Length == 0)
        {
            errors.Add(L("Email.NoRecipients"));
        }
        else if (recipients.Length > 100)
        {
            errors.Add(L("Email.TooManyRecipients"));
        }

        foreach (var recipient in recipients.Where(address => !IsEmailAddress(address)))
        {
            errors.Add(L("Email.InvalidRecipient", recipient));
        }

        if ((config.BodyPrefix?.Length ?? 0) > 10_000)
        {
            errors.Add(L("Email.BodyLong"));
        }

        if (requireSchedule)
        {
            if (!TimeOnly.TryParseExact(config.ScheduleTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                errors.Add(L("Email.InvalidSchedule"));
            }

            ValidateRequiredText(config.TaskName, L("Email.TaskName"), 238, errors);
            if (config.TaskName?.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            {
                errors.Add(L("Email.InvalidTaskName"));
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
            errors.Add(L("Email.Required", displayName));
            return;
        }

        if (value.Length > maximumLength || value.Any(char.IsControl))
        {
            errors.Add(L("Email.InvalidText", displayName));
        }
    }
}

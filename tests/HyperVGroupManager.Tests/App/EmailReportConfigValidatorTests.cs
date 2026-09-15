using HyperVGroupManager.App.Services;

namespace HyperVGroupManager.Tests.App;

public class EmailReportConfigValidatorTests
{
    [Fact]
    public void ValidateForSend_ValidConfiguration_IsAccepted()
    {
        var result = EmailReportConfigValidator.ValidateForSend(ValidConfig());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateForSend_InvalidPortAddressAndMissingPassword_ReturnsAllErrors()
    {
        var config = ValidConfig();
        config.SmtpPort = 70_000;
        config.SenderAddress = "not-an-address";
        config.UseAuthentication = true;
        config.Username = "user";
        config.Password = string.Empty;

        var result = EmailReportConfigValidator.ValidateForSend(config);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3);
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("8:00")]
    [InlineData("12:60")]
    public void ValidateForScheduledTask_InvalidTime_IsRejected(string time)
    {
        var config = ValidConfig();
        config.ScheduleTime = time;

        var result = EmailReportConfigValidator.ValidateForScheduledTask(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Sendezeit", StringComparison.Ordinal));
    }

    private static EmailReportConfig ValidConfig() => new()
    {
        TargetName = "HV01",
        SmtpHost = "smtp.example.test",
        SmtpPort = 587,
        SmtpSecurity = "STARTTLS",
        SenderAddress = "hvgm@example.test",
        RecipientAddresses = new List<string> { "admin@example.test" },
        ScheduleTime = "08:00",
        TaskName = "HyperVGroupManager_UntaggedVMsReport",
    };
}

using HyperVGroupManager.App.Services;

namespace HyperVGroupManager.Tests.App;

public class EmailReportServiceTests
{
    [Fact]
    public void ProtectPassword_RoundTrip_IsUserBoundAndNotPlaintext()
    {
        const string password = "Very secret äöü !";

        var protectedValue = EmailReportService.ProtectPassword(password);
        var clearValue = EmailReportService.UnprotectPassword(protectedValue);

        Assert.NotEmpty(protectedValue);
        Assert.DoesNotContain(password, protectedValue, StringComparison.Ordinal);
        Assert.Equal(password, clearValue);
    }

    [Fact]
    public void ProtectPassword_EmptyValue_RemainsEmpty()
    {
        Assert.Empty(EmailReportService.ProtectPassword(string.Empty));
        Assert.Empty(EmailReportService.UnprotectPassword(string.Empty));
    }
}

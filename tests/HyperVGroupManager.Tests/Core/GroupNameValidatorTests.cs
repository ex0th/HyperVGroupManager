using HyperVGroupManager.Core.Services;

namespace HyperVGroupManager.Tests.Core;

public class GroupNameValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespaceName_ReturnsInvalid(string? name)
    {
        var result = GroupNameValidator.Validate(name, existingGroupNames: Array.Empty<string>());

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Validate_DuplicateName_IsCaseInsensitive_ReturnsInvalid()
    {
        var existing = new[] { "VEEAM_Backup_Daily" };

        var result = GroupNameValidator.Validate("veeam_backup_daily", existing);

        Assert.False(result.IsValid);
        Assert.Contains("existiert bereits", result.ErrorMessage);
    }

    [Fact]
    public void Validate_NewUniqueName_ReturnsValid()
    {
        var existing = new[] { "VEEAM_Backup_Daily" };

        var result = GroupNameValidator.Validate("VEEAM_Backup_Weekly", existing);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }
}

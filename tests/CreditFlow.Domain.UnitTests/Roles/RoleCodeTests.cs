using CreditFlow.Domain.Roles;

namespace CreditFlow.Domain.UnitTests.Roles;

public sealed class RoleCodeTests
{
    [Theory]
    [InlineData("UW", "UW")]
    [InlineData(" sysadmin ", "SYSADMIN")]
    [InlineData("DIR", "DIR")]
    public void Valid_codes_are_normalized_to_uppercase(string input, string expected)
    {
        var result = RoleCode.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("U")]
    [InlineData("UW-SENIOR")]
    [InlineData("ABCDEFGHIJKLMNOPQ")]
    public void Invalid_codes_are_rejected(string? input)
    {
        var result = RoleCode.Create(input);

        Assert.Equal(RoleErrors.InvalidCode, result.Error);
    }
}

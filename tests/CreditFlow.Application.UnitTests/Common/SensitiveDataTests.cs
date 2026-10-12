using CreditFlow.Application.Common;

namespace CreditFlow.Application.UnitTests.Common;

public sealed class SensitiveDataTests
{
    [Theory]
    [InlineData("Client 01001011001 applied", "Client *******1001 applied")]
    [InlineData("01001011001", "*******1001")]
    [InlineData("pn=01001011001;", "pn=*******1001;")]
    [InlineData("{\"PersonalNumber\":\"01001011001\"}", "{\"PersonalNumber\":\"*******1001\"}")]
    [InlineData("two: 01001011001, 35001022334", "two: *******1001, *******2334")]
    public void Personal_numbers_in_text_keep_only_the_last_four_digits(string text, string expected)
    {
        Assert.Equal(expected, SensitiveData.MaskText(text));
    }

    [Theory]
    [InlineData("0100101100")] // 10 digits
    [InlineData("010010110012")] // 12 digits
    [InlineData("Amount 1500.00 GEL")]
    [InlineData("")]
    public void Other_text_is_left_alone(string text)
    {
        Assert.Equal(text, SensitiveData.MaskText(text));
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("NewPassword")]
    [InlineData("access_token")]
    [InlineData("AuthenticatorKey")]
    [InlineData("RecoveryCodes")]
    public void Secret_fields_are_hidden_completely(string name)
    {
        Assert.True(SensitiveData.IsSensitiveField(name));
        Assert.Equal(SensitiveData.Hidden, SensitiveData.MaskField(name, "Very!Secret-123"));
    }

    [Theory]
    [InlineData("PersonalNumber", "01001011001", "*******1001")]
    [InlineData("IdDocumentNumber", "12AB34567", "*****4567")]
    [InlineData("personalNumber", "123", "***")]
    public void Identifier_fields_keep_only_their_last_four_characters(string name, string value, string expected)
    {
        Assert.True(SensitiveData.IsSensitiveField(name));
        Assert.Equal(expected, SensitiveData.MaskField(name, value));
    }

    [Fact]
    public void Ordinary_fields_are_not_sensitive_and_a_missing_value_is_hidden()
    {
        Assert.False(SensitiveData.IsSensitiveField("FullName"));
        Assert.Equal(SensitiveData.Hidden, SensitiveData.MaskField("PersonalNumber", null));
    }
}

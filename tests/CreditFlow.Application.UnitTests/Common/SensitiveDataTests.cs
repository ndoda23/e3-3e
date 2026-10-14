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

    [Theory]
    [InlineData("Email to anna.g@pcb.ge sent", "Email to a***@pcb.ge sent")]
    [InlineData("{\"Email\":\"sysadmin@pcb.ge\"}", "{\"Email\":\"s***@pcb.ge\"}")]
    [InlineData("to 01001011001@clients.example.com", "to 0***@clients.example.com")]
    public void Email_addresses_keep_the_first_character_and_the_domain(string text, string expected)
    {
        Assert.Equal(expected, SensitiveData.MaskText(text));
    }

    [Theory]
    [InlineData("Sms to +995 599 011011", "Sms to +*** *** ****11")]
    [InlineData("Sms to +995599011011", "Sms to +**********11")]
    [InlineData("call +995-32-2-123456 now", "call +***-**-*-****56 now")]
    [InlineData("{\"Phone\":\"+995 555 000000\"}", "{\"Phone\":\"+*** *** ****00\"}")]
    [InlineData("mobile 599 01 10 11", "mobile *** ** ** 11")]
    [InlineData("mobile 599011011.", "mobile *******11.")]
    public void Phone_numbers_keep_only_their_last_two_digits(string text, string expected)
    {
        Assert.Equal(expected, SensitiveData.MaskText(text));
    }

    [Theory]
    [InlineData("Amount 59900.00 GEL")]
    [InlineData("Application APP-000123 moved to Approved")]
    [InlineData("Contract LN-2026-000123")]
    [InlineData("Id 0199a1b2-5123-7456-8abc-512345678901")]
    [InlineData("Took 512 ms on 2026-10-13")]
    public void Amounts_references_ids_and_dates_are_not_taken_for_phone_numbers(string text)
    {
        Assert.Equal(text, SensitiveData.MaskText(text));
    }

    [Theory]
    [InlineData("Phone", "+995 599 011011", "+*** *** ****11")]
    [InlineData("Email", "anna.g@pcb.ge", "a***@pcb.ge")]
    [InlineData("Recipient", "+995 599 011011", "+*** *** ****11")]
    [InlineData("Phone", "ext. 42", "***. **")] // too short to keep any digits
    public void Contact_fields_are_always_masked(string name, string value, string expected)
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

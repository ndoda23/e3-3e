using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.UnitTests.Clients;

public sealed class ClientTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static readonly IdentityDocument IdCard =
        IdentityDocument.Create(IdentityDocumentType.IdCard, "12ab34567", new DateOnly(2020, 1, 1), new DateOnly(2030, 1, 1)).Value;

    private static ClientDetails Details(DateOnly? birthDate = null) =>
        new(" Anna ", " Gelashvili ", birthDate ?? new DateOnly(1990, 5, 17), "+995 555 123456", "anna@example.ge", "Tbilisi, Vake");

    // ----- Personal number -----

    [Theory]
    [InlineData("01001011001")]
    [InlineData(" 01001011001 ")]
    public void A_personal_number_has_exactly_eleven_digits(string value)
    {
        Assert.Equal("01001011001", PersonalNumber.Create(value).Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0100101100")]
    [InlineData("010010110012")]
    [InlineData("0100101100A")]
    [InlineData("0100 101100")]
    public void Anything_else_is_not_a_personal_number(string? value)
    {
        Assert.Equal(ClientErrors.InvalidPersonalNumber, PersonalNumber.Create(value).Error);
    }

    // ----- Registration and details -----

    [Fact]
    public void A_client_is_registered_with_normalized_details()
    {
        var client = Register();

        Assert.Equal("Anna Gelashvili", client.FullName);
        Assert.Equal("01001011001", client.PersonalNumber.Value);
        Assert.Equal("12AB34567", client.IdentityDocument.Number);
        Assert.Equal(KycStatus.NotChecked, client.KycStatus);
        Assert.Equal(ScreeningStatus.NotChecked, client.ScreeningStatus);
    }

    [Fact]
    public void Every_invalid_field_is_reported_at_once()
    {
        var errors = Client.Validate(new ClientDetails("", " ", Today, "abc", "no-at-sign", ""), Today);

        Assert.Equal(
            [
                ClientErrors.InvalidFirstName, ClientErrors.InvalidLastName, ClientErrors.InvalidBirthDate,
                ClientErrors.InvalidPhone, ClientErrors.InvalidEmail, ClientErrors.InvalidAddress,
            ],
            errors);
    }

    [Fact]
    public void Email_is_optional()
    {
        var client = Client.Register(Pn(), Details() with { Email = " " }, IdCard, Today).Value;

        Assert.Null(client.Email);
    }

    [Fact]
    public void A_birth_date_today_or_more_than_120_years_ago_is_rejected()
    {
        Assert.Contains(ClientErrors.InvalidBirthDate, Client.Validate(Details(Today), Today));
        Assert.Contains(ClientErrors.InvalidBirthDate, Client.Validate(Details(Today.AddYears(-121)), Today));
        Assert.Empty(Client.Validate(Details(Today.AddDays(-1)), Today));
    }

    // ----- Age -----

    [Theory]
    [InlineData("2008-10-12", "2026-10-12", 18)]
    [InlineData("2008-10-13", "2026-10-12", 17)]
    [InlineData("2008-02-29", "2026-02-28", 18)]
    [InlineData("2008-02-29", "2026-02-27", 17)]
    [InlineData("1951-10-12", "2026-10-11", 74)]
    public void Age_is_in_full_years(string birth, string on, int expected)
    {
        Assert.Equal(expected, Age.YearsOn(DateOnly.Parse(birth, System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.Parse(on, System.Globalization.CultureInfo.InvariantCulture)));
    }

    // ----- Identity document -----

    [Fact]
    public void A_document_is_valid_from_issue_to_expiry_inclusive()
    {
        Assert.True(IdCard.IsValidOn(new DateOnly(2030, 1, 1)));
        Assert.False(IdCard.IsValidOn(new DateOnly(2030, 1, 2)));
        Assert.False(IdCard.IsValidOn(new DateOnly(2019, 12, 31)));
    }

    [Fact]
    public void Document_rules()
    {
        Assert.Equal(ClientErrors.InvalidDocumentNumber,
            IdentityDocument.Create(IdentityDocumentType.Passport, "12-34", Today, Today.AddYears(10)).Error);
        Assert.Equal(ClientErrors.InvalidDocumentDates,
            IdentityDocument.Create(IdentityDocumentType.Passport, "P123", Today, Today).Error);
    }

    [Fact]
    public void A_new_document_resets_the_KYC_result()
    {
        var client = Register();
        client.RecordChecks(KycStatus.Verified, ScreeningStatus.Clear, DateTimeOffset.UtcNow);

        client.ReplaceIdentityDocument(IdentityDocument.Create(
            IdentityDocumentType.Passport, "P1234567", Today, Today.AddYears(10)).Value);

        Assert.Equal(KycStatus.NotChecked, client.KycStatus);
        Assert.Equal(ScreeningStatus.Clear, client.ScreeningStatus);
    }

    // ----- Incomes and obligations -----

    [Fact]
    public void Incomes_carry_their_currency()
    {
        var client = Register();

        var salary = client.AddIncome(IncomeType.Salary, "TBC Bank", Money.Of(3000m, Currency.GEL));
        client.AddIncome(IncomeType.Rental, "Flat", Money.Of(500m, Currency.USD));

        Assert.True(salary.IsSuccess);
        Assert.True(client.HasIncomeIn(Currency.USD));
        Assert.False(client.HasIncomeIn(Currency.EUR));

        Assert.True(client.RemoveIncome(salary.Value).IsSuccess);
        Assert.Single(client.Incomes);
        Assert.Equal(ClientErrors.IncomeNotFound, client.RemoveIncome(salary.Value).Error);
    }

    [Fact]
    public void Income_and_obligation_rules()
    {
        var client = Register();

        Assert.Equal(ClientErrors.InvalidIncome, client.AddIncome(IncomeType.Salary, "X", Money.Zero(Currency.GEL)).Error);
        Assert.Equal(ClientErrors.InvalidIncomeSource, client.AddIncome(IncomeType.Salary, " ", Money.Of(1m, Currency.GEL)).Error);
        Assert.Equal(ClientErrors.InvalidObligation,
            client.AddObligation("Friend", Money.Of(100m, Currency.GEL), Money.Of(1000m, Currency.USD)).Error);
        Assert.True(client.AddObligation("Friend", Money.Of(100m, Currency.GEL), Money.Zero(Currency.GEL)).IsSuccess);
    }

    private static PersonalNumber Pn() => PersonalNumber.Create("01001011001").Value;

    private static Client Register() => Client.Register(Pn(), Details(), IdCard, Today).Value;
}

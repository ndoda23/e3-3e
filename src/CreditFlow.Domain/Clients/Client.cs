using CreditFlow.Domain.Common;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.Clients;

/// <summary>Identifier of a client.</summary>
public readonly record struct ClientId(Guid Value)
{
    public static ClientId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Identifier of an income or obligation line of a client.</summary>
public readonly record struct ClientLineId(Guid Value)
{
    public static ClientLineId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}

public enum IncomeType
{
    Salary = 1,
    Business = 2,
    Pension = 3,
    Rental = 4,
    Other = 5,
}

/// <summary>Result of the KYC / identity document verification.</summary>
public enum KycStatus
{
    NotChecked = 0,
    Verified = 1,
    Failed = 2,
}

/// <summary>Result of the sanctions and PEP screening.</summary>
public enum ScreeningStatus
{
    NotChecked = 0,
    Clear = 1,
    SanctionsMatch = 2,
    PepMatch = 3,
}

/// <summary>The personal and contact data of a client, validated together.</summary>
public sealed record ClientDetails(
    string? FirstName, string? LastName, DateOnly BirthDate, string? Phone, string? Email, string? Address);

/// <summary>A monthly net income declared for the client (with its currency, for the FX and PTI rules).</summary>
public sealed class ClientIncome
{
    public const int SourceMaxLength = 200;

    private ClientIncome()
    {
    }

    public ClientLineId Id { get; private set; }

    public IncomeType Type { get; private set; }

    /// <summary>Employer, business or other source.</summary>
    public string Source { get; private set; } = string.Empty;

    public Money MonthlyAmount { get; private set; }

    internal static Result<ClientIncome> Create(IncomeType type, string? source, Money monthlyAmount)
    {
        var normalized = Text.Normalize(source);
        if (!Enum.IsDefined(type) || normalized.Length is 0 or > SourceMaxLength)
        {
            return Result.Failure<ClientIncome>(ClientErrors.InvalidIncomeSource);
        }

        return monthlyAmount.IsPositive
            ? Result.Success(new ClientIncome { Id = ClientLineId.New(), Type = type, Source = normalized, MonthlyAmount = monthlyAmount })
            : Result.Failure<ClientIncome>(ClientErrors.InvalidIncome);
    }
}

/// <summary>An obligation the client declared themselves (bureau and own-bank loans are added by scoring).</summary>
public sealed class ClientObligation
{
    public const int LenderMaxLength = 200;

    private ClientObligation()
    {
    }

    public ClientLineId Id { get; private set; }

    public string Lender { get; private set; } = string.Empty;

    public Money MonthlyPayment { get; private set; }

    public Money OutstandingAmount { get; private set; }

    internal static Result<ClientObligation> Create(string? lender, Money monthlyPayment, Money outstandingAmount)
    {
        var normalized = Text.Normalize(lender);
        if (normalized.Length is 0 or > LenderMaxLength)
        {
            return Result.Failure<ClientObligation>(ClientErrors.InvalidLender);
        }

        var valid = monthlyPayment.IsPositive
                    && outstandingAmount.Currency == monthlyPayment.Currency
                    && outstandingAmount.Amount >= 0m;
        return valid
            ? Result.Success(new ClientObligation
            {
                Id = ClientLineId.New(), Lender = normalized, MonthlyPayment = monthlyPayment, OutstandingAmount = outstandingAmount,
            })
            : Result.Failure<ClientObligation>(ClientErrors.InvalidObligation);
    }
}

/// <summary>
/// A client of the bank (a borrower, co-borrower or guarantor on applications). The personal number is unique and
/// never changes. Holds the identity document, incomes, declared obligations and the latest KYC and screening results.
/// </summary>
public sealed class Client : IAuditedEntity
{
    public const int NameMaxLength = 100;
    public const int PhoneMaxLength = 20;
    public const int EmailMaxLength = 256;
    public const int AddressMaxLength = 300;
    public const int MaxAgeYears = 120;

    private readonly List<ClientIncome> _incomes = [];
    private readonly List<ClientObligation> _obligations = [];

    private Client()
    {
    }

    public ClientId Id { get; private set; }

    public PersonalNumber PersonalNumber { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public DateOnly BirthDate { get; private set; }

    public string Phone { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public string Address { get; private set; } = string.Empty;

    public IdentityDocument IdentityDocument { get; private set; } = null!;

    public IReadOnlyList<ClientIncome> Incomes => _incomes;

    public IReadOnlyList<ClientObligation> Obligations => _obligations;

    public KycStatus KycStatus { get; private set; }

    public ScreeningStatus ScreeningStatus { get; private set; }

    /// <summary>When KYC and screening were last run.</summary>
    public DateTimeOffset? CheckedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public UserId? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public UserId? UpdatedBy { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    /// <param name="personalNumber">The unique personal number (uniqueness is checked by the caller and the database).</param>
    /// <param name="details">Personal and contact data.</param>
    /// <param name="identityDocument">The identity document entered by the operator.</param>
    /// <param name="today">The bank date, for the birth date check.</param>
    public static Result<Client> Register(
        PersonalNumber personalNumber, ClientDetails details, IdentityDocument identityDocument, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(identityDocument);

        var client = new Client { Id = ClientId.New(), PersonalNumber = personalNumber, IdentityDocument = identityDocument };
        var updated = client.UpdateDetails(details, today);
        return updated.IsSuccess ? Result.Success(client) : Result.Failure<Client>(updated.Error!);
    }

    /// <summary>All errors in the details at once, so a form can show every field error.</summary>
    public static IReadOnlyList<Error> Validate(ClientDetails details, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(details);

        var errors = new List<Error>();
        if (Text.Normalize(details.FirstName).Length is 0 or > NameMaxLength)
        {
            errors.Add(ClientErrors.InvalidFirstName);
        }

        if (Text.Normalize(details.LastName).Length is 0 or > NameMaxLength)
        {
            errors.Add(ClientErrors.InvalidLastName);
        }

        if (details.BirthDate >= today || details.BirthDate < today.AddYears(-MaxAgeYears))
        {
            errors.Add(ClientErrors.InvalidBirthDate);
        }

        var phone = Text.Normalize(details.Phone);
        if (phone.Length is 0 or > PhoneMaxLength
            || !phone.Select((c, i) => char.IsAsciiDigit(c) || c == ' ' || (c == '+' && i == 0)).All(ok => ok))
        {
            errors.Add(ClientErrors.InvalidPhone);
        }

        var email = Text.Normalize(details.Email);
        if (email.Length > 0 && (email.Length > EmailMaxLength || email.Contains(' ', StringComparison.Ordinal)
                                 || email.IndexOf('@', StringComparison.Ordinal) is <= 0 || email.EndsWith('@')))
        {
            errors.Add(ClientErrors.InvalidEmail);
        }

        if (Text.Normalize(details.Address).Length is 0 or > AddressMaxLength)
        {
            errors.Add(ClientErrors.InvalidAddress);
        }

        return errors;
    }

    public Result UpdateDetails(ClientDetails details, DateOnly today)
    {
        var errors = Validate(details, today);
        if (errors.Count > 0)
        {
            return Result.Failure(Error.ValidationFailed(errors));
        }

        FirstName = Text.Normalize(details.FirstName);
        LastName = Text.Normalize(details.LastName);
        BirthDate = details.BirthDate;
        Phone = Text.Normalize(details.Phone);
        Email = Text.Normalize(details.Email) is { Length: > 0 } email ? email : null;
        Address = Text.Normalize(details.Address);
        return Result.Success();
    }

    /// <summary>A new or renewed identity document; the previous KYC result no longer applies.</summary>
    public void ReplaceIdentityDocument(IdentityDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document == IdentityDocument)
        {
            return;
        }

        IdentityDocument = document;
        KycStatus = KycStatus.NotChecked;
    }

    public Result<ClientLineId> AddIncome(IncomeType type, string? source, Money monthlyAmount)
    {
        var income = ClientIncome.Create(type, source, monthlyAmount);
        if (income.IsFailure)
        {
            return Result.Failure<ClientLineId>(income.Error!);
        }

        _incomes.Add(income.Value);
        return Result.Success(income.Value.Id);
    }

    public Result RemoveIncome(ClientLineId id) =>
        _incomes.RemoveAll(i => i.Id == id) > 0 ? Result.Success() : Result.Failure(ClientErrors.IncomeNotFound);

    public Result<ClientLineId> AddObligation(string? lender, Money monthlyPayment, Money outstandingAmount)
    {
        var obligation = ClientObligation.Create(lender, monthlyPayment, outstandingAmount);
        if (obligation.IsFailure)
        {
            return Result.Failure<ClientLineId>(obligation.Error!);
        }

        _obligations.Add(obligation.Value);
        return Result.Success(obligation.Value.Id);
    }

    public Result RemoveObligation(ClientLineId id) =>
        _obligations.RemoveAll(o => o.Id == id) > 0 ? Result.Success() : Result.Failure(ClientErrors.ObligationNotFound);

    /// <summary>Records the outcome of KYC and of the sanctions / PEP screening (pre-check).</summary>
    public void RecordChecks(KycStatus kyc, ScreeningStatus screening, DateTimeOffset at)
    {
        if (!Enum.IsDefined(kyc) || !Enum.IsDefined(screening))
        {
            throw new ArgumentOutOfRangeException(nameof(kyc), "Unknown check result.");
        }

        KycStatus = kyc;
        ScreeningStatus = screening;
        CheckedAt = at;
    }

    /// <summary>Full years of age on the given date.</summary>
    public int AgeOn(DateOnly date) => Age.YearsOn(BirthDate, date);

    /// <summary>True when the client declared an income in the given currency.</summary>
    public bool HasIncomeIn(Currency currency) => _incomes.Any(i => i.MonthlyAmount.Currency == currency);

    void IAuditedEntity.MarkCreated(UserId? by, DateTimeOffset at)
    {
        CreatedAt = at;
        CreatedBy = by;
    }

    void IAuditedEntity.MarkUpdated(UserId? by, DateTimeOffset at)
    {
        UpdatedAt = at;
        UpdatedBy = by;
    }
}

/// <summary>Age in full years (a person born on 29 February turns a year older on 28 February in common years).</summary>
public static class Age
{
    public static int YearsOn(DateOnly birthDate, DateOnly date)
    {
        var years = date.Year - birthDate.Year;
        return date < AnniversaryIn(birthDate, date.Year) ? years - 1 : years;
    }

    private static DateOnly AnniversaryIn(DateOnly birthDate, int year) =>
        new(year, birthDate.Month, Math.Min(birthDate.Day, DateTime.DaysInMonth(year, birthDate.Month)));
}

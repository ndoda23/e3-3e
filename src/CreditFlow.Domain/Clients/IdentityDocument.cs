using CreditFlow.Domain.Common;

namespace CreditFlow.Domain.Clients;

public enum IdentityDocumentType
{
    IdCard = 1,
    Passport = 2,
}

/// <summary>
/// The client's identity document as entered by the operator (verified by the KYC service). It must be valid on the
/// day of application and again on the day of disbursement.
/// </summary>
public sealed record IdentityDocument
{
    public const int NumberMaxLength = 20;

    /// <summary>For materialization by the persistence layer only.</summary>
    private IdentityDocument()
    {
        Number = string.Empty;
    }

    private IdentityDocument(IdentityDocumentType type, string number, DateOnly issuedOn, DateOnly expiresOn)
    {
        Type = type;
        Number = number;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
    }

    public IdentityDocumentType Type { get; }

    public string Number { get; }

    public DateOnly IssuedOn { get; }

    public DateOnly ExpiresOn { get; }

    public static Result<IdentityDocument> Create(
        IdentityDocumentType type, string? number, DateOnly issuedOn, DateOnly expiresOn)
    {
        if (!Enum.IsDefined(type))
        {
            return Result.Failure<IdentityDocument>(ClientErrors.InvalidDocumentType);
        }

        var normalized = Text.Normalize(number).ToUpperInvariant();
        if (normalized.Length is 0 or > NumberMaxLength || !normalized.All(char.IsAsciiLetterOrDigit))
        {
            return Result.Failure<IdentityDocument>(ClientErrors.InvalidDocumentNumber);
        }

        return expiresOn <= issuedOn
            ? Result.Failure<IdentityDocument>(ClientErrors.InvalidDocumentDates)
            : Result.Success(new IdentityDocument(type, normalized, issuedOn, expiresOn));
    }

    /// <summary>Issued on or before, and not expired on, the given date (the expiry day itself is still valid).</summary>
    public bool IsValidOn(DateOnly date) => IssuedOn <= date && date <= ExpiresOn;
}

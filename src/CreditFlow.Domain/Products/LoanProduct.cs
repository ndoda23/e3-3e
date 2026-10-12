using CreditFlow.Domain.Common;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.Products;

/// <summary>Identifier of a loan product.</summary>
public readonly record struct LoanProductId(Guid Value)
{
    public static LoanProductId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// One published version of a product's terms. Versions are immutable: changing a product publishes a new version,
/// so applications that stored a snapshot of an earlier version are not affected.
/// </summary>
public sealed class LoanProductVersion
{
    private LoanProductVersion()
    {
    }

    public int Number { get; private set; }

    public ProductTerms Terms { get; private set; } = null!;

    public DateTimeOffset PublishedAt { get; private set; }

    internal static LoanProductVersion Create(int number, ProductTerms terms, DateTimeOffset publishedAt) =>
        new() { Number = number, Terms = terms, PublishedAt = publishedAt };
}

/// <summary>
/// A loan product (consumer, mortgage, auto) with its versioned terms. The current version is the one new
/// applications use; the Admin changes terms by publishing a new version.
/// </summary>
public sealed class LoanProduct : IAuditedEntity
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 100;

    private readonly List<LoanProductVersion> _versions = [];

    private LoanProduct()
    {
    }

    public LoanProductId Id { get; private set; }

    /// <summary>Short unique code (e.g. <c>CONS</c>). Immutable.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public ProductType Type { get; private set; }

    /// <summary>Inactive products are not offered for new applications.</summary>
    public bool IsActive { get; private set; }

    public IReadOnlyList<LoanProductVersion> Versions => _versions;

    public LoanProductVersion CurrentVersion => _versions[^1];

    public DateTimeOffset CreatedAt { get; private set; }

    public UserId? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public UserId? UpdatedBy { get; private set; }

    public static Result<LoanProduct> Create(
        string? code, string? name, ProductType type, ProductTerms terms, DateTimeOffset publishedAt)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var normalizedCode = Text.Normalize(code).ToUpperInvariant();
        var errors = new List<Error>();
        if (normalizedCode.Length is < 2 or > CodeMaxLength
            || !normalizedCode.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '-'))
        {
            errors.Add(ProductErrors.InvalidCode);
        }

        if (!Enum.IsDefined(type))
        {
            errors.Add(ProductErrors.InvalidType);
        }

        var product = new LoanProduct { Id = LoanProductId.New(), Code = normalizedCode, Type = type, IsActive = true };
        if (product.Rename(name).Error is { } nameError)
        {
            errors.Add(nameError);
        }

        if (errors.Count == 0)
        {
            errors.AddRange(terms.Validate(type));
        }

        if (errors.Count > 0)
        {
            return Result.Failure<LoanProduct>(Error.ValidationFailed(errors));
        }

        product._versions.Add(LoanProductVersion.Create(1, terms, publishedAt));
        return Result.Success(product);
    }

    /// <summary>Publishes new terms as the next version; earlier versions stay unchanged.</summary>
    public Result<LoanProductVersion> PublishVersion(ProductTerms terms, DateTimeOffset publishedAt)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var errors = terms.Validate(Type);
        if (errors.Count > 0)
        {
            return Result.Failure<LoanProductVersion>(Error.ValidationFailed(errors));
        }

        if (terms == CurrentVersion.Terms)
        {
            return Result.Failure<LoanProductVersion>(ProductErrors.NothingChanged);
        }

        var version = LoanProductVersion.Create(CurrentVersion.Number + 1, terms, publishedAt);
        _versions.Add(version);
        return Result.Success(version);
    }

    public LoanProductVersion? FindVersion(int number) => _versions.SingleOrDefault(v => v.Number == number);

    public Result Rename(string? name)
    {
        var normalized = Text.Normalize(name);
        if (normalized.Length is 0 or > NameMaxLength)
        {
            return Result.Failure(ProductErrors.InvalidName);
        }

        Name = normalized;
        return Result.Success();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

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

public static class ProductErrors
{
    public static readonly Error InvalidCode = Error.Validation(
        "Product.InvalidCode", "A product code has 2 to 20 uppercase letters, digits or hyphens.", "Code");

    public static readonly Error DuplicateCode = Error.Conflict(
        "Product.DuplicateCode", "Another product already uses this code.", "Code");

    public static readonly Error InvalidName = Error.Validation(
        "Product.InvalidName", "A product name is required (up to 100 characters).", "Name");

    public static readonly Error InvalidType = Error.Validation(
        "Product.InvalidType", "The product type is not valid.", "Type");

    public static readonly Error InvalidAmountRange = Error.Validation(
        "Product.InvalidAmountRange", "The minimum amount must be greater than zero and not above the maximum.", "MinAmountGel");

    public static readonly Error InvalidTermRange = Error.Validation(
        "Product.InvalidTermRange", "The term must be 1 to 360 months, minimum not above maximum.", "MinTermMonths");

    public static readonly Error InvalidRateRange = Error.Validation(
        "Product.InvalidRateRange", "The rate range must be 0 to 100 %, minimum not above maximum.", "MinRate");

    public static readonly Error InvalidFee = Error.Validation(
        "Product.InvalidFee", "Fees and the early-repayment penalty must be 0 to 100 %.", "UpfrontFeePercent");

    public static readonly Error InvalidCurrencies = Error.Validation(
        "Product.InvalidCurrencies", "Choose at least one currency (each once).", "Currencies");

    public static readonly Error InvalidScheduleTypes = Error.Validation(
        "Product.InvalidScheduleTypes", "Choose at least one schedule type (each once).", "ScheduleTypes");

    public static readonly Error InvalidChecklist = Error.Validation(
        "Product.InvalidChecklist", "The document checklist or the standard conditions are not valid.", "RequiredDocuments");

    public static readonly Error UnsecuredProductHasNoCollateral = Error.Validation(
        "Product.UnsecuredProductHasNoCollateral", "A consumer product takes no collateral and has no LTV limit.", "MaxLtvPercent");

    public static readonly Error NothingChanged = Error.Conflict(
        "Product.NothingChanged", "The terms are the same as the current version.");

    public static readonly Error NotFound = Error.NotFound("Product.NotFound", "The product was not found.");

    public static Error InvalidLtv(ProductType type, decimal ceiling) => Error.Validation(
        "Product.InvalidLtv",
        $"A {type} product requires collateral and a maximum LTV above 0 and at most {ceiling} %.",
        "MaxLtvPercent");
}

using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;

namespace CreditFlow.Domain.UnitTests.Products;

public sealed class LoanProductTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

    internal static ProductTerms Consumer() => new()
    {
        MinAmountGel = 500m,
        MaxAmountGel = 50_000m,
        MinTermMonths = 6,
        MaxTermMonths = 60,
        MinRate = 12m,
        MaxRate = 24m,
        UpfrontFeePercent = 1m,
        EarlyRepaymentPenaltyPercent = 2m,
        Currencies = [Currency.GEL],
        ScheduleTypes = [ScheduleType.Annuity, ScheduleType.Differentiated],
        RequiredDocuments =
        [
            new(DocumentType.IdentityDocumentCopy),
            new(DocumentType.IncomeStatement),
            new(DocumentType.EmploymentCertificate, MinAmountGel: 20_000m),
        ],
    };

    internal static ProductTerms Mortgage() => Consumer() with
    {
        MaxAmountGel = 1_000_000m,
        MaxTermMonths = 240,
        CollateralRequired = true,
        MaxLtvPercent = 80m,
        Currencies = [Currency.GEL, Currency.USD],
        StandardConditions = [ConditionType.CollateralRegistration, ConditionType.CollateralInsurance],
    };

    [Fact]
    public void A_product_starts_with_version_1()
    {
        var product = LoanProduct.Create(" cons ", "Consumer loan", ProductType.Consumer, Consumer(), At).Value;

        Assert.Equal("CONS", product.Code);
        Assert.True(product.IsActive);
        Assert.Equal(1, Assert.Single(product.Versions).Number);
        Assert.Equal(Consumer(), product.CurrentVersion.Terms);
    }

    [Fact]
    public void Publishing_adds_a_version_and_keeps_the_earlier_one_unchanged()
    {
        var product = LoanProduct.Create("CONS", "Consumer loan", ProductType.Consumer, Consumer(), At).Value;

        var v2 = product.PublishVersion(Consumer() with { MaxRate = 22m }, At.AddDays(1));

        Assert.True(v2.IsSuccess);
        Assert.Equal(2, product.CurrentVersion.Number);
        Assert.Equal(24m, product.FindVersion(1)!.Terms.MaxRate);
        Assert.Equal(22m, product.CurrentVersion.Terms.MaxRate);
    }

    [Fact]
    public void Publishing_identical_terms_is_refused()
    {
        var product = LoanProduct.Create("CONS", "Consumer loan", ProductType.Consumer, Consumer(), At).Value;

        Assert.Equal(ProductErrors.NothingChanged, product.PublishVersion(Consumer(), At).Error);
    }

    [Fact]
    public void Terms_report_every_broken_rule()
    {
        var terms = Consumer() with
        {
            MinAmountGel = 0m,
            MinTermMonths = 10,
            MaxTermMonths = 5,
            MaxRate = 5m,
            UpfrontFeePercent = 101m,
            Currencies = [],
            ScheduleTypes = [ScheduleType.Annuity, ScheduleType.Annuity],
        };

        Assert.Equal(
            [
                ProductErrors.InvalidAmountRange, ProductErrors.InvalidTermRange, ProductErrors.InvalidRateRange,
                ProductErrors.InvalidFee, ProductErrors.InvalidCurrencies, ProductErrors.InvalidScheduleTypes,
            ],
            terms.Validate(ProductType.Consumer));
    }

    [Theory]
    [InlineData(ProductType.Mortgage, 80, true)]
    [InlineData(ProductType.Mortgage, 80.01, false)]
    [InlineData(ProductType.Auto, 70, true)]
    [InlineData(ProductType.Auto, 70.5, false)]
    public void Secured_products_need_collateral_and_an_LTV_within_the_ceiling(ProductType type, double ltv, bool valid)
    {
        var terms = Mortgage() with { MaxLtvPercent = (decimal)ltv };

        Assert.Equal(valid, terms.Validate(type).Count == 0);
    }

    [Fact]
    public void A_secured_product_without_collateral_and_a_consumer_product_with_collateral_are_invalid()
    {
        Assert.Equal("Product.InvalidLtv",
            Assert.Single((Mortgage() with { CollateralRequired = false }).Validate(ProductType.Mortgage)).Code);
        Assert.Equal(ProductErrors.UnsecuredProductHasNoCollateral,
            Assert.Single(Mortgage().Validate(ProductType.Consumer)));
    }

    [Fact]
    public void The_checklist_depends_on_the_amount()
    {
        var terms = Consumer();

        Assert.Equal([DocumentType.IdentityDocumentCopy, DocumentType.IncomeStatement], terms.DocumentsRequiredFor(19_999.99m));
        Assert.Contains(DocumentType.EmploymentCertificate, terms.DocumentsRequiredFor(20_000m));
    }

    [Fact]
    public void Ranges_are_inclusive()
    {
        var terms = Consumer();

        Assert.True(terms.AllowsAmount(500m) && terms.AllowsAmount(50_000m));
        Assert.False(terms.AllowsAmount(499.99m) || terms.AllowsAmount(50_000.01m));
        Assert.True(terms.AllowsTerm(6) && terms.AllowsTerm(60) && !terms.AllowsTerm(61));
        Assert.True(terms.AllowsRate(12m) && terms.AllowsRate(24m) && !terms.AllowsRate(24.01m));
    }

    [Fact]
    public void Code_name_and_type_are_validated_together()
    {
        var result = LoanProduct.Create("x", "", (ProductType)9, Consumer(), At);

        Assert.Equal(
            ["Product.InvalidCode", "Product.InvalidType", "Product.InvalidName"],
            result.Error!.Details.Select(e => e.Code));
    }
}

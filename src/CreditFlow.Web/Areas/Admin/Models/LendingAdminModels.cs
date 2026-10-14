using System.ComponentModel.DataAnnotations;
using CreditFlow.Application.Administration;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;

namespace CreditFlow.Web.Areas.Admin.Models;

/// <summary>One document of the checklist form: required or not, and from which GEL amount.</summary>
public sealed class DocumentRow
{
    public DocumentType Type { get; set; }

    public bool Required { get; set; }

    public decimal? MinAmountGel { get; set; }
}

/// <summary>The terms of a product version as a form (every field of <see cref="ProductTerms"/>).</summary>
public sealed class ProductTermsForm
{
    [Display(Name = "Products.MinAmountGel")]
    public decimal MinAmountGel { get; set; }

    [Display(Name = "Products.MaxAmountGel")]
    public decimal MaxAmountGel { get; set; }

    [Display(Name = "Products.MinTermMonths")]
    public int MinTermMonths { get; set; }

    [Display(Name = "Products.MaxTermMonths")]
    public int MaxTermMonths { get; set; }

    [Display(Name = "Products.MinRate")]
    public decimal MinRate { get; set; }

    [Display(Name = "Products.MaxRate")]
    public decimal MaxRate { get; set; }

    [Display(Name = "Products.UpfrontFeePercent")]
    public decimal UpfrontFeePercent { get; set; }

    [Display(Name = "Products.MonthlyFeePercent")]
    public decimal MonthlyFeePercent { get; set; }

    [Display(Name = "Products.EarlyRepaymentPenaltyPercent")]
    public decimal EarlyRepaymentPenaltyPercent { get; set; }

    [Display(Name = "Products.CollateralRequired")]
    public bool CollateralRequired { get; set; }

    [Display(Name = "Products.MaxLtvPercent")]
    public decimal? MaxLtvPercent { get; set; }

    public List<Currency> Currencies { get; set; } = [];

    public List<ScheduleType> ScheduleTypes { get; set; } = [];

    public List<ConditionType> StandardConditions { get; set; } = [];

    /// <summary>One row per document type (all types are listed; <see cref="DocumentRow.Required"/> selects them).</summary>
    public List<DocumentRow> Documents { get; set; } = [];

    public static ProductTermsForm From(ProductTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        return new()
        {
            MinAmountGel = terms.MinAmountGel,
            MaxAmountGel = terms.MaxAmountGel,
            MinTermMonths = terms.MinTermMonths,
            MaxTermMonths = terms.MaxTermMonths,
            MinRate = terms.MinRate,
            MaxRate = terms.MaxRate,
            UpfrontFeePercent = terms.UpfrontFeePercent,
            MonthlyFeePercent = terms.MonthlyFeePercent,
            EarlyRepaymentPenaltyPercent = terms.EarlyRepaymentPenaltyPercent,
            CollateralRequired = terms.CollateralRequired,
            MaxLtvPercent = terms.MaxLtvPercent,
            Currencies = [.. terms.Currencies],
            ScheduleTypes = [.. terms.ScheduleTypes],
            StandardConditions = [.. terms.StandardConditions],
            Documents = Enum.GetValues<DocumentType>().Select(type =>
            {
                var requirement = terms.RequiredDocuments.FirstOrDefault(d => d.Type == type);
                return new DocumentRow { Type = type, Required = requirement is not null, MinAmountGel = requirement?.MinAmountGel };
            }).ToList(),
        };
    }

    /// <summary>An empty form with every document type listed.</summary>
    public static ProductTermsForm Empty() => new()
    {
        Currencies = [Currency.GEL],
        ScheduleTypes = [ScheduleType.Annuity],
        Documents = Enum.GetValues<DocumentType>().Select(type => new DocumentRow { Type = type }).ToList(),
    };

    public ProductTerms ToTerms() => new()
    {
        MinAmountGel = MinAmountGel,
        MaxAmountGel = MaxAmountGel,
        MinTermMonths = MinTermMonths,
        MaxTermMonths = MaxTermMonths,
        MinRate = MinRate,
        MaxRate = MaxRate,
        UpfrontFeePercent = UpfrontFeePercent,
        MonthlyFeePercent = MonthlyFeePercent,
        EarlyRepaymentPenaltyPercent = EarlyRepaymentPenaltyPercent,
        CollateralRequired = CollateralRequired,
        MaxLtvPercent = MaxLtvPercent,
        Currencies = Currencies.Distinct().ToList(),
        ScheduleTypes = ScheduleTypes.Distinct().ToList(),
        StandardConditions = StandardConditions.Distinct().ToList(),
        RequiredDocuments = Documents.Where(d => d.Required).Select(d => new DocumentRequirement(d.Type, d.MinAmountGel)).ToList(),
    };
}

/// <summary>A new product: code (fixed afterwards), name, type and the terms of version 1.</summary>
public sealed class ProductCreateForm
{
    [Display(Name = "Products.Code")]
    public string? Code { get; set; }

    [Display(Name = "Products.Name")]
    public string? Name { get; set; }

    [Display(Name = "Products.Type")]
    public ProductType Type { get; set; } = ProductType.Consumer;

    public ProductTermsForm Terms { get; set; } = ProductTermsForm.Empty();
}

/// <summary>The product page: the product with its versions and the form for the next version.</summary>
public sealed record ProductPageViewModel(ProductAdminDetails Product, ProductTermsForm NextVersion);

/// <summary>One rule of a rule-set version as a form row.</summary>
public sealed class RuleRow
{
    public string Code { get; set; } = string.Empty;

    public bool Included { get; set; }

    public bool Overridable { get; set; }

    public string ParametersJson { get; set; } = "{}";
}

/// <summary>The scoring rules page: versions, the version in force and the form for the next one.</summary>
public sealed record ScoringRulesViewModel(
    IReadOnlyList<RuleSetVersionItem> Versions, ScoringRuleSet? Current, IReadOnlyList<RuleRow> NextVersion);

/// <summary>The FX rates page with its currency filter.</summary>
public sealed record FxRatesViewModel(Currency? Currency, PagedResult<FxRateItem> Rates);

/// <summary>The holidays of one year.</summary>
public sealed record HolidaysViewModel(int Year, IReadOnlyList<HolidayItem> Holidays);

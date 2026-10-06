using System.ComponentModel.DataAnnotations;
using Lams.Application.AuthorityLimits;
using Lams.Domain.AuthorityLimits;
using Lams.Domain.Roles;
using Lams.Domain.Users;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Lams.Web.Areas.Admin.Models;

/// <summary>Create / edit form of a credit decision limit. Role, level and currency identify the row (create only).</summary>
public sealed class CreditLimitForm
{
    [Display(Name = "Field.Role")]
    public Guid? RoleId { get; set; }

    [Display(Name = "Field.AuthorityLevel")]
    public AuthorityLevel? AuthorityLevel { get; set; }

    [Display(Name = "Field.Currency")]
    public Currency? Currency { get; set; } = Domain.AuthorityLimits.Currency.GEL;

    [Display(Name = "Field.MaxAmount")]
    public decimal? MaxAmount { get; set; }

    /// <summary>Business roles (limits do not apply to administrative roles).</summary>
    [BindNever]
    public IReadOnlyList<RoleOption> Roles { get; set; } = [];

    /// <summary>The limit being edited (<c>null</c> on create).</summary>
    [BindNever]
    public AuthorityLimitItem? Existing { get; set; }

    public static CreditLimitForm From(AuthorityLimitItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new()
        {
            RoleId = item.RoleId.Value,
            AuthorityLevel = item.AuthorityLevel,
            Currency = item.Currency,
            MaxAmount = item.MaxAmount,
            Existing = item,
        };
    }

    public CreateCreditDecisionLimitCommand ToCreateCommand() => new(
        new RoleId(RoleId ?? Guid.Empty), AuthorityLevel, Currency ?? default, MaxAmount ?? 0m);
}

/// <summary>Create / edit form of a pricing exception limit. Empty thresholds mean "no limit".</summary>
public sealed class PricingLimitForm
{
    [Display(Name = "Field.Role")]
    public Guid? RoleId { get; set; }

    [Display(Name = "Field.MaxRateGapPp")]
    public decimal? MaxRateGapPp { get; set; }

    [Display(Name = "Field.MinCustomerRoaePct")]
    public decimal? MinCustomerRoaePct { get; set; }

    [BindNever]
    public IReadOnlyList<RoleOption> Roles { get; set; } = [];

    [BindNever]
    public AuthorityLimitItem? Existing { get; set; }

    public static PricingLimitForm From(AuthorityLimitItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new()
        {
            RoleId = item.RoleId.Value,
            MaxRateGapPp = item.MaxRateGapPp,
            MinCustomerRoaePct = item.MinCustomerRoaePct,
            Existing = item,
        };
    }
}

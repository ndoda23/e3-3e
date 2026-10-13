using CreditFlow.Application.Administration;
using CreditFlow.Domain.Scoring;
using CreditFlow.Web.Areas.Admin.Models;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Areas.Admin.Controllers;

/// <summary>
/// Admin › Scoring rules: the versions, the rules of the version in force, and a form that publishes the next version
/// (rules included, their parameters, whether a failure can be overridden).
/// </summary>
[HasPermission(P.Admin.ScoringRulesManage)]
public sealed class ScoringRulesController(ScoringRuleAdministrationService rules, IStringLocalizer<SharedResource> localizer)
    : AdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(int? version, CancellationToken cancellationToken)
    {
        var shown = version is { } number ? await rules.GetAsync(number, cancellationToken) : await rules.GetLatestAsync(cancellationToken);
        if (version is not null && shown is null)
        {
            return NotFound();
        }

        var latest = await rules.GetLatestAsync(cancellationToken);
        return View(await ModelAsync(shown, Rows(latest), cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Publish(List<RuleRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var definitions = rows.Where(r => r.Included)
            .Select(r => new RuleDefinition(r.Code, r.Overridable, string.IsNullOrWhiteSpace(r.ParametersJson) ? "{}" : r.ParametersJson.Trim()))
            .ToList();
        var published = await rules.PublishAsync(definitions, cancellationToken);
        if (published.IsSuccess)
        {
            TempData["Notice"] = localizer["ScoringRules.Published", published.Value].Value;
            return RedirectToAction(nameof(Index));
        }

        ModelState.AddError(published.Error!, localizer);
        return View(nameof(Index), await ModelAsync(await rules.GetLatestAsync(cancellationToken), rows, cancellationToken));
    }

    private async Task<ScoringRulesViewModel> ModelAsync(
        ScoringRuleSet? shown, IReadOnlyList<RuleRow> nextVersion, CancellationToken cancellationToken) =>
        new(await rules.ListAsync(cancellationToken), shown, nextVersion);

    /// <summary>The form starts from the version in force; rules the engine knows but the version omits are listed off.</summary>
    private List<RuleRow> Rows(ScoringRuleSet? latest)
    {
        var rows = (latest?.Rules ?? [])
            .Select(r => new RuleRow { Code = r.Code, Included = true, Overridable = r.Overridable, ParametersJson = r.ParametersJson })
            .ToList();
        rows.AddRange(rules.KnownRuleCodes
            .Where(code => rows.All(r => r.Code != code))
            .Order(StringComparer.Ordinal)
            .Select(code => new RuleRow { Code = code }));
        return rows;
    }
}

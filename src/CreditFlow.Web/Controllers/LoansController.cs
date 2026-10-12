using CreditFlow.Application.Disbursements;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Loans;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Infrastructure;
using CreditFlow.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using P = CreditFlow.Application.Authorization.Permissions;

namespace CreditFlow.Web.Controllers;

/// <summary>
/// The loan page: terms, effective rate, schedule and repayments; the operator records the next installment or an
/// early repayment (with a quote of what it costs today). A loan outside the viewer's scope is 404.
/// </summary>
[HasPermission(P.Loans.View)]
public sealed class LoansController(LoanService loans, CurrentAccess currentAccess, IStringLocalizer<SharedResource> localizer)
    : Controller
{
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, decimal? quote, bool full, CancellationToken cancellationToken)
    {
        var loan = await loans.GetAsync(new LoanId(id), cancellationToken);
        if (loan is null)
        {
            return NotFound();
        }

        EarlyRepaymentQuote? earlyQuote = null;
        if (quote is not null || full)
        {
            var quoted = await loans.QuoteEarlyRepaymentAsync(loan.Id, full ? null : quote, cancellationToken);
            if (quoted.IsSuccess)
            {
                earlyQuote = quoted.Value;
            }
            else
            {
                TempData["Error"] = ModelStateExtensions.LocalizeAll(quoted.Error!, localizer);
            }
        }

        var canRecord = (await currentAccess.GetAsync())?.HasPermission(P.Loans.RecordRepayment) == true;
        return View(new LoanPageViewModel(loan, canRecord && loan.Status == LoanStatus.Active, earlyQuote, full ? null : quote));
    }

    [HttpPost]
    [HasPermission(P.Loans.RecordRepayment)]
    public async Task<IActionResult> PayInstallment(Guid id, CancellationToken cancellationToken) =>
        Done(id, await loans.PayInstallmentAsync(new LoanId(id), cancellationToken));

    /// <summary>An early repayment of <paramref name="amount"/> of principal, or of everything when <paramref name="full"/>.</summary>
    [HttpPost]
    [HasPermission(P.Loans.RecordRepayment)]
    public async Task<IActionResult> RepayEarly(Guid id, decimal? amount, bool full, CancellationToken cancellationToken) =>
        Done(id, await loans.RepayEarlyAsync(new LoanId(id), full ? null : amount ?? 0m, cancellationToken));

    private IActionResult Done(Guid id, Result result)
    {
        if (result.Error == LoanErrors.NotFound)
        {
            return NotFound();
        }

        if (result.IsSuccess)
        {
            TempData["Notice"] = localizer["Loans.RepaymentRecorded"].Value;
        }
        else
        {
            TempData["Error"] = ModelStateExtensions.LocalizeAll(result.Error!, localizer);
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}

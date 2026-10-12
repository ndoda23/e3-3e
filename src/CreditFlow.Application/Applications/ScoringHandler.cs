using CreditFlow.Application.Common;
using CreditFlow.Application.External;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Approval;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Loans;
using CreditFlow.Domain.Scoring;

namespace CreditFlow.Application.Applications;

/// <summary>
/// Step 4 (scoring) runs when an application enters Scoring: the newest rule set is evaluated with the facts of every
/// party (incomes, obligations, bureau report, exposure), the GEL amount is fixed at today's rate, and the application
/// is rejected or routed to Underwriting / the Credit Committee by the approval thresholds.
/// </summary>
internal sealed class ScoringHandler(
    ILoanApplicationRepository applications,
    IClientRepository clients,
    IBureauReportStore bureauReports,
    IExposureQueries exposure,
    IScoringRuleSetRepository ruleSets,
    IApprovalThresholdsRepository thresholds,
    IFxRateRepository rates,
    ScoringEngine engine,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IOutboxHandler<ApplicationStatusChanged>
{
    public async Task HandleAsync(ApplicationStatusChanged domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.To != ApplicationStatus.Scoring)
        {
            return;
        }

        var application = await applications.GetAsync(domainEvent.ApplicationId, cancellationToken);
        if (application?.Status != ApplicationStatus.Scoring)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var today = BankTime.DateOf(now);
        var ruleSet = await ruleSets.GetLatestAsync(cancellationToken)
                      ?? throw new InvalidOperationException("No scoring rule set is published.");
        var currentRates = await RatesAsync(today, cancellationToken);
        var loanRate = currentRates[application.Terms.Amount.Currency];
        var amountGel = loanRate.ToGel(application.Terms.Amount).Amount;

        var partyIds = application.Parties.Select(p => p.ClientId).ToList();
        var parties = await clients.GetManyAsync(partyIds, cancellationToken);
        var reports = await bureauReports.GetAsync(application.Id, cancellationToken);
        var exposures = await exposure.ExposureGelAsync(partyIds, application.Id, cancellationToken);
        var installments = await exposure.MonthlyInstallmentsGelAsync(partyIds, cancellationToken);

        var facts = application.Parties.Select(p =>
        {
            var client = parties[p.ClientId];
            return new PartyFacts(
                client.Id,
                client.FullName,
                p.Role,
                client.Incomes.Select(i => i.MonthlyAmount).ToList(),
                client.Obligations.Select(o => o.MonthlyPayment).ToList(),
                installments.GetValueOrDefault(client.Id),
                reports.GetValueOrDefault(client.Id),
                exposures.GetValueOrDefault(client.Id));
        }).ToList();

        var product = application.Snapshot!.Terms;
        var schedule = ScheduleCalculator.Build(
            application.Terms.Amount, application.Terms.AnnualRate, application.Terms.TermMonths, application.Terms.ScheduleType,
            today, application.Terms.Amount.Percent(product.MonthlyFeePercent));

        var input = new ScoringInput(
            application.Terms, product, amountGel, schedule.LargestInstallment, application.Collaterals, facts, currentRates, today);
        var run = engine.Score(ruleSet, input, now);
        var tier = (await thresholds.GetAsync(cancellationToken)).TierFor(amountGel);

        application.CompleteScoring(run, loanRate, tier, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Today's rate of every currency; a missing rate stops scoring and the outbox retries later.</summary>
    private async Task<Dictionary<Currency, ExchangeRate>> RatesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Currency, ExchangeRate> { [Currency.GEL] = ExchangeRate.Gel(today) };
        foreach (var currency in new[] { Currency.USD, Currency.EUR })
        {
            var rate = await rates.FindEffectiveAsync(currency, today, cancellationToken)
                       ?? throw new InvalidOperationException(FxErrors.RateMissing(currency, today).Description);
            result[currency] = rate.ToExchangeRate();
        }

        return result;
    }
}

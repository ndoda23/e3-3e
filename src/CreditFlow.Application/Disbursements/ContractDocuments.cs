using CreditFlow.Application.Applications;
using CreditFlow.Application.Authorization;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Loans;

namespace CreditFlow.Application.Disbursements;

/// <summary>A party as printed on the contract.</summary>
public sealed record ContractParty(string Name, string PersonalNumber, PartyRole Role, string IdentityDocument, string Address);

/// <summary>One row of a printed repayment schedule; <see cref="Remaining"/> is the principal left after it.</summary>
public sealed record ScheduleRow(int Number, DateOnly DueDate, Money Principal, Money Interest, Money Fee, Money Total, Money Remaining);

/// <summary>Everything the contract and the schedule PDFs show.</summary>
public sealed record ContractDocumentData
{
    /// <summary><c>null</c> before signing: the number is assigned when the contract is signed.</summary>
    public required string? ContractNumber { get; init; }

    public required string ApplicationReference { get; init; }

    public required DateOnly IssuedOn { get; init; }

    public required string BranchName { get; init; }

    public required string ProductName { get; init; }

    public required int ProductVersion { get; init; }

    public required IReadOnlyList<ContractParty> Parties { get; init; }

    public required LoanTerms Terms { get; init; }

    public required Money UpfrontFee { get; init; }

    public required Money MonthlyFee { get; init; }

    public required decimal EffectiveAnnualRate { get; init; }

    public required decimal EarlyRepaymentPenaltyPercent { get; init; }

    public required IReadOnlyList<string> Conditions { get; init; }

    public required IReadOnlyList<string> Collateral { get; init; }

    /// <summary>The booked schedule of the loan, or one projected from today before disbursement.</summary>
    public required IReadOnlyList<ScheduleRow> Schedule { get; init; }

    public required bool ScheduleIsProjected { get; init; }
}

/// <summary>A generated file.</summary>
public sealed record RenderedDocument(string FileName, string ContentType, byte[] Content);

/// <summary>Turns the contract data into PDF files.</summary>
public interface IContractDocumentRenderer
{
    byte[] RenderContract(ContractDocumentData data);

    byte[] RenderSchedule(ContractDocumentData data);
}

/// <summary>
/// The printable contract and repayment schedule of an approved application (offer, signed contract or loan), for
/// users with <c>contracts.manage</c> or <c>loans.view</c> who may see the application. Before disbursement the
/// schedule is projected from today; afterwards it is the loan's current schedule.
/// </summary>
public sealed class ContractDocumentService(
    CurrentAccessProfile access,
    IApplicationQueries applicationQueries,
    IClientRepository clients,
    ILoanRepository loans,
    IContractDocumentRenderer renderer,
    TimeProvider clock)
{
    private static readonly HashSet<ApplicationStatus> Printable =
    [
        ApplicationStatus.Approved, ApplicationStatus.ConditionallyApproved, ApplicationStatus.ContractSigned,
        ApplicationStatus.ConditionsMet, ApplicationStatus.Disbursed, ApplicationStatus.Active, ApplicationStatus.Closed,
    ];

    /// <summary>The contract PDF, or <c>null</c> when it cannot be printed for the viewer.</summary>
    public async Task<RenderedDocument?> ContractAsync(LoanApplicationId id, CancellationToken cancellationToken) =>
        await DataAsync(id, cancellationToken) is { } data
            ? new RenderedDocument($"{FileStem(data)}.pdf", "application/pdf", renderer.RenderContract(data))
            : null;

    /// <summary>The repayment schedule PDF, or <c>null</c> when it cannot be printed for the viewer.</summary>
    public async Task<RenderedDocument?> ScheduleAsync(LoanApplicationId id, CancellationToken cancellationToken) =>
        await DataAsync(id, cancellationToken) is { } data
            ? new RenderedDocument($"{FileStem(data)}-schedule.pdf", "application/pdf", renderer.RenderSchedule(data))
            : null;

    /// <summary>Whether the application's status has printable documents.</summary>
    public static bool IsPrintable(ApplicationStatus status) => Printable.Contains(status);

    private static string FileStem(ContractDocumentData data) => data.ContractNumber ?? $"offer-{data.ApplicationReference}";

    private async Task<ContractDocumentData?> DataAsync(LoanApplicationId id, CancellationToken cancellationToken)
    {
        var profile = await access.GetAsync(cancellationToken);
        if (profile is null
            || !(profile.HasPermission(Permissions.Contracts.Manage) || profile.HasPermission(Permissions.Loans.View)))
        {
            return null;
        }

        var application = await applicationQueries.GetDetailsAsync(id, ApplicationAccessFilter.For(profile), cancellationToken);
        if (application is null || !Printable.Contains(application.Summary.Status) || application.Snapshot is not { } snapshot)
        {
            return null;
        }

        var people = await clients.GetManyAsync(application.Parties.Select(p => p.ClientId), cancellationToken);
        var terms = application.Terms;
        var upfrontFee = terms.Amount.Percent(snapshot.Terms.UpfrontFeePercent);
        var monthlyFee = terms.Amount.Percent(snapshot.Terms.MonthlyFeePercent);
        var loan = await loans.GetByApplicationAsync(id, cancellationToken);

        IReadOnlyList<ScheduleRow> schedule;
        decimal effectiveRate;
        if (loan is not null)
        {
            schedule = Rows(loan.Installments.OrderBy(i => i.Number)
                .Select(i => (i.Number, i.DueDate, i.Principal, i.Interest, i.Fee)).ToList(), terms.Amount.Currency);
            effectiveRate = loan.EffectiveAnnualRate;
        }
        else
        {
            var projected = ScheduleCalculator.Build(
                terms.Amount, terms.AnnualRate, terms.TermMonths, terms.ScheduleType, BankTime.DateOf(clock.GetUtcNow()), monthlyFee);
            schedule = Rows(projected.Installments.Select(i => (i.Number, i.DueDate, i.Principal, i.Interest, i.Fee)).ToList(), terms.Amount.Currency);
            effectiveRate = EffectiveRate.AnnualPercent(terms.Amount, upfrontFee, projected);
        }

        return new ContractDocumentData
        {
            ContractNumber = application.ContractNumber,
            ApplicationReference = application.Summary.Reference,
            IssuedOn = BankTime.DateOf(clock.GetUtcNow()),
            BranchName = application.Summary.BranchName,
            ProductName = snapshot.Name,
            ProductVersion = snapshot.Version,
            Parties = application.Parties.Select(p =>
            {
                var client = people[p.ClientId];
                var document = client.IdentityDocument;
                return new ContractParty(
                    p.Name, p.PersonalNumber, p.Role, $"{document.Type} {document.Number}, valid until {document.ExpiresOn:yyyy-MM-dd}",
                    client.Address);
            }).ToList(),
            Terms = terms,
            UpfrontFee = upfrontFee,
            MonthlyFee = monthlyFee,
            EffectiveAnnualRate = effectiveRate,
            EarlyRepaymentPenaltyPercent = snapshot.Terms.EarlyRepaymentPenaltyPercent,
            Conditions = application.Conditions.Select(c => c.Description).ToList(),
            Collateral = application.Collaterals
                .Select(c => $"{c.Description}, market value {c.MarketValue}, valued on {c.ValuationDate:yyyy-MM-dd}").ToList(),
            Schedule = schedule,
            ScheduleIsProjected = loan is null,
        };
    }

    /// <summary>Remaining principal after each installment = the principal of the installments after it.</summary>
    private static List<ScheduleRow> Rows(
        List<(int Number, DateOnly DueDate, Money Principal, Money Interest, Money Fee)> installments, Currency currency)
    {
        var remaining = installments.Aggregate(Money.Zero(currency), (sum, i) => sum + i.Principal);
        var rows = new List<ScheduleRow>(installments.Count);
        foreach (var i in installments)
        {
            remaining -= i.Principal;
            rows.Add(new ScheduleRow(i.Number, i.DueDate, i.Principal, i.Interest, i.Fee, i.Principal + i.Interest + i.Fee, remaining));
        }

        return rows;
    }
}

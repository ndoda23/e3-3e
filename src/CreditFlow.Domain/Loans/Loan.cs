using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.Loans;

public readonly record struct LoanId(Guid Value)
{
    public static LoanId New() => new(IdGenerator.NewGuid());

    public override string ToString() => Value.ToString();
}

public enum LoanStatus
{
    Active = 1,
    Closed = 2,
}

public enum RepaymentKind
{
    /// <summary>The next installment of the schedule, paid in full.</summary>
    Installment = 1,

    /// <summary>Part of the principal before it is due: the remaining installments are recalculated.</summary>
    PartialEarly = 2,

    /// <summary>The whole outstanding principal: the loan is closed.</summary>
    FullEarly = 3,
}

/// <summary>One installment of the loan's current schedule.</summary>
public sealed class LoanInstallment
{
    private LoanInstallment()
    {
    }

    public Guid Id { get; private set; }

    public int Number { get; private set; }

    public DateOnly DueDate { get; private set; }

    public Money Principal { get; private set; }

    public Money Interest { get; private set; }

    public Money Fee { get; private set; }

    public bool IsPaid { get; private set; }

    public DateOnly? PaidOn { get; private set; }

    public Money Total => Principal + Interest + Fee;

    internal static LoanInstallment From(Installment installment) => new()
    {
        Id = IdGenerator.NewGuid(),
        Number = installment.Number,
        DueDate = installment.DueDate,
        Principal = installment.Principal,
        Interest = installment.Interest,
        Fee = installment.Fee,
    };

    internal void MarkPaid(DateOnly on)
    {
        IsPaid = true;
        PaidOn = on;
    }
}

/// <summary>A recorded repayment and how it was split.</summary>
public sealed class LoanRepayment
{
    private LoanRepayment()
    {
    }

    public Guid Id { get; private set; }

    public RepaymentKind Kind { get; private set; }

    public DateOnly PaidOn { get; private set; }

    public Money Principal { get; private set; }

    public Money Interest { get; private set; }

    public Money Fee { get; private set; }

    public Money Penalty { get; private set; }

    public UserId RecordedBy { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public Money Total => Principal + Interest + Fee + Penalty;

    internal static LoanRepayment Create(
        RepaymentKind kind, DateOnly paidOn, Money principal, Money interest, Money fee, Money penalty, UserId by,
        DateTimeOffset at) => new()
    {
        Id = IdGenerator.NewGuid(),
        Kind = kind,
        PaidOn = paidOn,
        Principal = principal,
        Interest = interest,
        Fee = fee,
        Penalty = penalty,
        RecordedBy = by,
        RecordedAt = at,
    };
}

/// <summary>
/// A disbursed loan: the contract terms, the current repayment schedule and the repayments. Booked from the application
/// once core banking confirmed the payout. Repayments are recorded by an operator (there is no collection system):
/// the next installment, or an early repayment that either recalculates the remaining installments on the lower
/// balance (same remaining term) or repays everything and closes the loan.
/// </summary>
public sealed class Loan : IHasDomainEvents
{
    private readonly List<LoanInstallment> _installments = [];
    private readonly List<LoanRepayment> _repayments = [];
    private readonly List<IDomainEvent> _events = [];

    private Loan()
    {
    }

    public LoanId Id { get; private set; }

    public LoanApplicationId ApplicationId { get; private set; }

    public string ContractNumber { get; private set; } = string.Empty;

    public ClientId BorrowerId { get; private set; }

    public LoanProductId ProductId { get; private set; }

    public Money Principal { get; private set; }

    public decimal AnnualRate { get; private set; }

    public ScheduleType ScheduleType { get; private set; }

    public Money MonthlyFee { get; private set; }

    public decimal EarlyRepaymentPenaltyPercent { get; private set; }

    /// <summary>Effective annual rate including the fees (percent), at booking.</summary>
    public decimal EffectiveAnnualRate { get; private set; }

    public DateOnly DisbursedOn { get; private set; }

    public LoanStatus Status { get; private set; }

    public Money OutstandingPrincipal { get; private set; }

    public DateTimeOffset BookedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyList<LoanInstallment> Installments => _installments;

    public IReadOnlyList<LoanRepayment> Repayments => _repayments;

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    /// <summary>The first unpaid installment, or <c>null</c> when nothing is due any more.</summary>
    public LoanInstallment? NextInstallment => _installments.Where(i => !i.IsPaid).MinBy(i => i.Number);

    /// <summary>Books the loan of a disbursed application with its schedule from the disbursement day.</summary>
    public static Result<Loan> Book(LoanApplication application, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (application.Status != ApplicationStatus.Disbursed || application.DisbursedAt is not { } disbursedAt
            || application.ContractNumber is not { } contractNumber || application.Snapshot is not { } snapshot)
        {
            return Result.Failure<Loan>(ApplicationErrors.WrongStatus(application.Status));
        }

        var terms = application.Terms;
        var disbursedOn = BankTime.DateOf(disbursedAt);
        var monthlyFee = terms.Amount.Percent(snapshot.Terms.MonthlyFeePercent);
        var schedule = ScheduleCalculator.Build(
            terms.Amount, terms.AnnualRate, terms.TermMonths, terms.ScheduleType, disbursedOn, monthlyFee);

        var loan = new Loan
        {
            Id = LoanId.New(),
            ApplicationId = application.Id,
            ContractNumber = contractNumber,
            BorrowerId = application.BorrowerId,
            ProductId = application.ProductId,
            Principal = terms.Amount,
            AnnualRate = terms.AnnualRate,
            ScheduleType = terms.ScheduleType,
            MonthlyFee = monthlyFee,
            EarlyRepaymentPenaltyPercent = snapshot.Terms.EarlyRepaymentPenaltyPercent,
            EffectiveAnnualRate = EffectiveRate.AnnualPercent(terms.Amount, terms.Amount.Percent(snapshot.Terms.UpfrontFeePercent), schedule),
            DisbursedOn = disbursedOn,
            Status = LoanStatus.Active,
            OutstandingPrincipal = terms.Amount,
            BookedAt = at,
        };
        loan._installments.AddRange(schedule.Installments.Select(LoanInstallment.From));
        return Result.Success(loan);
    }

    /// <summary>Records the payment of the next installment in full.</summary>
    public Result PayInstallment(UserId by, DateOnly paidOn, DateTimeOffset at)
    {
        if (Status != LoanStatus.Active || NextInstallment is not { } installment)
        {
            return Result.Failure(LoanErrors.Closed);
        }

        installment.MarkPaid(paidOn);
        OutstandingPrincipal -= installment.Principal;
        Record(LoanRepayment.Create(
            RepaymentKind.Installment, paidOn, installment.Principal, installment.Interest, installment.Fee,
            Money.Zero(Principal.Currency), by, at));
        CloseIfRepaid(at);
        return Result.Success();
    }

    /// <summary>What repaying <paramref name="principal"/> early (or everything, when <c>null</c>) costs on the day.</summary>
    public Result<EarlyRepaymentQuote> QuoteEarlyRepayment(decimal? principal, DateOnly payDate)
    {
        if (Status != LoanStatus.Active)
        {
            return Result.Failure<EarlyRepaymentQuote>(LoanErrors.Closed);
        }

        if (_installments.Any(i => !i.IsPaid && i.DueDate < payDate))
        {
            return Result.Failure<EarlyRepaymentQuote>(LoanErrors.InstallmentOverdue);
        }

        var prepaid = Money.Of(principal ?? OutstandingPrincipal.Amount, Principal.Currency);
        if (!prepaid.IsPositive || prepaid > OutstandingPrincipal)
        {
            return Result.Failure<EarlyRepaymentQuote>(LoanErrors.InvalidPrepayment);
        }

        return Result.Success(EarlyRepaymentCalculator.Quote(
            OutstandingPrincipal, prepaid, AnnualRate, LastDueDate, payDate, EarlyRepaymentPenaltyPercent));
    }

    /// <summary>
    /// Early repayment of <paramref name="principal"/> (everything when <c>null</c>): interest accrued since the last due
    /// date and the product's penalty are paid on the day. A partial repayment keeps the remaining term and lowers the
    /// remaining installments; a full one closes the loan.
    /// </summary>
    public Result RepayEarly(decimal? principal, UserId by, DateOnly payDate, DateTimeOffset at)
    {
        var quote = QuoteEarlyRepayment(principal, payDate);
        if (quote.IsFailure)
        {
            return Result.Failure(quote.Error!);
        }

        var unpaid = _installments.Where(i => !i.IsPaid).OrderBy(i => i.Number).ToList();
        var lastDueDate = LastDueDate;
        OutstandingPrincipal -= quote.Value.PrincipalPaid;
        foreach (var installment in unpaid)
        {
            _installments.Remove(installment);
        }

        var full = OutstandingPrincipal.IsZero;
        if (!full)
        {
            var schedule = EarlyRepaymentCalculator.Recalculate(
                OutstandingPrincipal, AnnualRate, unpaid.Count, ScheduleType, lastDueDate, MonthlyFee, unpaid[0].Number);
            _installments.AddRange(schedule.Installments.Select(LoanInstallment.From));
        }

        Record(LoanRepayment.Create(
            full ? RepaymentKind.FullEarly : RepaymentKind.PartialEarly, payDate, quote.Value.PrincipalPaid,
            quote.Value.AccruedInterest, Money.Zero(Principal.Currency), quote.Value.Penalty, by, at));
        CloseIfRepaid(at);
        return Result.Success();
    }

    public void ClearDomainEvents() => _events.Clear();

    /// <summary>Interest runs from the last due date that was paid (or from the disbursement).</summary>
    private DateOnly LastDueDate =>
        _installments.Where(i => i.IsPaid).Select(i => (DateOnly?)i.DueDate).Max() ?? DisbursedOn;

    private void Record(LoanRepayment repayment)
    {
        _repayments.Add(repayment);
        _events.Add(new LoanRepaymentRecorded(Id, ApplicationId, repayment.Kind, repayment.Total, repayment.RecordedAt));
    }

    private void CloseIfRepaid(DateTimeOffset at)
    {
        if (OutstandingPrincipal.IsZero && Status == LoanStatus.Active)
        {
            Status = LoanStatus.Closed;
            ClosedAt = at;
            _events.Add(new LoanClosed(Id, ApplicationId, at));
        }
    }
}

/// <summary>A repayment was recorded (audited).</summary>
public sealed record LoanRepaymentRecorded(
    LoanId LoanId, LoanApplicationId ApplicationId, RepaymentKind Kind, Money Amount, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>The loan was repaid in full; the application is closed too.</summary>
public sealed record LoanClosed(LoanId LoanId, LoanApplicationId ApplicationId, DateTimeOffset OccurredAt) : IDomainEvent;

public static class LoanErrors
{
    public static readonly Error NotFound = Error.NotFound("Loan.NotFound", "The loan was not found.");

    public static readonly Error Closed = Error.Conflict("Loan.Closed", "The loan is repaid; nothing is due.");

    public static readonly Error InstallmentOverdue = Error.Conflict(
        "Loan.InstallmentOverdue", "Record the overdue installments before an early repayment.");

    public static readonly Error InvalidPrepayment = Error.Validation(
        "Loan.InvalidPrepayment", "The early repayment must be more than zero and at most the outstanding principal.", "Amount");
}

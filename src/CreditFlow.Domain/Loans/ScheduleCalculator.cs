using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;

namespace CreditFlow.Domain.Loans;

/// <summary>One installment of a repayment schedule.</summary>
public sealed record Installment(
    int Number, DateOnly DueDate, Money Principal, Money Interest, Money Fee, Money RemainingPrincipal)
{
    public Money Total => Principal + Interest + Fee;
}

/// <summary>A repayment schedule. The principal parts always add up exactly to the principal.</summary>
public sealed record RepaymentSchedule(Money Principal, IReadOnlyList<Installment> Installments)
{
    public Money TotalInterest => Sum(i => i.Interest);

    public Money TotalFees => Sum(i => i.Fee);

    public Money TotalPayable => Sum(i => i.Total);

    /// <summary>The largest installment (the first one for a differentiated schedule); used by the PTI rule.</summary>
    public Money LargestInstallment =>
        Installments.Select(i => i.Total).Aggregate((a, b) => a.Amount >= b.Amount ? a : b);

    private Money Sum(Func<Installment, Money> part) =>
        Installments.Select(part).Aggregate(Money.Zero(Principal.Currency), (a, b) => a + b);
}

/// <summary>
/// Builds repayment schedules (monthly installments, interest = annual rate / 12 on the remaining principal).
/// Every amount uses <see cref="Money.Round"/>; the rounding difference goes into the last installment, so the
/// principal parts add up exactly to the loan amount.
/// </summary>
public static class ScheduleCalculator
{
    /// <param name="principal">The loan amount.</param>
    /// <param name="annualRatePercent">Annual nominal rate, percent.</param>
    /// <param name="months">Number of installments.</param>
    /// <param name="type">Annuity (equal installments) or differentiated (equal principal parts).</param>
    /// <param name="startDate">Disbursement date; installment k is due k months later.</param>
    /// <param name="monthlyFee">Fee added to every installment.</param>
    /// <param name="firstNumber">Number of the first installment (after an early repayment, the next number).</param>
    public static RepaymentSchedule Build(
        Money principal, decimal annualRatePercent, int months, ScheduleType type, DateOnly startDate, Money monthlyFee,
        int firstNumber = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(annualRatePercent);
        if (!principal.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(principal), principal, "The principal must be positive.");
        }

        var monthlyRate = annualRatePercent / 100m / 12m;
        var level = type == ScheduleType.Annuity
            ? AnnuityPayment(principal.Amount, monthlyRate, months)
            : Money.Round(principal.Amount / months);

        var installments = new List<Installment>(months);
        var remaining = principal.Amount;
        for (var k = 1; k <= months; k++)
        {
            var interest = Money.Round(remaining * monthlyRate);
            var principalPart = k == months
                ? remaining // the last installment absorbs every rounding difference
                : Math.Min(remaining, type == ScheduleType.Annuity ? level - interest : level);
            remaining -= principalPart;

            installments.Add(new Installment(
                firstNumber + k - 1,
                startDate.AddMonths(k),
                Money.Of(principalPart, principal.Currency),
                Money.Of(interest, principal.Currency),
                monthlyFee,
                Money.Of(remaining, principal.Currency)));
        }

        return new RepaymentSchedule(principal, installments);
    }

    /// <summary>The level annuity payment, rounded: P·r / (1 − (1 + r)^−n), or P / n at a zero rate.</summary>
    public static decimal AnnuityPayment(decimal principal, decimal monthlyRate, int months)
    {
        if (monthlyRate == 0m)
        {
            return Money.Round(principal / months);
        }

        var discount = 1m;
        for (var k = 0; k < months; k++)
        {
            discount /= 1m + monthlyRate;
        }

        return Money.Round(principal * monthlyRate / (1m - discount));
    }
}

/// <summary>
/// Effective annual interest rate including fees: the monthly internal rate of return of the client's cash flows
/// (amount received = principal − upfront fee; payments = installments with fees), annualised as (1 + i)^12 − 1.
/// Solved by bisection in <c>decimal</c>.
/// </summary>
public static class EffectiveRate
{
    private const int Iterations = 200;

    /// <returns>Percent, rounded to 2 decimals.</returns>
    public static decimal AnnualPercent(Money principal, Money upfrontFee, RepaymentSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var received = principal.Amount - upfrontFee.Amount;
        var payments = schedule.Installments.Select(i => i.Total.Amount).ToList();
        if (received <= 0m || payments.Sum() <= received)
        {
            return 0m;
        }

        // Net present value falls as the rate rises; find the monthly rate where it is zero.
        decimal low = 0m, high = 1m;
        for (var n = 0; n < Iterations; n++)
        {
            var mid = (low + high) / 2m;
            if (PresentValue(payments, mid) > received)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        var monthly = (low + high) / 2m;
        var annual = 1m;
        for (var k = 0; k < 12; k++)
        {
            annual *= 1m + monthly;
        }

        return Money.Round((annual - 1m) * 100m);
    }

    private static decimal PresentValue(IReadOnlyList<decimal> payments, decimal monthlyRate)
    {
        decimal value = 0m, discount = 1m;
        foreach (var payment in payments)
        {
            discount /= 1m + monthlyRate;
            value += payment * discount;
        }

        return value;
    }
}

/// <summary>What an early repayment costs on a given day.</summary>
/// <param name="AccruedInterest">Interest since the last due date (actual / 365 on the outstanding principal).</param>
/// <param name="Penalty">The product's early-repayment penalty on the prepaid principal.</param>
/// <param name="PrincipalPaid">The principal repaid early.</param>
public sealed record EarlyRepaymentQuote(Money PrincipalPaid, Money AccruedInterest, Money Penalty)
{
    public Money Total => PrincipalPaid + AccruedInterest + Penalty;
}

/// <summary>
/// Early repayment. Full: the outstanding principal + accrued interest + penalty closes the loan. Partial: the
/// prepaid principal reduces the outstanding principal, the remaining term stays the same and the remaining
/// installments are recalculated (lower installments); accrued interest and the penalty are paid on the day.
/// </summary>
public static class EarlyRepaymentCalculator
{
    public const int DayCountBasis = 365;

    public static EarlyRepaymentQuote Quote(
        Money outstanding, Money prepaidPrincipal, decimal annualRatePercent, DateOnly lastDueDate, DateOnly payDate,
        decimal penaltyPercent)
    {
        if (prepaidPrincipal.Currency != outstanding.Currency || !prepaidPrincipal.IsPositive || prepaidPrincipal > outstanding)
        {
            throw new ArgumentOutOfRangeException(nameof(prepaidPrincipal), prepaidPrincipal, "Prepay between zero and the outstanding principal.");
        }

        var days = Math.Max(0, payDate.DayNumber - lastDueDate.DayNumber);
        var accrued = outstanding.Multiply(annualRatePercent / 100m * days / DayCountBasis);
        return new EarlyRepaymentQuote(prepaidPrincipal, accrued, prepaidPrincipal.Percent(penaltyPercent));
    }

    /// <summary>The schedule after a partial prepayment: same remaining installments, recalculated on the new balance.</summary>
    public static RepaymentSchedule Recalculate(
        Money newOutstanding, decimal annualRatePercent, int remainingInstallments, ScheduleType type,
        DateOnly lastDueDate, Money monthlyFee, int nextNumber) =>
        ScheduleCalculator.Build(newOutstanding, annualRatePercent, remainingInstallments, type, lastDueDate, monthlyFee, nextNumber);
}

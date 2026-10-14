using CreditFlow.Domain.Common;
using CreditFlow.Domain.Loans;
using CreditFlow.Domain.Products;

namespace CreditFlow.Domain.UnitTests.Loans;

public sealed class ScheduleCalculatorTests
{
    private static readonly DateOnly Start = new(2026, 1, 31);
    private static readonly Money NoFee = Money.Zero(Currency.GEL);

    private static Money Gel(decimal amount) => Money.Of(amount, Currency.GEL);

    [Fact]
    public void Annuity_installments_are_level_and_the_last_absorbs_the_rounding()
    {
        var schedule = ScheduleCalculator.Build(Gel(10_000m), 12m, 12, ScheduleType.Annuity, Start, NoFee);

        Assert.Equal(12, schedule.Installments.Count);
        Assert.All(schedule.Installments.SkipLast(1), i => Assert.Equal(888.49m, i.Total.Amount));
        Assert.Equal(100.00m, schedule.Installments[0].Interest.Amount);
        Assert.Equal(788.49m, schedule.Installments[0].Principal.Amount);
        Assert.Equal(10_000m, schedule.Installments.Sum(i => i.Principal.Amount));
        Assert.Equal(0m, schedule.Installments[^1].RemainingPrincipal.Amount);
        Assert.InRange(schedule.Installments[^1].Total.Amount, 888.40m, 888.60m);
    }

    [Fact]
    public void Differentiated_principal_parts_are_equal_except_the_last()
    {
        var schedule = ScheduleCalculator.Build(Gel(10_000m), 12m, 12, ScheduleType.Differentiated, Start, NoFee);

        Assert.All(schedule.Installments.SkipLast(1), i => Assert.Equal(833.33m, i.Principal.Amount));
        Assert.Equal(833.37m, schedule.Installments[^1].Principal.Amount);
        Assert.Equal(10_000m, schedule.Installments.Sum(i => i.Principal.Amount));
        Assert.Equal(100.00m, schedule.Installments[0].Interest.Amount);
        Assert.True(schedule.Installments[0].Total > schedule.Installments[^1].Total);
        Assert.Equal(schedule.Installments[0].Total, schedule.LargestInstallment);
    }

    [Theory]
    [InlineData(ScheduleType.Annuity)]
    [InlineData(ScheduleType.Differentiated)]
    public void A_zero_rate_splits_the_principal_and_the_last_takes_the_cent(ScheduleType type)
    {
        var schedule = ScheduleCalculator.Build(Gel(1_000m), 0m, 3, type, Start, NoFee);

        Assert.Equal([333.33m, 333.33m, 333.34m], schedule.Installments.Select(i => i.Principal.Amount));
        Assert.Equal(0m, schedule.TotalInterest.Amount);
    }

    [Theory]
    [InlineData(ScheduleType.Annuity)]
    [InlineData(ScheduleType.Differentiated)]
    public void A_one_month_loan_is_repaid_in_one_installment(ScheduleType type)
    {
        var installment = Assert.Single(ScheduleCalculator.Build(Gel(5_000m), 24m, 1, type, Start, NoFee).Installments);

        Assert.Equal(5_000m, installment.Principal.Amount);
        Assert.Equal(100m, installment.Interest.Amount);
    }

    [Theory]
    [InlineData(1234.56, 7.9, 360, ScheduleType.Annuity)]
    [InlineData(99_999.99, 19.99, 37, ScheduleType.Annuity)]
    [InlineData(500, 35, 7, ScheduleType.Differentiated)]
    [InlineData(750_000, 11.5, 240, ScheduleType.Differentiated)]
    public void The_principal_parts_add_up_exactly_to_the_amount(double amount, double rate, int months, ScheduleType type)
    {
        var principal = Gel((decimal)amount);
        var schedule = ScheduleCalculator.Build(principal, (decimal)rate, months, type, Start, NoFee);

        Assert.Equal(principal.Amount, schedule.Installments.Sum(i => i.Principal.Amount));
        Assert.All(schedule.Installments, i => Assert.True(i.Principal.Amount >= 0m));
        Assert.All(schedule.Installments, i => Assert.Equal(Money.Round(i.Interest.Amount), i.Interest.Amount));
    }

    [Fact]
    public void Due_dates_are_monthly_from_the_start_and_month_ends_are_clamped()
    {
        var schedule = ScheduleCalculator.Build(Gel(1_000m), 10m, 3, ScheduleType.Annuity, Start, NoFee);

        Assert.Equal([new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30)],
            schedule.Installments.Select(i => i.DueDate));
    }

    [Fact]
    public void Monthly_fees_are_added_to_every_installment()
    {
        var schedule = ScheduleCalculator.Build(Gel(1_000m), 10m, 4, ScheduleType.Annuity, Start, Gel(5m));

        Assert.All(schedule.Installments, i => Assert.Equal(5m, i.Fee.Amount));
        Assert.Equal(20m, schedule.TotalFees.Amount);
    }

    // ----- Effective rate -----

    [Fact]
    public void Without_fees_the_effective_rate_is_the_compounded_nominal_rate()
    {
        var schedule = ScheduleCalculator.Build(Gel(10_000m), 12m, 12, ScheduleType.Annuity, Start, NoFee);

        Assert.Equal(12.68m, EffectiveRate.AnnualPercent(Gel(10_000m), NoFee, schedule));
    }

    [Fact]
    public void Fees_raise_the_effective_rate()
    {
        var principal = Gel(10_000m);
        var plain = ScheduleCalculator.Build(principal, 12m, 12, ScheduleType.Annuity, Start, NoFee);
        var withMonthlyFee = ScheduleCalculator.Build(principal, 12m, 12, ScheduleType.Annuity, Start, Gel(10m));

        var upfront = EffectiveRate.AnnualPercent(principal, Gel(100m), plain);
        var monthly = EffectiveRate.AnnualPercent(principal, NoFee, withMonthlyFee);

        Assert.InRange(upfront, 14.5m, 14.9m);
        Assert.InRange(monthly, 15.0m, 15.4m);
    }

    [Fact]
    public void A_free_loan_has_a_zero_effective_rate()
    {
        var schedule = ScheduleCalculator.Build(Gel(1_200m), 0m, 12, ScheduleType.Annuity, Start, NoFee);

        Assert.Equal(0m, EffectiveRate.AnnualPercent(Gel(1_200m), NoFee, schedule));
    }

    // ----- Early repayment -----

    [Fact]
    public void Early_repayment_charges_accrued_interest_and_the_penalty()
    {
        var quote = EarlyRepaymentCalculator.Quote(
            Gel(10_000m), Gel(4_000m), 18.25m, lastDueDate: Start, payDate: Start.AddDays(10), penaltyPercent: 2m);

        Assert.Equal(50m, quote.AccruedInterest.Amount); // 10,000 × 18.25 % × 10 / 365
        Assert.Equal(80m, quote.Penalty.Amount);
        Assert.Equal(4_130m, quote.Total.Amount);
    }

    [Fact]
    public void A_partial_prepayment_keeps_the_term_and_lowers_the_installments()
    {
        var before = ScheduleCalculator.Build(Gel(10_000m), 12m, 12, ScheduleType.Annuity, Start, NoFee);
        var afterThree = before.Installments[2];

        var after = EarlyRepaymentCalculator.Recalculate(
            afterThree.RemainingPrincipal - Gel(2_000m), 12m, 9, ScheduleType.Annuity, afterThree.DueDate, NoFee, nextNumber: 4);

        Assert.Equal(9, after.Installments.Count);
        Assert.Equal(4, after.Installments[0].Number);
        Assert.Equal(afterThree.DueDate.AddMonths(1), after.Installments[0].DueDate);
        Assert.True(after.Installments[0].Total < before.Installments[3].Total);
        Assert.Equal((afterThree.RemainingPrincipal - Gel(2_000m)).Amount, after.Installments.Sum(i => i.Principal.Amount));
    }

    [Fact]
    public void Prepaying_more_than_outstanding_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EarlyRepaymentCalculator.Quote(Gel(100m), Gel(100.01m), 10m, Start, Start, 1m));
    }
}

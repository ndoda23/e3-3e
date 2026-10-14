using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Loans;
using CreditFlow.Domain.Users;
using CreditFlow.Domain.UnitTests.Applications;

namespace CreditFlow.Domain.UnitTests.Loans;

/// <summary>Booking, installments, early repayment (partial and full) and closing of a loan.</summary>
public sealed class LoanTests
{
    private static readonly UserId Operator = UserId.New();
    private readonly ApplicationFixture _fixture =
        new(ApplicationFixture.ConsumerTerms() with { EarlyRepaymentPenaltyPercent = 2m, UpfrontFeePercent = 1m });

    [Fact]
    public void A_loan_is_booked_from_a_disbursed_application_with_its_full_schedule()
    {
        var application = _fixture.In(ApplicationStatus.Disbursed);

        var loan = Loan.Book(application, ApplicationFixture.Now).Value;

        Assert.Equal(application.ContractNumber, loan.ContractNumber);
        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Equal(application.Terms.Amount, loan.Principal);
        Assert.Equal(loan.Principal, loan.OutstandingPrincipal);
        Assert.Equal(application.Terms.TermMonths, loan.Installments.Count);
        Assert.Equal(loan.Principal.Amount, loan.Installments.Sum(i => i.Principal.Amount));
        Assert.Equal(BankTime.DateOf(application.DisbursedAt!.Value).AddMonths(1), loan.Installments[0].DueDate);
        Assert.True(loan.EffectiveAnnualRate > loan.AnnualRate);
    }

    [Theory]
    [InlineData(ApplicationStatus.ConditionsMet)]
    [InlineData(ApplicationStatus.Active)]
    public void Only_a_disbursed_application_is_booked(ApplicationStatus status)
    {
        Assert.True(Loan.Book(_fixture.In(status), ApplicationFixture.Now).IsFailure);
    }

    [Fact]
    public void Paying_installments_lowers_the_outstanding_principal_and_the_last_one_closes_the_loan()
    {
        var loan = BookedLoan();
        var first = loan.NextInstallment!;

        Assert.True(loan.PayInstallment(Operator, first.DueDate, ApplicationFixture.Now).IsSuccess);

        Assert.True(first.IsPaid);
        Assert.Equal(loan.Principal - first.Principal, loan.OutstandingPrincipal);
        Assert.Equal(RepaymentKind.Installment, Assert.Single(loan.Repayments).Kind);

        while (loan.NextInstallment is { } next)
        {
            Assert.True(loan.PayInstallment(Operator, next.DueDate, ApplicationFixture.Now).IsSuccess);
        }

        Assert.Equal(LoanStatus.Closed, loan.Status);
        Assert.True(loan.OutstandingPrincipal.IsZero);
        Assert.Contains(loan.DomainEvents, e => e is LoanClosed);
        Assert.Equal(LoanErrors.Closed, loan.PayInstallment(Operator, ApplicationFixture.Today, ApplicationFixture.Now).Error);
    }

    [Fact]
    public void A_partial_early_repayment_keeps_the_term_and_lowers_the_installments()
    {
        var loan = BookedLoan();
        var payDay = loan.DisbursedOn.AddDays(10);
        var before = loan.Installments.Count;
        var installmentBefore = loan.Installments[0].Total;

        Assert.True(loan.RepayEarly(4_000m, Operator, payDay, ApplicationFixture.Now).IsSuccess);

        Assert.Equal(before, loan.Installments.Count);
        Assert.Equal(Money.Of(6_000m, Currency.GEL), loan.OutstandingPrincipal);
        Assert.Equal(6_000m, loan.Installments.Sum(i => i.Principal.Amount));
        Assert.True(loan.Installments[0].Total < installmentBefore);
        Assert.Equal([1, 2], loan.Installments.Take(2).Select(i => i.Number));
        Assert.Equal(loan.DisbursedOn.AddMonths(1), loan.Installments[0].DueDate);

        var repayment = Assert.Single(loan.Repayments);
        Assert.Equal(RepaymentKind.PartialEarly, repayment.Kind);
        Assert.Equal(Money.Of(4_000m * 0.02m, Currency.GEL), repayment.Penalty); // the product's 2 % penalty
        Assert.Equal(Money.Of(10_000m * 0.18m * 10 / 365, Currency.GEL), repayment.Interest);
    }

    [Fact]
    public void A_full_early_repayment_closes_the_loan()
    {
        var loan = BookedLoan();
        var first = loan.NextInstallment!;
        loan.PayInstallment(Operator, first.DueDate, ApplicationFixture.Now);

        var quote = loan.QuoteEarlyRepayment(null, first.DueDate.AddDays(5)).Value;
        Assert.True(loan.RepayEarly(null, Operator, first.DueDate.AddDays(5), ApplicationFixture.Now).IsSuccess);

        Assert.Equal(LoanStatus.Closed, loan.Status);
        Assert.Equal(quote.Total, loan.Repayments[^1].Total);
        Assert.Equal(RepaymentKind.FullEarly, loan.Repayments[^1].Kind);
        Assert.All(loan.Installments, i => Assert.True(i.IsPaid));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000.01)]
    public void An_early_repayment_must_be_positive_and_at_most_the_outstanding_principal(decimal amount)
    {
        var loan = BookedLoan();

        Assert.Equal(LoanErrors.InvalidPrepayment, loan.RepayEarly(amount, Operator, loan.DisbursedOn.AddDays(1), ApplicationFixture.Now).Error);
        Assert.Empty(loan.Repayments);
    }

    [Fact]
    public void Overdue_installments_come_before_an_early_repayment()
    {
        var loan = BookedLoan();

        var late = loan.RepayEarly(1_000m, Operator, loan.Installments[0].DueDate.AddDays(1), ApplicationFixture.Now);

        Assert.Equal(LoanErrors.InstallmentOverdue, late.Error);
    }

    private Loan BookedLoan()
    {
        var loan = Loan.Book(_fixture.In(ApplicationStatus.Disbursed), ApplicationFixture.Now).Value;
        Assert.Equal(Money.Of(10_000m, Currency.GEL), loan.Principal);
        return loan;
    }
}

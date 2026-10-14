using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Users;

namespace CreditFlow.Domain.UnitTests.Loans;

/// <summary>The payout's lifecycle: one key per application, Pending until core banking answers, retry after a rejection.</summary>
public sealed class DisbursementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
    private static readonly UserId Operator = UserId.New();
    private static readonly Money Amount = Money.Of(9_900m, Currency.GEL);

    [Fact]
    public void The_key_is_the_same_for_every_attempt_of_an_application()
    {
        var applicationId = LoanApplicationId.New();

        var first = Disbursement.Request(applicationId, Amount, Operator, Now);

        Assert.Equal(Disbursement.KeyFor(applicationId), first.IdempotencyKey);
        Assert.Equal(Disbursement.KeyFor(applicationId), Disbursement.KeyFor(new LoanApplicationId(applicationId.Value)));
        Assert.NotEqual(Disbursement.KeyFor(applicationId), Disbursement.KeyFor(LoanApplicationId.New()));
        Assert.Equal(DisbursementStatus.Pending, first.Status);
        Assert.IsType<DisbursementRequested>(Assert.Single(first.DomainEvents));
    }

    [Fact]
    public void Success_is_recorded_once_and_a_repeated_answer_with_the_same_transaction_is_harmless()
    {
        var payout = Disbursement.Request(LoanApplicationId.New(), Amount, Operator, Now);

        Assert.True(payout.Succeed("TX1", Now).IsSuccess);
        Assert.True(payout.Succeed("TX1", Now).IsSuccess);

        Assert.Equal(DisbursementStatus.Succeeded, payout.Status);
        Assert.Equal("TX1", payout.TransactionId);
        Assert.Single(payout.DomainEvents, e => e is DisbursementSucceeded);
        Assert.Equal(DisbursementErrors.DifferentTransaction, payout.Succeed("TX2", Now).Error);
    }

    [Fact]
    public void A_rejected_payout_is_retried_with_the_same_key()
    {
        var payout = Disbursement.Request(LoanApplicationId.New(), Amount, Operator, Now);
        var key = payout.IdempotencyKey;

        Assert.True(payout.Fail("Account blocked.", Now).IsSuccess);
        Assert.Equal(DisbursementStatus.Failed, payout.Status);
        Assert.True(payout.Retry(Operator, Now.AddMinutes(5)).IsSuccess);

        Assert.Equal(DisbursementStatus.Pending, payout.Status);
        Assert.Equal(key, payout.IdempotencyKey);
        Assert.Equal(2, payout.Attempts);
    }

    [Fact]
    public void A_timeout_keeps_the_payout_pending()
    {
        var payout = Disbursement.Request(LoanApplicationId.New(), Amount, Operator, Now);

        Assert.True(payout.TimedOut(Now).IsSuccess);

        Assert.Equal(DisbursementStatus.Pending, payout.Status);
        Assert.Contains(payout.DomainEvents, e => e is DisbursementTimedOut);
        Assert.True(payout.Succeed("TX1", Now).IsSuccess);
    }

    [Fact]
    public void Only_a_failed_payout_can_be_retried_and_only_a_pending_one_can_fail()
    {
        var payout = Disbursement.Request(LoanApplicationId.New(), Amount, Operator, Now);

        Assert.True(payout.Retry(Operator, Now).IsFailure);
        payout.Succeed("TX1", Now);
        Assert.True(payout.Fail("Too late.", Now).IsFailure);
        Assert.True(payout.TimedOut(Now).IsFailure);
    }

    [Fact]
    public void A_payout_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Disbursement.Request(LoanApplicationId.New(), Money.Zero(Currency.GEL), Operator, Now));
    }
}

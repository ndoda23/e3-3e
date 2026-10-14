using CreditFlow.Domain.Applications;
using S = CreditFlow.Domain.Applications.ApplicationStatus;

namespace CreditFlow.Domain.UnitTests.Applications;

/// <summary>
/// Every pair of statuses (19 × 19): the allowed ones exactly as in the task (PLAN.md §P.3.2, written out here
/// independently of the implementation), every other one impossible.
/// </summary>
public sealed class StateMachineTests
{
    private static readonly S[] BeforeDisbursement =
    [
        S.Draft, S.Submitted, S.PreCheck, S.ComplianceReview, S.DocumentsPending, S.Scoring, S.Underwriting,
        S.CreditCommittee, S.CounterOffered, S.Approved, S.ConditionallyApproved, S.ContractSigned, S.ConditionsMet,
    ];

    private static readonly HashSet<(S From, S To)> Allowed =
    [
        (S.Draft, S.Submitted),
        (S.Submitted, S.PreCheck),
        (S.PreCheck, S.ComplianceReview), (S.PreCheck, S.Rejected), (S.PreCheck, S.DocumentsPending),
        (S.ComplianceReview, S.DocumentsPending), (S.ComplianceReview, S.Rejected),
        (S.DocumentsPending, S.Scoring),
        (S.Scoring, S.Rejected), (S.Scoring, S.Underwriting), (S.Scoring, S.CreditCommittee),
        (S.Underwriting, S.Approved), (S.Underwriting, S.ConditionallyApproved), (S.Underwriting, S.Rejected),
        (S.Underwriting, S.CounterOffered),
        (S.CreditCommittee, S.Approved), (S.CreditCommittee, S.ConditionallyApproved), (S.CreditCommittee, S.Rejected),
        (S.CounterOffered, S.Scoring), (S.CounterOffered, S.Expired),
        (S.Approved, S.ContractSigned), (S.Approved, S.Expired),
        (S.ConditionallyApproved, S.ContractSigned), (S.ConditionallyApproved, S.Expired),
        (S.ContractSigned, S.ConditionsMet),
        (S.ConditionsMet, S.Disbursed),
        (S.Disbursed, S.Active),
        (S.Active, S.Closed),
        .. BeforeDisbursement.Select(s => (s, S.Cancelled)),
    ];

    public static TheoryData<S, S> AllPairs()
    {
        var data = new TheoryData<S, S>();
        foreach (var from in Enum.GetValues<S>())
        {
            foreach (var to in Enum.GetValues<S>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Only_the_transitions_of_the_task_are_possible(S from, S to)
    {
        Assert.Equal(Allowed.Contains((from, to)), ApplicationStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void There_are_nineteen_statuses()
    {
        Assert.Equal(19, Enum.GetValues<S>().Length);
    }

    [Theory]
    [InlineData(S.Closed)]
    [InlineData(S.Rejected)]
    [InlineData(S.Expired)]
    [InlineData(S.Cancelled)]
    public void Terminal_statuses_lead_nowhere(S terminal)
    {
        Assert.True(ApplicationStateMachine.IsTerminal(terminal));
        Assert.All(Enum.GetValues<S>(), to => Assert.False(ApplicationStateMachine.CanTransition(terminal, to)));
    }

    [Fact]
    public void Open_applications_are_those_before_disbursement()
    {
        Assert.Equal(BeforeDisbursement.Order(), ApplicationStateMachine.Open.Order());
    }
}

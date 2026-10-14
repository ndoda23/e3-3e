using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Approval;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;

namespace CreditFlow.Application.Applications;

/// <summary>
/// Decisions on applications: compliance review, underwriting (approve, reject, counter-offer) and committee votes,
/// plus the decision makers' inboxes. The approval authority comes from permissions; four-eyes and every other rule
/// is enforced by <see cref="LoanApplication"/> (and the database). Two simultaneous decisions on one application
/// cannot both succeed (<c>rowversion</c>); a committee vote that loses such a race is retried once with fresh data,
/// so no vote is lost.
/// </summary>
public sealed class DecisionService(
    CurrentAccessProfile access,
    ILoanApplicationRepository applications,
    IApprovalThresholdsRepository thresholds,
    IApplicationQueries queries,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<PagedResult<ApplicationListItem>> InboxAsync(
        Inbox inbox, int page, CancellationToken cancellationToken, InboxFilter? filter = null)
    {
        var profile = await access.GetAsync(cancellationToken);
        if (profile is null || !profile.HasPermission(PermissionFor(inbox)))
        {
            return new PagedResult<ApplicationListItem>([], 0, 1, Paging.DefaultPageSize);
        }

        return await queries.InboxAsync(
            inbox, ApplicationAccessFilter.For(profile), CurrentAccessProfile.AuthorityOf(profile)?.MaxTier,
            Paging.NormalizePage(page), Paging.DefaultPageSize, inbox == Inbox.Underwriting ? filter : null, cancellationToken);
    }

    public Task<Result> DecideComplianceAsync(LoanApplicationId id, bool clear, string? reason, CancellationToken cancellationToken) =>
        Change(id, Permissions.Compliance.Review, (application, profile, now) =>
            Task.FromResult(application.DecideCompliance(profile.UserId, clear, reason, now)), cancellationToken);

    public Task<Result> ApproveAsync(
        LoanApplicationId id, IReadOnlyList<RequestedCondition> conditions, string? overrideReason, CancellationToken cancellationToken) =>
        Change(id, Permissions.Underwriting.Decide, (application, profile, now) =>
            Task.FromResult(application.Approve(profile.UserId, CurrentAccessProfile.AuthorityOf(profile)!, conditions, overrideReason, now)),
            cancellationToken);

    public Task<Result> RejectAsync(LoanApplicationId id, string? reason, CancellationToken cancellationToken) =>
        Change(id, Permissions.Underwriting.Decide, (application, profile, now) =>
            Task.FromResult(application.Reject(profile.UserId, CurrentAccessProfile.AuthorityOf(profile)!, reason, now)),
            cancellationToken);

    public Task<Result> CounterOfferAsync(
        LoanApplicationId id, decimal amount, int termMonths, decimal annualRate, string? reason, CancellationToken cancellationToken) =>
        Change(id, Permissions.Underwriting.CounterOffer, (application, profile, now) =>
        {
            var authority = CurrentAccessProfile.AuthorityOf(profile);
            var terms = LoanTerms.Create(
                Money.Of(amount, application.Terms.Amount.Currency), termMonths, annualRate, application.Terms.ScheduleType);
            if (authority is null || terms.IsFailure)
            {
                return Task.FromResult(Result.Failure(terms.Error ?? CommonErrors.AccessDenied));
            }

            return Task.FromResult(application.CounterOffer(profile.UserId, authority, terms.Value, reason, now));
        }, cancellationToken);

    public Task<Result> VoteAsync(LoanApplicationId id, bool approve, string? reason, CancellationToken cancellationToken) =>
        ApplicationChange.RunWithRetryAsync(access, applications, unitOfWork, clock, id, Permissions.Committee.Vote,
            async (application, profile, now) =>
            {
                var required = (await thresholds.GetAsync(cancellationToken)).RequiredVotes;
                return application.Vote(profile.UserId, approve, reason, required, now);
            },
            cancellationToken);

    private static string PermissionFor(Inbox inbox) => inbox switch
    {
        Inbox.Underwriting => Permissions.Underwriting.Decide,
        Inbox.Committee => Permissions.Committee.Vote,
        Inbox.Compliance => Permissions.Compliance.Review,
        _ => throw new ArgumentOutOfRangeException(nameof(inbox), inbox, "Unknown inbox."),
    };

    private Task<Result> Change(
        LoanApplicationId id,
        string permission,
        Func<LoanApplication, UserAccessProfile, DateTimeOffset, Task<Result>> change,
        CancellationToken cancellationToken) =>
        ApplicationChange.RunAsync(access, applications, unitOfWork, clock, id, permission, change, cancellationToken);
}

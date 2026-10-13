using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Application.External;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Calendar;
using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Applications;

/// <summary>
/// Claims the applications a background job works on. Each call runs inside the job's transaction and locks the rows it
/// returns (<c>UPDLOCK, ROWLOCK</c>) while skipping rows another runner holds (<c>READPAST</c>), so two runners never
/// work on the same application. Keyset paging by id (<paramref name="after"/>) lets one run walk past candidates it
/// leaves unchanged.
/// </summary>
public interface IApplicationJobQueries
{
    /// <summary>Underwriting files not yet escalated that entered Underwriting before <paramref name="enteredBefore"/>.</summary>
    Task<IReadOnlyList<LoanApplicationId>> ClaimEscalationCandidatesAsync(
        DateTimeOffset enteredBefore, LoanApplicationId? after, int batchSize, CancellationToken cancellationToken);

    /// <summary>Approvals and counter-offers whose offer expired at or before <paramref name="now"/>.</summary>
    Task<IReadOnlyList<LoanApplicationId>> ClaimExpiredOffersAsync(
        DateTimeOffset now, LoanApplicationId? after, int batchSize, CancellationToken cancellationToken);
}

/// <summary>
/// The time-driven steps of the process, run periodically by a background service. Both are idempotent: running a job
/// twice, or two runners at once, has the effect of running it once — the claimed rows are locked for the batch, a
/// done step no longer matches the claim (escalated / no longer offered), and the domain changes nothing twice
/// (<see cref="LoanApplication.Escalate"/> sets the flag once; <see cref="LoanApplication.Expire"/> moves the status once).
/// </summary>
public sealed class ApplicationLifecycleJobs(
    IApplicationJobQueries candidates,
    ILoanApplicationRepository applications,
    IHolidayRepository holidays,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public const int BatchSize = 50;

    /// <summary>
    /// Escalates every file still in Underwriting <see cref="LendingPolicy.EscalationWorkingDays"/> working days after
    /// it arrived (Tbilisi time; weekends and public holidays do not count). From then on only a Senior Underwriter
    /// decides it, and the seniors are notified.
    /// </summary>
    /// <returns>How many applications were escalated.</returns>
    public async Task<int> EscalateOverdueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = BankTime.DateOf(now);
        var calendar = new WorkingDayCalendar(await holidays.ListDatesAsync(today.AddYears(-1), today.AddMonths(1), cancellationToken));

        // Working days are never fewer than calendar days, so this bound only narrows the candidates.
        return await RunAsync(
            (after, token) => candidates.ClaimEscalationCandidatesAsync(
                now.AddDays(-LendingPolicy.EscalationWorkingDays), after, BatchSize, token),
            application => application.EscalationDeadline(calendar, LendingPolicy.EscalationWorkingDays) <= now
                ? application.Escalate(now)
                : null,
            cancellationToken);
    }

    /// <summary>Expires approvals and counter-offers the client did not accept within the offer's validity.</summary>
    /// <returns>How many applications expired.</returns>
    public async Task<int> ExpireOffersAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return await RunAsync(
            (after, token) => candidates.ClaimExpiredOffersAsync(now, after, BatchSize, token),
            application => application.Expire(now),
            cancellationToken);
    }

    /// <summary>
    /// Batch by batch: claim, change, save, commit. <paramref name="change"/> returns <c>null</c> for a candidate that is
    /// not due yet. A conflict with a user's change ends the run without saving the batch; the next run picks it up.
    /// </summary>
    private async Task<int> RunAsync(
        Func<LoanApplicationId?, CancellationToken, Task<IReadOnlyList<LoanApplicationId>>> claim,
        Func<LoanApplication, Result?> change,
        CancellationToken cancellationToken)
    {
        var total = 0;
        LoanApplicationId? after = null;
        try
        {
            while (true)
            {
                var changed = 0;
                IReadOnlyList<LoanApplicationId> claimed = [];
                await unitOfWork.ExecuteInTransactionAsync(
                    async token =>
                    {
                        claimed = await claim(after, token);
                        foreach (var id in claimed)
                        {
                            if (await applications.GetAsync(id, token) is { } application && change(application) is { IsSuccess: true })
                            {
                                changed++;
                            }
                        }

                        await unitOfWork.SaveChangesAsync(token);
                        return Result.Success();
                    },
                    cancellationToken);

                total += changed;
                if (claimed.Count < BatchSize)
                {
                    return total;
                }

                after = claimed[^1];
                unitOfWork.Reset();
            }
        }
        catch (ConcurrencyConflictException)
        {
            return total;
        }
    }
}

/// <summary>An escalated file is announced to every active user who may decide it (Senior Underwriters).</summary>
internal sealed class NotifyEscalationHandler(
    ILoanApplicationRepository applications, IUserDirectory users, INotificationSender sender) : IOutboxHandler<ApplicationEscalated>
{
    public async Task HandleAsync(ApplicationEscalated domainEvent, CancellationToken cancellationToken)
    {
        if (await applications.GetAsync(domainEvent.ApplicationId, cancellationToken) is not { } application)
        {
            return;
        }

        var reference = application.Reference;
        var subject = $"Application {reference}: escalated";
        var body = $"Application {reference} has waited in Underwriting for more than {LendingPolicy.EscalationWorkingDays} working days and now needs a Senior Underwriter.";
        foreach (var email in await users.ListActiveEmailsWithPermissionAsync(Permissions.Underwriting.DecideSenior, cancellationToken))
        {
            await sender.SendAsync(new Notification(NotificationChannel.Email, email, subject, body, domainEvent.ApplicationId), cancellationToken);
        }
    }
}

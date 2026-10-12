using CreditFlow.Application.Applications;
using CreditFlow.Domain.Applications;
using CreditFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CreditFlow.Infrastructure.Jobs;

/// <summary>Background job settings (configuration section <c>Jobs</c>).</summary>
public sealed class JobOptions
{
    public const string SectionName = "Jobs";

    public bool Enabled { get; set; } = true;

    /// <summary>How often escalation and offer expiry run.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Claims job candidates with <c>UPDLOCK, READPAST, ROWLOCK</c> inside the caller's transaction: the rows stay locked
/// until the batch commits, and a second runner skips them instead of waiting or doing the work twice.
/// </summary>
internal sealed class ApplicationJobQueries(CreditFlowDbContext db) : IApplicationJobQueries
{
    // Statuses are stored as their names.
    private const string Underwriting = nameof(ApplicationStatus.Underwriting);
    private const string Approved = nameof(ApplicationStatus.Approved);
    private const string ConditionallyApproved = nameof(ApplicationStatus.ConditionallyApproved);
    private const string CounterOffered = nameof(ApplicationStatus.CounterOffered);

    public async Task<IReadOnlyList<LoanApplicationId>> ClaimEscalationCandidatesAsync(
        DateTimeOffset enteredBefore, LoanApplicationId? after, int batchSize, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        var from = after?.Value ?? Guid.Empty; // the smallest uniqueidentifier
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT TOP ({batchSize}) [Id] AS [Value]
            FROM [LoanApplications] WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE [Status] = {Underwriting} AND [EscalatedAt] IS NULL AND [StatusChangedAt] <= {enteredBefore} AND [Id] > {from}
            ORDER BY [Id]
            """).ToListAsync(cancellationToken);
        return ids.Select(id => new LoanApplicationId(id)).ToList();
    }

    public async Task<IReadOnlyList<LoanApplicationId>> ClaimExpiredOffersAsync(
        DateTimeOffset now, LoanApplicationId? after, int batchSize, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        var from = after?.Value ?? Guid.Empty;
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT TOP ({batchSize}) [Id] AS [Value]
            FROM [LoanApplications] WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE [Status] IN ({Approved}, {ConditionallyApproved}, {CounterOffered}) AND [OfferExpiresAt] <= {now} AND [Id] > {from}
            ORDER BY [Id]
            """).ToListAsync(cancellationToken);
        return ids.Select(id => new LoanApplicationId(id)).ToList();
    }

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Job candidates are claimed inside the job's transaction.");
        }
    }
}

/// <summary>Runs escalation and offer expiry every <see cref="JobOptions.Interval"/>, each run in its own scope.</summary>
internal sealed partial class LifecycleJobsService(
    IServiceScopeFactory scopes, IOptions<JobOptions> options, TimeProvider clock, ILogger<LifecycleJobsService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var jobs = scope.ServiceProvider.GetRequiredService<ApplicationLifecycleJobs>();
                var escalated = await jobs.EscalateOverdueAsync(stoppingToken);
                var expired = await jobs.ExpireOffersAsync(stoppingToken);
                if (escalated + expired > 0)
                {
                    LogRun(escalated, expired);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(exception);
            }

            await Task.Delay(options.Value.Interval, clock, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Lifecycle jobs: {Escalated} escalated, {Expired} expired")]
    private partial void LogRun(int escalated, int expired);

    [LoggerMessage(Level = LogLevel.Error, Message = "The lifecycle jobs failed; they will run again")]
    private partial void LogFailed(Exception exception);
}

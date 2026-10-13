using CreditFlow.Application.Common;
using CreditFlow.Domain.Common;
using CreditFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CreditFlow.Infrastructure.Outbox;

/// <summary>Outbox settings (configuration section <c>Outbox</c>).</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Run the background dispatcher (tests turn it off and dispatch explicitly).</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 20;

    /// <summary>How long a claimed message is reserved for one dispatcher.</summary>
    public TimeSpan ClaimDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The longest wait between two attempts (the backoff doubles from one minute up to this).</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Dispatches outbox messages. Claiming is safe with several dispatchers: a short transaction reserves a batch with
/// <c>UPDLOCK, READPAST</c> (rows another dispatcher holds are skipped) and sets <see cref="OutboxMessage.ClaimedUntil"/>.
/// Each handler then runs in its own transaction, which also records it in <see cref="ProcessedMessage"/>, so a retried
/// message never runs a handler twice. A failure leaves the message for a later attempt with exponential backoff.
/// </summary>
public sealed partial class OutboxProcessor(
    IServiceScopeFactory scopes, TimeProvider clock, IOptions<OutboxOptions> options, ILogger<OutboxProcessor> logger)
{
    /// <summary>Claims and dispatches one batch; returns the number of messages claimed.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimAsync(cancellationToken);
        foreach (var id in claimed)
        {
            await ProcessAsync(id, cancellationToken);
        }

        return claimed.Count;
    }

    /// <summary>Dispatches until nothing is due (tests and start-up catch-up).</summary>
    public async Task<int> ProcessAllDueAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        int claimed;
        while ((claimed = await ProcessBatchAsync(cancellationToken)) > 0)
        {
            total += claimed;
        }

        return total;
    }

    private async Task<List<Guid>> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CreditFlowDbContext>();
        var now = clock.GetUtcNow();
        var until = now + options.Value.ClaimDuration;
        var batch = options.Value.BatchSize;

        return await db.Database.SqlQuery<Guid>(
                $"""
                WITH due AS (
                    SELECT TOP ({batch}) *
                    FROM [OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE [ProcessedAt] IS NULL AND [AvailableAt] <= {now}
                      AND ([ClaimedUntil] IS NULL OR [ClaimedUntil] < {now})
                    ORDER BY [OccurredAt])
                UPDATE due SET [ClaimedUntil] = {until}
                OUTPUT inserted.[Id] AS [Value];
                """)
            .ToListAsync(cancellationToken);
    }

    private async Task ProcessAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CreditFlowDbContext>();
        var message = await db.Set<OutboxMessage>().SingleAsync(m => m.Id == messageId, cancellationToken);

        try
        {
            var domainEvent = OutboxEventTypes.FromMessage(message);
            var handlerType = typeof(IOutboxHandler<>).MakeGenericType(domainEvent.GetType());
            var handlerTypes = scope.ServiceProvider.GetServices(handlerType).Select(h => h!.GetType()).ToList();
            foreach (var type in handlerTypes)
            {
                await RunHandlerAsync(handlerType, type, domainEvent, messageId, cancellationToken);
            }

            message.ProcessedAt = clock.GetUtcNow();
            message.ClaimedUntil = null;
            message.LastError = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            message = await db.Set<OutboxMessage>().SingleAsync(m => m.Id == messageId, CancellationToken.None);
            message.Attempts++;
            message.AvailableAt = clock.GetUtcNow() + Backoff(message.Attempts);
            message.ClaimedUntil = null;
            var cause = Unwrap(exception);
            message.LastError = Truncate(SensitiveData.MaskText($"{cause.GetType().Name}: {cause.Message}"));
            if (IsTransient(cause))
            {
                // Expected (a mock service down or slow): one line, no stack trace; the retry is scheduled.
                LogTransientRetry(message.Type, messageId, message.Attempts, message.AvailableAt, cause.Message);
            }
            else
            {
                LogRetry(message.Type, messageId, message.Attempts, message.AvailableAt, exception);
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>One handler in its own transaction (in a fresh scope), recorded as processed in that transaction.</summary>
    private async Task RunHandlerAsync(
        Type handlerInterface, Type handlerType, IDomainEvent domainEvent, Guid messageId, CancellationToken cancellationToken)
    {
        var name = handlerType.FullName!;
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<CreditFlowDbContext>();
        var handler = services.GetServices(handlerInterface).Single(h => h!.GetType() == handlerType)!;
        var handle = handlerInterface.GetMethod(nameof(IOutboxHandler<IDomainEvent>.HandleAsync))!;

        var result = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            async token =>
            {
                if (await db.Set<ProcessedMessage>().AnyAsync(p => p.MessageId == messageId && p.Handler == name, token))
                {
                    return Result.Success();
                }

                await (Task)handle.Invoke(handler, [domainEvent, token])!;

                db.Set<ProcessedMessage>().Add(new ProcessedMessage { MessageId = messageId, Handler = name, ProcessedAt = clock.GetUtcNow() });
                await db.SaveChangesAsync(token);
                return Result.Success();
            },
            cancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{name} failed: {result.Error!.Description}");
        }
    }

    /// <summary>1, 2, 4, 8 … minutes, at most <see cref="OutboxOptions.MaxBackoff"/>.</summary>
    internal TimeSpan Backoff(int attempts)
    {
        var minutes = Math.Pow(2, Math.Min(attempts - 1, 20));
        var backoff = TimeSpan.FromMinutes(minutes);
        return backoff < options.Value.MaxBackoff ? backoff : options.Value.MaxBackoff;
    }

    /// <summary>The handler's own exception (handlers are invoked through reflection).</summary>
    private static Exception Unwrap(Exception exception) =>
        exception is System.Reflection.TargetInvocationException { InnerException: { } inner } ? Unwrap(inner) : exception;

    /// <summary>An external service that was down or did not answer in time: retried with backoff, not a bug.</summary>
    internal static bool IsTransient(Exception exception) =>
        exception is ExternalServiceUnavailableException or TimeoutException;

    private static string Truncate(string text) =>
        text.Length <= OutboxMessage.ErrorMaxLength ? text : text[..OutboxMessage.ErrorMaxLength];

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Outbox message {Type} {MessageId} failed (attempt {Attempts}); next attempt at {AvailableAt}")]
    private partial void LogRetry(string type, Guid messageId, int attempts, DateTimeOffset availableAt, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Outbox message {Type} {MessageId} will be retried (attempt {Attempts}) at {AvailableAt}: {Reason}")]
    private partial void LogTransientRetry(string type, Guid messageId, int attempts, DateTimeOffset availableAt, string reason);
}

/// <summary>Runs the <see cref="OutboxProcessor"/> in the background.</summary>
public sealed partial class OutboxDispatcher(
    OutboxProcessor processor, IOptions<OutboxOptions> options, TimeProvider clock, ILogger<OutboxDispatcher> logger)
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
                if (await processor.ProcessBatchAsync(stoppingToken) > 0)
                {
                    continue; // more may be due
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogDispatchFailed(exception);
            }

            await Task.Delay(options.Value.PollInterval, clock, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The outbox dispatcher failed; it will try again")]
    private partial void LogDispatchFailed(Exception exception);
}

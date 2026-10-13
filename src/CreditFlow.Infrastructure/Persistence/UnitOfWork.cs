using System.Text.RegularExpressions;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence;

internal sealed partial class UnitOfWork(CreditFlowDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The data was changed by someone else.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            // 2601: duplicate key in a unique index; 2627: unique / primary key constraint.
            var index = IndexNamePattern().Match(sql.Message) is { Success: true } match ? match.Groups["name"].Value : null;
            throw new UniqueConstraintViolationException(index, $"Unique index '{index}' rejected the change.", exception);
        }
    }

    [GeneratedRegex("(?:unique index|constraint) '(?<name>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex IndexNamePattern();

    public void Reset() => db.ChangeTracker.Clear();

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
        where TResult : Result
    {
        ArgumentNullException.ThrowIfNull(work);

        // Nested call: join the transaction that is already open.
        if (db.Database.CurrentTransaction is not null)
        {
            return await work(cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await work(cancellationToken);
            if (result.IsSuccess)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }

            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}

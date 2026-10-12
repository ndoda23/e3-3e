using CreditFlow.Domain.Common;

namespace CreditFlow.Application.Common;

public static class CommonErrors
{
    /// <summary>An operation that needs an acting user was called without one.</summary>
    public static readonly Error NotAuthenticated = Error.Forbidden(
        "Access.NotAuthenticated", "You must be signed in to perform this action.");

    /// <summary>The user lacks the permission for the operation (the web layer checks it first; this is the second line).</summary>
    public static readonly Error AccessDenied = Error.Forbidden(
        "Access.Denied", "You do not have permission to do this.");

    /// <summary>Someone else changed the same record in the meantime (optimistic concurrency).</summary>
    public static readonly Error ConcurrencyConflict = Error.Conflict(
        "Concurrency.Conflict", "Someone else changed this record at the same time. Reload the page and try again.");
}

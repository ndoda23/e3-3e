using Lams.Domain.Common;

namespace Lams.Application.Common;

public static class CommonErrors
{
    /// <summary>An operation that needs an acting user was called without one.</summary>
    public static readonly Error NotAuthenticated = Error.Forbidden(
        "Access.NotAuthenticated", "You must be signed in to perform this action.");
}

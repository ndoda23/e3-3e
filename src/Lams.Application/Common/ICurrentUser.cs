using Lams.Domain.Users;

namespace Lams.Application.Common;

/// <summary>The user and request context of the current operation (empty for background work and anonymous requests).</summary>
public interface ICurrentUser
{
    /// <summary>The signed-in user, or <c>null</c> when nobody is signed in.</summary>
    UserId? UserId { get; }

    string? UserName { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    string? CorrelationId { get; }
}

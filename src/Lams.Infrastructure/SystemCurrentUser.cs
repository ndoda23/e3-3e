using Lams.Application.Common;
using Lams.Domain.Users;

namespace Lams.Infrastructure;

/// <summary>Current user outside an HTTP request (start-up seeding, background work): nobody.</summary>
internal sealed class SystemCurrentUser : ICurrentUser
{
    public UserId? UserId => null;

    public string? UserName => "system";

    public string? IpAddress => null;

    public string? UserAgent => null;

    public string? CorrelationId => null;
}

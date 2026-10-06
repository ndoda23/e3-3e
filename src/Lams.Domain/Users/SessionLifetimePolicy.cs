namespace Lams.Domain.Users;

/// <summary>
/// Absolute session lifetime: a session ends a fixed time after the original sign-in, regardless of activity.
/// Fails closed: a session without a known sign-in time, or with a sign-in time in the future, is expired.
/// </summary>
public static class SessionLifetimePolicy
{
    /// <summary>Tolerance for a sign-in time slightly ahead of the current clock.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static bool IsExpired(DateTimeOffset? signedInAt, DateTimeOffset now, TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        if (signedInAt is not { } start)
        {
            return true;
        }

        if (start > now + MaxClockSkew)
        {
            return true;
        }

        return now >= start + lifetime;
    }
}

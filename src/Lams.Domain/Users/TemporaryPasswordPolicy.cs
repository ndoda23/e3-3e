namespace Lams.Domain.Users;

/// <summary>A temporary password issued by an administrator is valid only for a limited time.</summary>
public static class TemporaryPasswordPolicy
{
    public static DateTimeOffset ExpiresAt(DateTimeOffset issuedAt, TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        return issuedAt + lifetime;
    }

    /// <summary>
    /// True when a temporary password exists (<paramref name="expiresAt"/> is set) and its lifetime has ended.
    /// A user without a temporary password (<c>null</c>) is never expired by this rule.
    /// </summary>
    public static bool IsExpired(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt is { } expiry && now >= expiry;
}

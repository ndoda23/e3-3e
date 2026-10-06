using Lams.Domain.Users;

namespace Lams.Domain.UnitTests.Users;

public sealed class TemporaryPasswordPolicyTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    [Fact]
    public void Expiry_is_issue_time_plus_lifetime()
    {
        Assert.Equal(IssuedAt.AddHours(72), TemporaryPasswordPolicy.ExpiresAt(IssuedAt, Lifetime));
    }

    [Fact]
    public void Lifetime_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TemporaryPasswordPolicy.ExpiresAt(IssuedAt, TimeSpan.Zero));
    }

    [Fact]
    public void Valid_until_the_expiry_moment()
    {
        var expiresAt = TemporaryPasswordPolicy.ExpiresAt(IssuedAt, Lifetime);

        Assert.False(TemporaryPasswordPolicy.IsExpired(expiresAt, expiresAt.AddTicks(-1)));
    }

    [Fact]
    public void Expired_at_and_after_the_expiry_moment()
    {
        var expiresAt = TemporaryPasswordPolicy.ExpiresAt(IssuedAt, Lifetime);

        Assert.True(TemporaryPasswordPolicy.IsExpired(expiresAt, expiresAt));
        Assert.True(TemporaryPasswordPolicy.IsExpired(expiresAt, expiresAt.AddMinutes(1)));
    }

    [Fact]
    public void A_user_without_a_temporary_password_is_never_expired_by_this_rule()
    {
        Assert.False(TemporaryPasswordPolicy.IsExpired(null, DateTimeOffset.MaxValue));
    }
}

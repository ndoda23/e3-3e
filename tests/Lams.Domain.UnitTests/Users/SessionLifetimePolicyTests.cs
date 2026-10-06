using Lams.Domain.Users;

namespace Lams.Domain.UnitTests.Users;

public sealed class SessionLifetimePolicyTests
{
    private static readonly DateTimeOffset SignedInAt = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TenHours = TimeSpan.FromHours(10);

    [Fact]
    public void Session_is_valid_before_the_absolute_lifetime()
    {
        Assert.False(SessionLifetimePolicy.IsExpired(SignedInAt, SignedInAt.AddHours(9).AddMinutes(59), TenHours));
    }

    [Fact]
    public void Session_ends_exactly_at_the_absolute_lifetime()
    {
        Assert.True(SessionLifetimePolicy.IsExpired(SignedInAt, SignedInAt.AddHours(10), TenHours));
    }

    [Fact]
    public void Missing_sign_in_time_fails_closed()
    {
        Assert.True(SessionLifetimePolicy.IsExpired(null, SignedInAt, TenHours));
    }

    [Fact]
    public void Sign_in_time_far_in_the_future_fails_closed()
    {
        var now = SignedInAt;
        var tampered = now + SessionLifetimePolicy.MaxClockSkew + TimeSpan.FromSeconds(1);

        Assert.True(SessionLifetimePolicy.IsExpired(tampered, now, TenHours));
    }

    [Fact]
    public void Small_clock_skew_is_tolerated()
    {
        var now = SignedInAt;

        Assert.False(SessionLifetimePolicy.IsExpired(now.AddMinutes(1), now, TenHours));
    }

    [Fact]
    public void Lifetime_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SessionLifetimePolicy.IsExpired(SignedInAt, SignedInAt, TimeSpan.Zero));
    }
}

namespace CreditFlow.Application.Common;

/// <summary>Security settings (configuration section <c>Security</c>). Validated on start by the host.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>A session ends this long after sign-in, regardless of activity.</summary>
    public TimeSpan AbsoluteSessionLifetime { get; set; } = TimeSpan.FromHours(10);

    /// <summary>Inactivity after which the sliding session expires.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long a temporary password issued by an administrator stays valid.</summary>
    public TimeSpan TemporaryPasswordLifetime { get; set; } = TimeSpan.FromHours(72);

    /// <summary>How often the security stamp is re-validated (deactivation / forced sign-out take effect within this).</summary>
    public TimeSpan SecurityStampValidationInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Sign-in attempts (password, 2FA and recovery code posts) allowed per client IP address per minute.</summary>
    public int SignInAttemptsPerMinute { get; set; } = 10;
}

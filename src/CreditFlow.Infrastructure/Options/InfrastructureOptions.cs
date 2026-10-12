using CreditFlow.Application.Common;
using Microsoft.Extensions.Options;

namespace CreditFlow.Infrastructure.Options;

/// <summary>Configuration section <c>Database</c>. The connection string is <c>ConnectionStrings:CreditFlow</c>.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const string ConnectionStringName = "CreditFlow";

    /// <summary>Apply pending EF Core migrations at start-up (Development). Other environments use a migration bundle.</summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}

/// <summary>Configuration section <c>Seed</c>. Secrets (passwords) come from user-secrets or the environment.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public AdminSeedOptions Admin { get; set; } = new();

    public DevUserSeedOptions DevUsers { get; set; } = new();
}

/// <summary>The first System Administrator account, created once if it does not exist.</summary>
public sealed class AdminSeedOptions
{
    public string? UserName { get; set; }

    public string? Email { get; set; }

    public string FullName { get; set; } = "System Administrator";

    /// <summary>Initial password; the administrator must change it at first sign-in.</summary>
    public string? Password { get; set; }
}

/// <summary>Demo users (one per role/level/segment). Created only in the Development environment.</summary>
public sealed class DevUserSeedOptions
{
    public bool Enabled { get; set; }

    /// <summary>Shared password of all demo users.</summary>
    public string? Password { get; set; }
}

internal sealed class SeedOptionsValidator : IValidateOptions<SeedOptions>
{
    public ValidateOptionsResult Validate(string? name, SeedOptions options)
    {
        var failures = new List<string>();
        var admin = options.Admin;

        if (!string.IsNullOrWhiteSpace(admin.UserName)
            && (string.IsNullOrWhiteSpace(admin.Email) || string.IsNullOrWhiteSpace(admin.Password)))
        {
            failures.Add("Seed:Admin requires Email and Password when UserName is set.");
        }

        if (options.DevUsers.Enabled && string.IsNullOrWhiteSpace(options.DevUsers.Password))
        {
            failures.Add("Seed:DevUsers:Password is required when Seed:DevUsers:Enabled is true.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed class SecurityOptionsValidator : IValidateOptions<SecurityOptions>
{
    public ValidateOptionsResult Validate(string? name, SecurityOptions options)
    {
        var failures = new List<string>();

        if (options.IdleTimeout <= TimeSpan.Zero)
        {
            failures.Add("Security:IdleTimeout must be positive.");
        }

        if (options.AbsoluteSessionLifetime < options.IdleTimeout)
        {
            failures.Add("Security:AbsoluteSessionLifetime must be at least Security:IdleTimeout.");
        }

        if (options.TemporaryPasswordLifetime <= TimeSpan.Zero)
        {
            failures.Add("Security:TemporaryPasswordLifetime must be positive.");
        }

        if (options.SecurityStampValidationInterval <= TimeSpan.Zero
            || options.SecurityStampValidationInterval > TimeSpan.FromMinutes(1))
        {
            failures.Add("Security:SecurityStampValidationInterval must be between 0 and 1 minute.");
        }

        if (options.SignInAttemptsPerMinute <= 0)
        {
            failures.Add("Security:SignInAttemptsPerMinute must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

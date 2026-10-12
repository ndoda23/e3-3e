using CreditFlow.Application.Auditing;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Branches;
using CreditFlow.Application.Common;
using CreditFlow.Application.Roles;
using CreditFlow.Application.Users;
using CreditFlow.Infrastructure.Auditing;
using CreditFlow.Infrastructure.Authorization;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.Infrastructure.Options;
using CreditFlow.Infrastructure.Persistence;
using CreditFlow.Infrastructure.Persistence.Interceptors;
using CreditFlow.Infrastructure.Persistence.Repositories;
using CreditFlow.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CreditFlow.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Lockout policy: 5 failed attempts lock the account for 15 minutes.</summary>
    public const int MaxFailedAccessAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();

        AddOptions(services, configuration);
        AddPersistence(services, configuration);
        AddIdentity(services);
        AddAccessCaching(services);

        services.AddDataProtection()
            .SetApplicationName("CreditFlow")
            .PersistKeysToDbContext<CreditFlowDbContext>();

        services.AddHealthChecks().AddDbContextCheck<CreditFlowDbContext>("database");

        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName)).ValidateOnStart();

        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<SeedOptions>, SeedOptionsValidator>();

        services.AddOptions<SecurityOptions>().Bind(configuration.GetSection(SecurityOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ConsistencyInterceptor>();
        services.AddScoped<AuditTrailInterceptor>();

        services.AddDbContext<CreditFlowDbContext>((provider, options) =>
        {
            var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{DatabaseOptions.ConnectionStringName}' is not configured (use user-secrets).");
            }

            options.UseSqlServer(connectionString);
            options.AddInterceptors(
                provider.GetRequiredService<ConsistencyInterceptor>(),
                provider.GetRequiredService<AuditTrailInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditLogQueries, AuditLogQueries>();

        services.AddScoped<ReferenceDataSeeder>();
        services.AddScoped<AccountSeeder>();
        services.AddScoped<DatabaseInitializer>();
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services
            .AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredUniqueChars = 4;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = MaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;

                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters =
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";

                // Accounts are created by administrators; there is no self-registration or email confirmation.
                options.SignIn.RequireConfirmedAccount = false;
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddEntityFrameworkStores<CreditFlowDbContext>()
            .AddSignInManager<CreditFlowSignInManager>()
            .AddClaimsPrincipalFactory<CreditFlowUserClaimsPrincipalFactory>()
            .AddPasswordValidator<PasswordHistoryValidator>()
            .AddDefaultTokenProviders();

        // Deactivation, role/attribute changes and forced sign-out take effect within this interval.
        services.AddOptions<SecurityStampValidatorOptions>()
            .Configure<IOptions<SecurityOptions>, TimeProvider>((options, security, clock) =>
            {
                options.ValidationInterval = security.Value.SecurityStampValidationInterval;
                options.TimeProvider = clock;
            });

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<AccountService>();
        services.AddSingleton<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();
    }

    private static void AddAccessCaching(IServiceCollection services)
    {
        services.AddHybridCache(options =>
        {
            // Explicit invalidation does the real work; expiry is only a safety net.
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(1),
                LocalCacheExpiration = TimeSpan.FromMinutes(1),
            };
        });

        services.AddScoped<IUserAccessProfileService, UserAccessProfileService>();
        services.AddScoped<IAccessCacheInvalidator, AccessCacheInvalidator>();
    }
}

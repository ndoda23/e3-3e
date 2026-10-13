using CreditFlow.Application.Administration;
using CreditFlow.Application.Applications;
using CreditFlow.Application.Auditing;
using CreditFlow.Application.Authorization;
using CreditFlow.Application.Branches;
using CreditFlow.Application.Clients;
using CreditFlow.Application.Common;
using CreditFlow.Application.Reports;
using CreditFlow.Application.Disbursements;
using CreditFlow.Application.Roles;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Approval;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Calendar;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Loans;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.Users;
using CreditFlow.Application.External;
using CreditFlow.Infrastructure.Auditing;
using CreditFlow.Infrastructure.Documents;
using CreditFlow.Infrastructure.ExternalServices;
using CreditFlow.Infrastructure.Jobs;
using CreditFlow.Infrastructure.Outbox;
using CreditFlow.Infrastructure.Authorization;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.Infrastructure.Options;
using CreditFlow.Infrastructure.Persistence;
using CreditFlow.Infrastructure.Persistence.Interceptors;
using CreditFlow.Infrastructure.Persistence.Queries;
using CreditFlow.Infrastructure.Persistence.Repositories;
using CreditFlow.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        AddOutboxAndExternalServices(services, configuration);

        // The keys protect the auth and antiforgery cookies; they are stored in the database and encrypted at rest:
        // with a certificate when one is configured (servers, containers), otherwise with DPAPI on Windows.
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("CreditFlow")
            .PersistKeysToDbContext<CreditFlowDbContext>();
        var keyCertificate = configuration["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(keyCertificate))
        {
            dataProtection.ProtectKeysWithCertificate(
                System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                    keyCertificate, configuration["DataProtection:CertificatePassword"]));
        }
        else if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi();
        }

        services.AddHealthChecks().AddDbContextCheck<CreditFlowDbContext>("database");

        return services;
    }

    /// <summary>The transactional outbox with its background dispatcher, the lifecycle jobs, and the mock external services.</summary>
    private static void AddOutboxAndExternalServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.SectionName));
        services.AddSingleton<OutboxProcessor>();
        services.AddHostedService<OutboxDispatcher>();

        services.AddOptions<JobOptions>().Bind(configuration.GetSection(JobOptions.SectionName));
        services.AddScoped<IApplicationJobQueries, ApplicationJobQueries>();
        services.AddHostedService<LifecycleJobsService>();

        services.AddOptions<MockOptions>().Bind(configuration.GetSection(MockOptions.SectionName));
        services.AddSingleton<MockScenarios>();
        services.AddScoped<ISanctionsScreeningService, MockSanctionsScreeningService>();
        services.AddScoped<IIdentityVerificationService, MockIdentityVerificationService>();
        services.AddSingleton<ICreditBureauClient, MockCreditBureauClient>();
        services.AddSingleton<ICoreBankingClient, MockCoreBankingClient>();
        services.AddScoped<IBureauReportStore, BureauReportStore>();
        services.AddScoped<INotificationSender, DatabaseNotificationSender>();
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
        services.AddScoped<DomainEventAuditInterceptor>();
        services.AddSingleton<OutboxInterceptor>();
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
            if (configuration.GetSection(DatabaseOptions.SectionName).GetValue<bool>(nameof(DatabaseOptions.ThrowOnQueryWarnings)))
            {
                options.ConfigureWarnings(warnings => warnings.Throw(
                    RelationalEventId.MultipleCollectionIncludeWarning,
                    CoreEventId.RowLimitingOperationWithoutOrderByWarning,
                    CoreEventId.FirstWithoutOrderByAndFilterWarning,
                    SqlServerEventId.DecimalTypeDefaultWarning));
            }

            options.AddInterceptors(
                provider.GetRequiredService<DomainEventAuditInterceptor>(),
                provider.GetRequiredService<OutboxInterceptor>(),
                provider.GetRequiredService<ConsistencyInterceptor>(),
                provider.GetRequiredService<AuditTrailInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRoleQueries, RoleQueries>();
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IBranchQueries, BranchQueries>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<ILoanProductRepository, LoanProductRepository>();
        services.AddScoped<ILoanApplicationRepository, LoanApplicationRepository>();
        services.AddScoped<IScoringRuleSetRepository, ScoringRuleSetRepository>();
        services.AddScoped<IApprovalThresholdsRepository, ApprovalThresholdsRepository>();
        services.AddScoped<IFxRateRepository, FxRateRepository>();
        services.AddScoped<IHolidayRepository, HolidayRepository>();
        services.AddScoped<IDisbursementRepository, DisbursementRepository>();
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<IContractNumberGenerator, ContractNumberGenerator>();
        services.AddScoped<IExposureLock, SqlExposureLock>();
        services.AddScoped<ILoanQueries, LoanQueries>();
        services.AddScoped<ILendingAdminQueries, LendingAdminQueries>();
        services.AddScoped<IReportQueries, ReportQueries>();
        services.AddSingleton<IReportExcelWriter, ExcelReportWriter>();
        services.AddSingleton<IContractDocumentRenderer, PdfContractRenderer>();
        services.AddScoped<IApplicationQueries, ApplicationQueries>();
        services.AddScoped<CreditFlow.Application.Dashboard.IDashboardQueries, DashboardQueries>();
        services.AddScoped<IClientQueries, ClientQueries>();
        services.AddScoped<IProductQueries, ProductQueries>();
        services.AddScoped<IExposureQueries, ExposureQueries>();
        services.AddScoped<IDocumentStore, DocumentStore>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditLogQueries, AuditLogQueries>();

        services.AddScoped<ReferenceDataSeeder>();
        services.AddScoped<AccountSeeder>();
        services.AddScoped<DevLendingDataSeeder>();
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

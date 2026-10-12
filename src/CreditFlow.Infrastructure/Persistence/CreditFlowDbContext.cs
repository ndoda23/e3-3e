using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Approval;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Calendar;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Loans;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.Identity;
using CreditFlow.Infrastructure.Logging;
using CreditFlow.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CreditFlow.Infrastructure.Persistence;

/// <summary>The single CreditFlow database context: Identity, staff, reference data, audit log, logs and Data Protection keys.</summary>
public sealed class CreditFlowDbContext(DbContextOptions<CreditFlowDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserPasswordHistory> PasswordHistory => Set<UserPasswordHistory>();

    public DbSet<UserDirectoryRow> UserDirectory => Set<UserDirectoryRow>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<SeedHistoryEntry> SeedHistory => Set<SeedHistoryEntry>();

    public DbSet<LogEntry> Logs => Set<LogEntry>();

    public DbSet<Client> Clients => Set<Client>();

    public DbSet<LoanProduct> LoanProducts => Set<LoanProduct>();

    public DbSet<LoanApplication> LoanApplications => Set<LoanApplication>();

    public DbSet<ScoringRuleSet> ScoringRuleSets => Set<ScoringRuleSet>();

    public DbSet<ApprovalThresholds> ApprovalThresholds => Set<ApprovalThresholds>();

    public DbSet<FxRate> FxRates => Set<FxRate>();

    public DbSet<Holiday> Holidays => Set<Holiday>();

    public DbSet<Disbursement> Disbursements => Set<Disbursement>();

    public DbSet<Loan> Loans => Set<Loan>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(CreditFlowDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<UserId>().HaveConversion<UserIdConverter>();
        configurationBuilder.Properties<RoleId>().HaveConversion<RoleIdConverter>();
        configurationBuilder.Properties<BranchId>().HaveConversion<BranchIdConverter>();
        configurationBuilder.Properties<AuditLogId>().HaveConversion<AuditLogIdConverter>();
        configurationBuilder.Properties<ClientId>().HaveConversion<ClientIdConverter>();
        configurationBuilder.Properties<ClientLineId>().HaveConversion<ClientLineIdConverter>();
        configurationBuilder.Properties<LoanProductId>().HaveConversion<LoanProductIdConverter>();
        configurationBuilder.Properties<LoanApplicationId>().HaveConversion<LoanApplicationIdConverter>();
        configurationBuilder.Properties<ApplicationLineId>().HaveConversion<ApplicationLineIdConverter>();
        configurationBuilder.Properties<DisbursementId>().HaveConversion<DisbursementIdConverter>();
        configurationBuilder.Properties<LoanId>().HaveConversion<LoanIdConverter>();

        // Enums are stored as readable strings.
        configurationBuilder.Properties<DataScope>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<DashboardKind>().HaveConversion<string>().HaveMaxLength(30);
        configurationBuilder.Properties<AttributeRule>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<RoleKind>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<Currency>().HaveConversion<string>().HaveMaxLength(3);
        foreach (var enumType in new[]
        {
            typeof(ProductType), typeof(ScheduleType), typeof(IncomeType), typeof(KycStatus), typeof(ScreeningStatus),
            typeof(IdentityDocumentType), typeof(ApplicationStatus), typeof(PartyRole), typeof(CollateralType),
            typeof(DecisionKind), typeof(ConditionType), typeof(DocumentType), typeof(ApprovalTier),
            typeof(DisbursementStatus), typeof(LoanStatus), typeof(RepaymentKind),
        })
        {
            configurationBuilder.Properties(enumType).HaveConversion<string>().HaveMaxLength(30);
        }
    }
}

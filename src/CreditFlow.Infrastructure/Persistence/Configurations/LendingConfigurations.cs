using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Approval;
using CreditFlow.Domain.Branches;
using CreditFlow.Domain.Calendar;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Scoring;
using CreditFlow.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CreditFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Optimistic concurrency for aggregate roots: a <c>rowversion</c> checked on every update, and a <c>Revision</c>
/// counter that <see cref="Interceptors.ConsistencyInterceptor"/> increments whenever a part of the aggregate changes,
/// so that changing only child rows still updates (and checks) the root. Both are shadow properties: the domain
/// model knows nothing about them.
/// </summary>
internal static class AggregateRoot
{
    public const string RowVersion = "RowVersion";
    public const string Revision = "Revision";

    public static void Configure<T>(EntityTypeBuilder<T> builder)
        where T : class
    {
        builder.Property<byte[]>(RowVersion).IsRowVersion();
        builder.Property<int>(Revision).HasDefaultValue(0);
    }
}

internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("Clients");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.PersonalNumber)
            .HasConversion(pn => pn.Value, value => PersonalNumber.Create(value).Value)
            .HasMaxLength(PersonalNumber.Length)
            .IsFixedLength()
            .IsUnicode(false);
        builder.HasIndex(c => c.PersonalNumber).IsUnique();
        builder.Property(c => c.FirstName).HasMaxLength(Client.NameMaxLength);
        builder.Property(c => c.LastName).HasMaxLength(Client.NameMaxLength);
        builder.Property(c => c.Phone).HasMaxLength(Client.PhoneMaxLength);
        builder.Property(c => c.Email).HasMaxLength(Client.EmailMaxLength);
        builder.Property(c => c.Address).HasMaxLength(Client.AddressMaxLength);
        builder.Property(c => c.KycStatus).HasMaxLength(20);
        builder.Property(c => c.ScreeningStatus).HasMaxLength(20);
        builder.HasIndex(c => new { c.LastName, c.FirstName });
        builder.Ignore(c => c.FullName);

        builder.ComplexProperty(c => c.IdentityDocument, document =>
        {
            document.Property(d => d.Type).HasColumnName("DocumentType").HasMaxLength(20);
            document.Property(d => d.Number).HasColumnName("DocumentNumber").HasMaxLength(IdentityDocument.NumberMaxLength);
            document.Property(d => d.IssuedOn).HasColumnName("DocumentIssuedOn");
            document.Property(d => d.ExpiresOn).HasColumnName("DocumentExpiresOn");
        });

        builder.HasMany(c => c.Incomes).WithOne().HasForeignKey("ClientId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Obligations).WithOne().HasForeignKey("ClientId").OnDelete(DeleteBehavior.Cascade);
        AggregateRoot.Configure(builder);
    }
}

internal sealed class ClientIncomeConfiguration : IEntityTypeConfiguration<ClientIncome>
{
    public void Configure(EntityTypeBuilder<ClientIncome> builder)
    {
        builder.ToTable("ClientIncomes");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Type).HasMaxLength(20);
        builder.Property(i => i.Source).HasMaxLength(ClientIncome.SourceMaxLength);
        builder.ComplexProperty(i => i.MonthlyAmount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("MonthlyAmount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("Currency");
        });
    }
}

internal sealed class ClientObligationConfiguration : IEntityTypeConfiguration<ClientObligation>
{
    public void Configure(EntityTypeBuilder<ClientObligation> builder)
    {
        builder.ToTable("ClientObligations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Lender).HasMaxLength(ClientObligation.LenderMaxLength);
        builder.ComplexProperty(o => o.MonthlyPayment, money =>
        {
            money.Property(m => m.Amount).HasColumnName("MonthlyPayment").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("Currency");
        });
        builder.ComplexProperty(o => o.OutstandingAmount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("OutstandingAmount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("OutstandingCurrency");
        });
    }
}

internal sealed class LoanProductConfiguration : IEntityTypeConfiguration<LoanProduct>
{
    public void Configure(EntityTypeBuilder<LoanProduct> builder)
    {
        builder.ToTable("LoanProducts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Code).HasMaxLength(LoanProduct.CodeMaxLength);
        builder.HasIndex(p => p.Code).IsUnique();
        builder.Property(p => p.Name).HasMaxLength(LoanProduct.NameMaxLength);
        builder.Property(p => p.Type).HasMaxLength(20);
        builder.Ignore(p => p.CurrentVersion);
        builder.HasMany(p => p.Versions).WithOne().HasForeignKey("ProductId").OnDelete(DeleteBehavior.Cascade);
        AggregateRoot.Configure(builder);
    }
}

internal sealed class LoanProductVersionConfiguration : IEntityTypeConfiguration<LoanProductVersion>
{
    public void Configure(EntityTypeBuilder<LoanProductVersion> builder)
    {
        builder.ToTable("LoanProductVersions");
        builder.Property<LoanProductId>("ProductId");
        builder.HasKey("ProductId", nameof(LoanProductVersion.Number));
        builder.Property(v => v.Number).ValueGeneratedNever();
        builder.Property(v => v.Terms)
            .HasConversion(JsonColumns.Converter<ProductTerms>(), JsonColumns.Comparer<ProductTerms>())
            .HasColumnName("TermsJson");
    }
}

internal sealed class LoanApplicationConfiguration : IEntityTypeConfiguration<LoanApplication>
{
    public void Configure(EntityTypeBuilder<LoanApplication> builder)
    {
        builder.ToTable("LoanApplications");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Status).HasMaxLength(30);
        builder.Property(a => a.Tier).HasMaxLength(20);
        builder.Property(a => a.AmountGel).HasPrecision(18, 2);
        builder.Property(a => a.ContractNumber).HasMaxLength(20);
        builder.HasIndex(a => a.ContractNumber).IsUnique().HasFilter("[ContractNumber] IS NOT NULL");

        builder.HasOne<Branch>().WithMany().HasForeignKey(a => a.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(a => a.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LoanProduct>().WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Client>().WithMany().HasForeignKey(a => a.BorrowerId).OnDelete(DeleteBehavior.Restrict);

        builder.ComplexProperty(a => a.Terms, terms =>
        {
            terms.ComplexProperty(t => t.Amount, money =>
            {
                money.Property(m => m.Amount).HasColumnName("Amount").HasPrecision(18, 2);
                money.Property(m => m.Currency).HasColumnName("Currency");
            });
            terms.Property(t => t.TermMonths).HasColumnName("TermMonths");
            terms.Property(t => t.AnnualRate).HasColumnName("AnnualRate").HasPrecision(7, 4);
            terms.Property(t => t.ScheduleType).HasColumnName("ScheduleType").HasMaxLength(20);
        });

        builder.Property(a => a.Snapshot)
            .HasConversion(JsonColumns.Converter<ProductSnapshot?>(), JsonColumns.Comparer<ProductSnapshot?>())
            .HasColumnName("SnapshotJson");
        builder.Property(a => a.Rate)
            .HasConversion(JsonColumns.Converter<CreditFlow.Domain.Fx.ExchangeRate?>(), JsonColumns.Comparer<CreditFlow.Domain.Fx.ExchangeRate?>())
            .HasColumnName("RateJson");
        builder.Property(a => a.ProposedTerms)
            .HasConversion(JsonColumns.Converter<LoanTerms?>(), JsonColumns.Comparer<LoanTerms?>())
            .HasColumnName("ProposedTermsJson");
        builder.Property<List<ScoringRun>>("_scoringRuns")
            .HasConversion(JsonColumns.Converter<List<ScoringRun>>(), JsonColumns.Comparer<List<ScoringRun>>())
            .HasColumnName("ScoringRunsJson");
        builder.Ignore(a => a.ScoringRuns);
        builder.Ignore(a => a.LatestScoring);
        builder.Ignore(a => a.RequiredTier);
        builder.Ignore(a => a.CanChangeDocuments);
        builder.Ignore(a => a.DomainEvents);

        builder.HasMany(a => a.Parties).WithOne().HasForeignKey("ApplicationId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(a => a.Collaterals).WithOne().HasForeignKey("ApplicationId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(a => a.Documents).WithOne().HasForeignKey("ApplicationId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(a => a.History).WithOne().HasForeignKey("ApplicationId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(a => a.Decisions).WithOne().HasForeignKey("ApplicationId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(a => a.Conditions).WithOne().HasForeignKey("ApplicationId").OnDelete(DeleteBehavior.Cascade);

        // One open application per borrower and product (the domain checks it too).
        builder.HasIndex(a => new { a.BorrowerId, a.ProductId }).IsUnique().HasFilter("[IsOpen] = 1")
            .HasDatabaseName("UX_LoanApplications_OpenPerBorrowerAndProduct");

        // Operator lists (branch scope), inboxes and jobs.
        builder.HasIndex(a => new { a.BranchId, a.Status, a.StatusChangedAt });
        builder.HasIndex(a => new { a.Status, a.Tier, a.StatusChangedAt }).IncludeProperties(a => new { a.CreatedBy, a.AmountGel });
        builder.HasIndex(a => new { a.Status, a.OfferExpiresAt });
        builder.HasIndex(a => new { a.Status, a.EscalatedAt, a.StatusChangedAt });

        AggregateRoot.Configure(builder);
    }
}

internal sealed class ApplicationPartyConfiguration : IEntityTypeConfiguration<ApplicationParty>
{
    public void Configure(EntityTypeBuilder<ApplicationParty> builder)
    {
        builder.ToTable("ApplicationParties");
        builder.Property<LoanApplicationId>("ApplicationId");
        builder.HasKey("ApplicationId", nameof(ApplicationParty.ClientId));
        builder.Property(p => p.Role).HasMaxLength(20);
        builder.HasOne<Client>().WithMany().HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.ClientId);
    }
}

internal sealed class CollateralConfiguration : IEntityTypeConfiguration<Collateral>
{
    public void Configure(EntityTypeBuilder<Collateral> builder)
    {
        builder.ToTable("ApplicationCollaterals");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Type).HasMaxLength(20);
        builder.Property(c => c.Description).HasMaxLength(Collateral.DescriptionMaxLength);
        builder.ComplexProperty(c => c.MarketValue, money =>
        {
            money.Property(m => m.Amount).HasColumnName("MarketValue").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("Currency");
        });
    }
}

internal sealed class ApplicationDocumentConfiguration : IEntityTypeConfiguration<ApplicationDocument>
{
    public void Configure(EntityTypeBuilder<ApplicationDocument> builder)
    {
        builder.ToTable("ApplicationDocuments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Type).HasMaxLength(30);
        builder.Property(d => d.FileName).HasMaxLength(ApplicationDocument.FileNameMaxLength);
        builder.Property(d => d.ContentType).HasMaxLength(100);
        builder.Property(d => d.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
    }
}

internal sealed class StatusChangeConfiguration : IEntityTypeConfiguration<StatusChange>
{
    public void Configure(EntityTypeBuilder<StatusChange> builder)
    {
        builder.ToTable("ApplicationStatusHistory");
        builder.Property<LoanApplicationId>("ApplicationId");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).UseIdentityColumn();
        builder.Property(h => h.From).HasMaxLength(30);
        builder.Property(h => h.To).HasMaxLength(30);
        builder.Property(h => h.Reason).HasMaxLength(StatusChange.ReasonMaxLength);
        builder.HasIndex("ApplicationId", nameof(StatusChange.At));

        // Reports: time per stage, rejections and approvals by period.
        builder.HasIndex(h => new { h.To, h.At });
    }
}

internal sealed class ApplicationDecisionConfiguration : IEntityTypeConfiguration<ApplicationDecision>
{
    public void Configure(EntityTypeBuilder<ApplicationDecision> builder)
    {
        builder.ToTable("ApplicationDecisions");
        builder.Property<LoanApplicationId>("ApplicationId");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Kind).HasMaxLength(30);
        builder.Property(d => d.Reason).HasMaxLength(StatusChange.ReasonMaxLength);
        builder.Property(d => d.OverrideReason).HasMaxLength(StatusChange.ReasonMaxLength);

        // Four-eyes in the database: one decision per person per application.
        builder.HasIndex("ApplicationId", nameof(ApplicationDecision.DecidedBy)).IsUnique()
            .HasDatabaseName("UX_ApplicationDecisions_OnePerPerson");
    }
}

internal sealed class ApplicationConditionConfiguration : IEntityTypeConfiguration<ApplicationCondition>
{
    public void Configure(EntityTypeBuilder<ApplicationCondition> builder)
    {
        builder.ToTable("ApplicationConditions");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Type).HasMaxLength(30);
        builder.Property(c => c.Description).HasMaxLength(ApplicationCondition.DescriptionMaxLength);
    }
}

internal sealed class ScoringRuleSetConfiguration : IEntityTypeConfiguration<ScoringRuleSet>
{
    public void Configure(EntityTypeBuilder<ScoringRuleSet> builder)
    {
        builder.ToTable("ScoringRuleSets");
        builder.HasKey(s => s.Version);
        builder.Property(s => s.Version).ValueGeneratedNever();
        builder.Property<List<RuleDefinition>>("_rules")
            .HasConversion(JsonColumns.Converter<List<RuleDefinition>>(), JsonColumns.Comparer<List<RuleDefinition>>())
            .HasColumnName("RulesJson");
        builder.Ignore(s => s.Rules);
    }
}

internal sealed class ApprovalThresholdsConfiguration : IEntityTypeConfiguration<ApprovalThresholds>
{
    public void Configure(EntityTypeBuilder<ApprovalThresholds> builder)
    {
        builder.ToTable("ApprovalThresholds", t => t.HasCheckConstraint("CK_ApprovalThresholds_Single", "[Id] = 1"));
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.UnderwriterMaxGel).HasPrecision(18, 2);
        builder.Property(t => t.SeniorUnderwriterMaxGel).HasPrecision(18, 2);
        builder.Property<byte[]>(AggregateRoot.RowVersion).IsRowVersion();
    }
}

internal sealed class FxRateConfiguration : IEntityTypeConfiguration<FxRate>
{
    public void Configure(EntityTypeBuilder<FxRate> builder)
    {
        builder.ToTable("FxRates");
        builder.HasKey(r => new { r.Currency, r.EffectiveDate });
        builder.Property(r => r.GelPerUnit).HasPrecision(18, FxRate.RateDecimals);
    }
}

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.ToTable("Holidays");
        builder.HasKey(h => h.Date);
        builder.Property(h => h.Name).HasMaxLength(Holiday.NameMaxLength);
    }
}

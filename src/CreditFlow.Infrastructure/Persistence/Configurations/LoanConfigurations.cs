using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Clients;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Disbursements;
using CreditFlow.Domain.Loans;
using CreditFlow.Domain.Products;
using CreditFlow.Domain.Users;
using CreditFlow.Infrastructure.ExternalServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CreditFlow.Infrastructure.Persistence.Configurations;

internal sealed class DisbursementConfiguration : IEntityTypeConfiguration<Disbursement>
{
    public void Configure(EntityTypeBuilder<Disbursement> builder)
    {
        builder.ToTable("Disbursements");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        AggregateRoot.Configure(builder);
        builder.Ignore(d => d.DomainEvents);

        // One payout per application and one per key: a second request finds the first instead of paying again.
        builder.HasIndex(d => d.ApplicationId).IsUnique();
        builder.Property(d => d.IdempotencyKey).HasMaxLength(64);
        builder.HasIndex(d => d.IdempotencyKey).IsUnique();
        builder.HasOne<LoanApplication>().WithMany().HasForeignKey(d => d.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(d => d.RequestedBy).OnDelete(DeleteBehavior.Restrict);

        builder.ComplexProperty(d => d.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("Currency");
        });
        builder.Property(d => d.Status).HasMaxLength(20);
        builder.Property(d => d.TransactionId).HasMaxLength(Disbursement.TransactionIdMaxLength);
        builder.Property(d => d.LastError).HasMaxLength(Disbursement.ErrorMaxLength);
    }
}

internal sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("Loans");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        AggregateRoot.Configure(builder);
        builder.Ignore(l => l.DomainEvents);
        builder.Ignore(l => l.NextInstallment);

        builder.HasIndex(l => l.ApplicationId).IsUnique();
        builder.HasIndex(l => l.ContractNumber).IsUnique();
        builder.Property(l => l.ContractNumber).HasMaxLength(20);
        builder.HasOne<LoanApplication>().WithMany().HasForeignKey(l => l.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Client>().WithMany().HasForeignKey(l => l.BorrowerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LoanProduct>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);

        builder.ComplexProperty(l => l.Principal, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Principal").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("Currency");
        });
        builder.ComplexProperty(l => l.MonthlyFee, money =>
        {
            money.Property(m => m.Amount).HasColumnName("MonthlyFee").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("MonthlyFeeCurrency");
        });
        builder.ComplexProperty(l => l.OutstandingPrincipal, money =>
        {
            money.Property(m => m.Amount).HasColumnName("OutstandingPrincipal").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("OutstandingCurrency");
        });
        builder.Property(l => l.AnnualRate).HasPrecision(9, 4);
        builder.Property(l => l.EffectiveAnnualRate).HasPrecision(9, 4);
        builder.Property(l => l.EarlyRepaymentPenaltyPercent).HasPrecision(9, 4);
        builder.Property(l => l.Status).HasMaxLength(20);

        builder.HasMany(l => l.Installments).WithOne().HasForeignKey("LoanId").OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(l => l.Repayments).WithOne().HasForeignKey("LoanId").OnDelete(DeleteBehavior.Cascade);

        // Reports: portfolio by product.
        builder.HasIndex(l => new { l.ProductId, l.Status });
    }
}

internal sealed class LoanInstallmentConfiguration : IEntityTypeConfiguration<LoanInstallment>
{
    public void Configure(EntityTypeBuilder<LoanInstallment> builder)
    {
        builder.ToTable("LoanInstallments");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property<LoanId>("LoanId");
        builder.HasIndex("LoanId", nameof(LoanInstallment.Number)).IsUnique();
        builder.Ignore(i => i.Total);
        MapMoney(builder.ComplexProperty(i => i.Principal), "Principal", "Currency");
        MapMoney(builder.ComplexProperty(i => i.Interest), "Interest", "InterestCurrency");
        MapMoney(builder.ComplexProperty(i => i.Fee), "Fee", "FeeCurrency");
    }

    internal static void MapMoney<T>(ComplexPropertyBuilder<T> money, string amountColumn, string currencyColumn)
    {
        money.Property("Amount").HasColumnName(amountColumn).HasPrecision(18, 2);
        money.Property("Currency").HasColumnName(currencyColumn);
    }
}

internal sealed class LoanRepaymentConfiguration : IEntityTypeConfiguration<LoanRepayment>
{
    public void Configure(EntityTypeBuilder<LoanRepayment> builder)
    {
        builder.ToTable("LoanRepayments");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property<LoanId>("LoanId");
        builder.HasIndex("LoanId", nameof(LoanRepayment.PaidOn));
        builder.Property(r => r.Kind).HasMaxLength(20);
        builder.Ignore(r => r.Total);
        builder.HasOne<StaffMember>().WithMany().HasForeignKey(r => r.RecordedBy).OnDelete(DeleteBehavior.Restrict);
        LoanInstallmentConfiguration.MapMoney(builder.ComplexProperty(r => r.Principal), "Principal", "Currency");
        LoanInstallmentConfiguration.MapMoney(builder.ComplexProperty(r => r.Interest), "Interest", "InterestCurrency");
        LoanInstallmentConfiguration.MapMoney(builder.ComplexProperty(r => r.Fee), "Fee", "FeeCurrency");
        LoanInstallmentConfiguration.MapMoney(builder.ComplexProperty(r => r.Penalty), "Penalty", "PenaltyCurrency");
    }
}

/// <summary>The counter row per year behind the gapless contract numbers.</summary>
internal sealed class ContractNumberSequence
{
    public int Year { get; set; }

    public int LastNumber { get; set; }
}

internal sealed class ContractNumberSequenceConfiguration : IEntityTypeConfiguration<ContractNumberSequence>
{
    public void Configure(EntityTypeBuilder<ContractNumberSequence> builder)
    {
        builder.ToTable("ContractNumberSequences");
        builder.HasKey(s => s.Year);
        builder.Property(s => s.Year).ValueGeneratedNever();
    }
}

internal sealed class MockCoreBankingTransactionConfiguration : IEntityTypeConfiguration<MockCoreBankingTransaction>
{
    public void Configure(EntityTypeBuilder<MockCoreBankingTransaction> builder)
    {
        builder.ToTable("MockCoreBankingTransactions");
        builder.HasKey(t => t.IdempotencyKey);
        builder.Property(t => t.IdempotencyKey).HasMaxLength(64);
        builder.Property(t => t.TransactionId).HasMaxLength(Disbursement.TransactionIdMaxLength);
        builder.HasIndex(t => t.TransactionId).IsUnique();
        builder.Property(t => t.Beneficiary).HasMaxLength(PersonalNumber.Length);
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.Currency).HasMaxLength(3);
        builder.Property(t => t.Reference).HasMaxLength(20);
    }
}

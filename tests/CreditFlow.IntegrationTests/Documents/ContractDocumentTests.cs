using CreditFlow.Application.Disbursements;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Loans;
using CreditFlow.Domain.Products;
using CreditFlow.Infrastructure.Documents;
using MigraDoc.DocumentObjectModel.IO;

namespace CreditFlow.IntegrationTests.Documents;

/// <summary>The contract and schedule documents: their content (MigraDoc model) and that they render to PDF.</summary>
public sealed class ContractDocumentTests
{
    [Fact]
    public void The_contract_names_the_number_the_parties_the_costs_and_the_conditions()
    {
        var ddl = DdlWriter.WriteToString(ContractDocumentBuilder.Contract(Data("LN-2026-000123")));

        Assert.Contains("LN-2026-000123", ddl, StringComparison.Ordinal);
        Assert.Contains("ნინო ბერიძე", ddl, StringComparison.Ordinal);
        Assert.Contains("01001011001", ddl, StringComparison.Ordinal);
        Assert.Contains("Guarantor", ddl, StringComparison.Ordinal);
        Assert.Contains("10,000.00 GEL", ddl, StringComparison.Ordinal);
        Assert.Contains("Effective annual rate incl. fees", ddl, StringComparison.Ordinal);
        Assert.Contains("19.85 %", ddl, StringComparison.Ordinal);
        Assert.Contains("Life insurance policy assigned to the bank", ddl, StringComparison.Ordinal);
    }

    [Fact]
    public void Before_signing_the_contract_says_the_number_comes_with_the_signature()
    {
        var ddl = DdlWriter.WriteToString(ContractDocumentBuilder.Contract(Data(null)));

        Assert.Contains("assigned at signing", ddl, StringComparison.Ordinal);
        Assert.Contains("projected", ddl, StringComparison.Ordinal);
    }

    [Fact]
    public void The_schedule_lists_every_installment_and_totals_the_principal_exactly()
    {
        var data = Data("LN-2026-000123");

        var ddl = DdlWriter.WriteToString(ContractDocumentBuilder.Schedule(data));

        foreach (var row in data.Schedule)
        {
            Assert.Contains(row.DueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ddl, StringComparison.Ordinal);
        }

        Assert.Contains("10,000.00", ddl, StringComparison.Ordinal); // the principal column's total
        Assert.Equal(10_000m, data.Schedule.Sum(r => r.Principal.Amount));
    }

    [Fact]
    public void Both_documents_render_to_pdf_with_the_embedded_fonts()
    {
        var renderer = new PdfContractRenderer();
        var data = Data("LN-2026-000123");

        foreach (var pdf in new[] { renderer.RenderContract(data), renderer.RenderSchedule(data) })
        {
            Assert.Equal("%PDF-"u8.ToArray(), pdf[..5]);
            Assert.True(pdf.Length > 5_000);
        }
    }

    private static ContractDocumentData Data(string? contractNumber)
    {
        var amount = Money.Of(10_000m, Currency.GEL);
        var schedule = ScheduleCalculator.Build(amount, 18m, 12, ScheduleType.Annuity, new DateOnly(2026, 10, 12), Money.Zero(Currency.GEL));
        var remaining = amount;
        var rows = new List<ScheduleRow>();
        foreach (var i in schedule.Installments)
        {
            remaining -= i.Principal;
            rows.Add(new ScheduleRow(i.Number, i.DueDate, i.Principal, i.Interest, i.Fee, i.Total, remaining));
        }

        return new ContractDocumentData
        {
            ContractNumber = contractNumber,
            ApplicationReference = "0199A1B2",
            IssuedOn = new DateOnly(2026, 10, 12),
            BranchName = "Vake",
            ProductName = "Consumer loan",
            ProductVersion = 2,
            Parties =
            [
                new ContractParty("ნინო ბერიძე", "01001011001", PartyRole.Borrower, "IdCard 12AB34567, valid until 2031-01-01", "Tbilisi, Vazha-Pshavela 10"),
                new ContractParty("Giorgi Abashidze", "01001011020", PartyRole.Guarantor, "Passport P1234567, valid until 2030-05-01", "Batumi, Rustaveli 5"),
            ],
            Terms = LoanTerms.Create(amount, 12, 18m, ScheduleType.Annuity).Value,
            UpfrontFee = amount.Percent(0.5m),
            MonthlyFee = Money.Zero(Currency.GEL),
            EffectiveAnnualRate = 19.85m,
            EarlyRepaymentPenaltyPercent = 2m,
            Conditions = ["Life insurance policy assigned to the bank"],
            Collateral = [],
            Schedule = rows,
            ScheduleIsProjected = contractNumber is null,
        };
    }
}

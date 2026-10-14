using System.Globalization;
using System.Reflection;
using CreditFlow.Application.Disbursements;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace CreditFlow.Infrastructure.Documents;

/// <summary>
/// PDFs with PDFsharp-MigraDoc (MIT). The documents are built as a MigraDoc object model
/// (<see cref="ContractDocumentBuilder"/>) and rendered here; fonts come from the assembly, never from the machine.
/// </summary>
internal sealed class PdfContractRenderer : IContractDocumentRenderer
{
    static PdfContractRenderer() => GlobalFontSettings.FontResolver = new EmbeddedFontResolver();

    public byte[] RenderContract(ContractDocumentData data) => Render(ContractDocumentBuilder.Contract(data));

    public byte[] RenderSchedule(ContractDocumentData data) => Render(ContractDocumentBuilder.Schedule(data));

    private static byte[] Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }
}

/// <summary>Builds the contract and the schedule documents (no rendering, so their content can be tested).</summary>
internal static class ContractDocumentBuilder
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static Document Contract(ContractDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var document = NewDocument($"Loan contract {Number(data)}");
        var section = NewSection(document, data);

        Heading(section, "Loan contract", 16);
        section.AddParagraph($"Contract number: {Number(data)}");
        section.AddParagraph($"Application {data.ApplicationReference} · {data.BranchName} branch · issued {Date(data.IssuedOn)}");

        Heading(section, "1. Parties", 11);
        var parties = NewTable(section, [5.5, 3, 3, 5.5]);
        Header(parties, "Name", "Personal number", "Role", "Identity document");
        foreach (var party in data.Parties)
        {
            Cells(parties.AddRow(), party.Name, party.PersonalNumber, RoleName(party.Role), party.IdentityDocument);
        }

        section.AddParagraph($"Addresses: {string.Join("; ", data.Parties.Select(p => $"{p.Name}: {p.Address}"))}").Format.SpaceBefore = Unit.FromPoint(4);

        Heading(section, "2. Loan", 11);
        var loan = NewTable(section, [7, 10]);
        var terms = data.Terms;
        foreach (var (label, value) in new[]
        {
            ("Product", $"{data.ProductName} (version {data.ProductVersion}, fixed at submission)"),
            ("Amount", Amount(terms.Amount)),
            ("Term", $"{terms.TermMonths} months"),
            ("Annual interest rate", Percent(terms.AnnualRate)),
            ("Repayment schedule", terms.ScheduleType == ScheduleType.Annuity ? "Annuity (equal installments)" : "Differentiated (equal principal parts)"),
            ("Upfront fee", $"{Amount(data.UpfrontFee)} (deducted from the payout)"),
            ("Monthly service fee", Amount(data.MonthlyFee)),
            ("Effective annual rate incl. fees", Percent(data.EffectiveAnnualRate)),
            ("Early repayment penalty", $"{Percent(data.EarlyRepaymentPenaltyPercent)} of the principal repaid early"),
        })
        {
            Cells(loan.AddRow(), label, value);
        }

        if (data.Collateral.Count > 0)
        {
            Heading(section, "3. Collateral", 11);
            foreach (var item in data.Collateral)
            {
                section.AddParagraph($"• {item}");
            }
        }

        Heading(section, data.Collateral.Count > 0 ? "4. Conditions before disbursement" : "3. Conditions before disbursement", 11);
        if (data.Conditions.Count == 0)
        {
            section.AddParagraph("None.");
        }

        foreach (var condition in data.Conditions)
        {
            section.AddParagraph($"• {condition}");
        }

        Heading(section, "Repayment", 11);
        section.AddParagraph(
            $"{data.Schedule.Count} monthly installments, the first due {Date(data.Schedule[0].DueDate)}. Rounding differences are " +
            "settled in the last installment, so the principal parts add up exactly to the loan amount. The schedule is attached" +
            (data.ScheduleIsProjected ? " (projected; the final schedule runs from the disbursement day)." : "."));

        Heading(section, "Signatures", 11);
        var signatures = NewTable(section, [8.5, 8.5]);
        signatures.Borders.Visible = false;
        foreach (var party in data.Parties)
        {
            Cells(signatures.AddRow(), $"{party.Name} ({RoleName(party.Role)})", "____________________________");
        }

        Cells(signatures.AddRow(), "For the bank", "____________________________");
        return document;
    }

    public static Document Schedule(ContractDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var document = NewDocument($"Repayment schedule {Number(data)}");
        var section = NewSection(document, data);

        Heading(section, "Repayment schedule", 16);
        section.AddParagraph($"Contract {Number(data)} · {data.Parties.FirstOrDefault(p => p.Role == PartyRole.Borrower)?.Name}");
        section.AddParagraph(
            $"{Amount(data.Terms.Amount)} · {data.Terms.TermMonths} months · {Percent(data.Terms.AnnualRate)} · " +
            $"effective annual rate incl. fees {Percent(data.EffectiveAnnualRate)}" +
            (data.ScheduleIsProjected ? " · projected from today" : string.Empty));

        var table = NewTable(section, [1.2, 2.6, 2.7, 2.6, 2, 2.9, 3]);
        Header(table, "#", "Due date", "Principal", "Interest", "Fee", "Installment", "Remaining");
        foreach (var row in data.Schedule)
        {
            Cells(table.AddRow(), row.Number.ToString(Culture), Date(row.DueDate), Number(row.Principal), Number(row.Interest),
                Number(row.Fee), Number(row.Total), Number(row.Remaining));
        }

        var currency = data.Terms.Amount.Currency;
        Money Sum(Func<ScheduleRow, Money> part) => data.Schedule.Select(part).Aggregate(Money.Zero(currency), (a, b) => a + b);
        var totals = table.AddRow();
        totals.Format.Font.Bold = true;
        Cells(totals, string.Empty, "Total", Number(Sum(r => r.Principal)), Number(Sum(r => r.Interest)), Number(Sum(r => r.Fee)),
            Number(Sum(r => r.Total)), string.Empty);
        return document;
    }

    private static Document NewDocument(string title)
    {
        var document = new Document();
        document.Info.Title = title;
        document.Info.Author = "CreditFlow";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedFontResolver.FamilyName;
        normal.Font.Size = 9;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);
        return document;
    }

    private static Section NewSection(Document document, ContractDocumentData data)
    {
        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(2);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromCentimeter(2);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7;
        footer.AddText($"CreditFlow · {Number(data)} · page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
        return section;
    }

    private static void Heading(Section section, string text, int size)
    {
        var paragraph = section.AddParagraph(text);
        paragraph.Format.Font.Size = size;
        paragraph.Format.Font.Bold = true;
        paragraph.Format.SpaceBefore = Unit.FromPoint(size > 12 ? 0 : 10);
        paragraph.Format.SpaceAfter = Unit.FromPoint(4);
    }

    private static Table NewTable(Section section, double[] columnsCm)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.4;
        table.Borders.Color = Colors.Gray;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(3);
        foreach (var width in columnsCm)
        {
            table.AddColumn(Unit.FromCentimeter(width));
        }

        return table;
    }

    private static void Header(Table table, params string[] titles)
    {
        var row = table.AddRow();
        row.HeadingFormat = true;
        row.Format.Font.Bold = true;
        row.Shading.Color = Colors.WhiteSmoke;
        Cells(row, titles);
    }

    private static void Cells(Row row, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            row.Cells[i].AddParagraph(values[i]);
        }
    }

    private static string Number(ContractDocumentData data) => data.ContractNumber ?? "assigned at signing";

    private static string Number(Money money) => money.Amount.ToString("#,0.00", Culture);

    private static string Amount(Money money) => $"{Number(money)} {money.Currency}";

    private static string Percent(decimal value) => value.ToString("0.##", Culture) + " %";

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", Culture);

    private static string RoleName(PartyRole role) => role switch
    {
        PartyRole.Borrower => "Borrower",
        PartyRole.CoBorrower => "Co-borrower",
        _ => "Guarantor",
    };
}

/// <summary>
/// Serves the embedded DejaVu Sans faces (regular and bold) for every font family, so PDFs look the same on every
/// machine and carry Latin and Georgian glyphs. Italic is simulated.
/// </summary>
internal sealed class EmbeddedFontResolver : IFontResolver
{
    public const string FamilyName = "DejaVu Sans";

    private const string Regular = "DejaVuSans";
    private const string Bold = "DejaVuSans-Bold";

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? Bold : Regular, mustSimulateBold: false, mustSimulateItalic: isItalic);

    public byte[] GetFont(string faceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"CreditFlow.Fonts.{faceName}.ttf")
                           ?? throw new InvalidOperationException($"The font {faceName} is not embedded.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

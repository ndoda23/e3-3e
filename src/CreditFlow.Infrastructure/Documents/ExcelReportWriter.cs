using System.Globalization;
using ClosedXML.Excel;
using CreditFlow.Application.Reports;

namespace CreditFlow.Infrastructure.Documents;

/// <summary>
/// The reports as an Excel workbook (ClosedXML, MIT): one sheet per report with its title, the period, a header row and
/// typed cells (numbers stay numbers). Text is written as text, so nothing in the data can become a formula. Column
/// widths are fixed rather than measured, so no system fonts are needed.
/// </summary>
internal sealed class ExcelReportWriter : IReportExcelWriter
{
    private const int HeaderRow = 4;

    public byte[] Write(IReadOnlyList<ReportTable> tables, ReportFilter filter)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(filter);

        using var workbook = new XLWorkbook();
        foreach (var table in tables)
        {
            var sheet = workbook.Worksheets.Add(table.Kind.ToString());
            sheet.Cell(1, 1).Value = table.Title;
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(2, 1).Value = string.Create(CultureInfo.InvariantCulture, $"Period {filter.From:yyyy-MM-dd} to {filter.To:yyyy-MM-dd}");

            for (var c = 0; c < table.Columns.Count; c++)
            {
                var header = sheet.Cell(HeaderRow, c + 1);
                header.Value = table.Columns[c].Title;
                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.WhiteSmoke;
                sheet.Column(c + 1).Width = table.Columns[c].Kind == ReportColumnKind.Text ? 36 : 18;
            }

            for (var r = 0; r < table.Rows.Count; r++)
            {
                for (var c = 0; c < table.Columns.Count; c++)
                {
                    Write(sheet.Cell(HeaderRow + 1 + r, c + 1), table.Rows[r][c], table.Columns[c].Kind);
                }
            }

            sheet.SheetView.FreezeRows(HeaderRow);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Write(IXLCell cell, object value, ReportColumnKind kind)
    {
        switch (value)
        {
            case int number:
                cell.Value = number;
                break;
            case decimal number:
                cell.Value = (double)number;
                cell.Style.NumberFormat.Format = kind == ReportColumnKind.Amount ? "#,##0.00" : "0.0";
                break;
            default:
                cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                break;
        }
    }
}

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace WunderkindLC.Application.Services.FinanceReports2;

/// <summary>
/// Uslubli .xlsx (moliya hisobotlari eksporti): sonlar HAQIQIY son katakda <c>#,##0</c> formatida (Excel'da
/// qo'shish/filtrlash ishlaydi), ekrandagi ranglar — yashil kirim, qizil chiqim, sariq sof. Matn kataklari
/// <see cref="ExcelExport"/> dagi kabi formula injeksiyasidan himoyalangan.
/// </summary>
public static class StyledExcel
{
    public enum Style : uint
    {
        Text = 0, Bold = 1, Number = 2, BoldNumber = 3,
        IncomeLabel = 4, IncomeNumber = 5, ExpenseLabel = 6, ExpenseNumber = 7, NetLabel = 8, NetNumber = 9,
        Section = 10, Header = 11, Indent = 12, Title = 13, Muted = 14,
    }

    public readonly record struct Cell(string? Text, decimal? Number, Style Style)
    {
        public static Cell T(string? text, Style s = Style.Text) => new(text, null, s);
        public static Cell N(decimal n, Style s = Style.Number) => new(null, n, s);
    }

    public sealed record SheetSpec(string Name, IReadOnlyList<IReadOnlyList<Cell>> Rows, IReadOnlyList<double> ColumnWidths,
        IReadOnlyList<(int Row, int FromCol, int ToCol)>? Merges = null, int FreezeColumns = 1, int FreezeRows = 0);

    public static byte[] Build(SheetSpec spec)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wb = doc.AddWorkbookPart();
            wb.Workbook = new Workbook();
            var styles = wb.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = Stylesheet();
            styles.Stylesheet.Save();

            var ws = wb.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            var worksheet = new Worksheet();

            if (spec.FreezeColumns > 0 || spec.FreezeRows > 0)
            {
                var topLeft = $"{ColName(spec.FreezeColumns)}{spec.FreezeRows + 1}";
                var pane = new Pane
                {
                    HorizontalSplit = spec.FreezeColumns > 0 ? spec.FreezeColumns : null,
                    VerticalSplit = spec.FreezeRows > 0 ? spec.FreezeRows : null,
                    TopLeftCell = topLeft,
                    ActivePane = spec.FreezeRows > 0 && spec.FreezeColumns > 0 ? PaneValues.BottomRight
                        : spec.FreezeRows > 0 ? PaneValues.BottomLeft : PaneValues.TopRight,
                    State = PaneStateValues.Frozen,
                };
                worksheet.Append(new SheetViews(new SheetView(pane) { WorkbookViewId = 0 }));
            }

            var cols = new Columns();
            for (var i = 0; i < spec.ColumnWidths.Count; i++)
                cols.Append(new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = spec.ColumnWidths[i], CustomWidth = true });
            worksheet.Append(cols);
            worksheet.Append(data);

            uint r = 1;
            foreach (var row in spec.Rows)
            {
                var xr = new Row { RowIndex = r };
                for (var c = 0; c < row.Count; c++)
                {
                    var cell = row[c];
                    var x = new DocumentFormat.OpenXml.Spreadsheet.Cell { CellReference = $"{ColName(c)}{r}", StyleIndex = (uint)cell.Style };
                    if (cell.Number is { } n)
                    {
                        x.DataType = CellValues.Number;
                        x.CellValue = new CellValue(n);
                    }
                    else if (!string.IsNullOrEmpty(cell.Text))
                    {
                        x.DataType = CellValues.InlineString;
                        x.InlineString = new InlineString(new Text(Safe(cell.Text)) { Space = SpaceProcessingModeValues.Preserve });
                    }
                    xr.Append(x);
                }
                data.Append(xr);
                r++;
            }

            if (spec.Merges is { Count: > 0 })
            {
                var merges = new MergeCells();
                foreach (var (row, from, to) in spec.Merges)
                    merges.Append(new MergeCell { Reference = $"{ColName(from)}{row}:{ColName(to)}{row}" });
                worksheet.Append(merges);
            }

            ws.Worksheet = worksheet;
            wb.Workbook.AppendChild(new Sheets()).Append(new Sheet
            {
                Id = wb.GetIdOfPart(ws), SheetId = 1, Name = spec.Name.Length > 31 ? spec.Name[..31] : spec.Name,
            });
            wb.Workbook.Save();
        }
        return ms.ToArray();
    }

    public static string ColName(int index)
    {
        var s = "";
        index++;
        while (index > 0)
        {
            var m = (index - 1) % 26;
            s = (char)('A' + m) + s;
            index = (index - m - 1) / 26;
        }
        return s;
    }

    private static string Safe(string v) => "=+-@\t\r".IndexOf(v[0]) >= 0 ? "'" + v : v;

    private static Stylesheet Stylesheet()
    {
        Fill Solid(string rgb) => new(new PatternFill(new ForegroundColor { Rgb = rgb }) { PatternType = PatternValues.Solid });
        var fonts = new Fonts(
            new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 14 }, new FontName { Val = "Calibri" }),
            new Font(new Color { Rgb = "FF6B7280" }, new FontSize { Val = 10 }, new FontName { Val = "Calibri" }));
        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            Solid("FFE8F8EE"), // 2 yashil (kirim)
            Solid("FFFDECEB"), // 3 qizil (chiqim)
            Solid("FFFFF6E5"), // 4 sariq (sof)
            Solid("FFF5F6F7")); // 5 kulrang (bo'lim/sarlavha)
        var borders = new Borders(
            new Border(),
            new Border(
                new LeftBorder(), new RightBorder(), new TopBorder(),
                new BottomBorder(new Color { Rgb = "FFE0E0E0" }) { Style = BorderStyleValues.Thin }, new DiagonalBorder()));
        var numFmts = new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0" });

        CellFormat F(uint font, uint fill, bool num, uint border = 1, HorizontalAlignmentValues? align = null, uint indent = 0)
        {
            var cf = new CellFormat
            {
                FontId = font, FillId = fill, BorderId = border, NumberFormatId = num ? 164u : 0u,
                ApplyFont = true, ApplyFill = fill > 0, ApplyBorder = true, ApplyNumberFormat = num,
            };
            if (align is not null || indent > 0)
            {
                cf.ApplyAlignment = true;
                cf.Alignment = new Alignment { Horizontal = align, Indent = indent > 0 ? indent : null, Vertical = VerticalAlignmentValues.Center };
            }
            return cf;
        }

        var formats = new CellFormats(
            F(0, 0, false),               // 0 Text
            F(1, 0, false),               // 1 Bold
            F(0, 0, true),                // 2 Number
            F(1, 0, true),                // 3 BoldNumber
            F(1, 2, false),               // 4 IncomeLabel
            F(1, 2, true),                // 5 IncomeNumber
            F(1, 3, false),               // 6 ExpenseLabel
            F(1, 3, true),                // 7 ExpenseNumber
            F(1, 4, false),               // 8 NetLabel
            F(1, 4, true),                // 9 NetNumber
            F(1, 5, false),               // 10 Section
            F(1, 5, false, 1, HorizontalAlignmentValues.Center), // 11 Header
            F(0, 0, false, 1, null, 2),   // 12 Indent
            F(2, 0, false, 0),            // 13 Title
            F(3, 0, false, 0));           // 14 Muted

        return new Stylesheet(numFmts, fonts, fills, borders, new CellStyleFormats(new CellFormat()), formats);
    }
}

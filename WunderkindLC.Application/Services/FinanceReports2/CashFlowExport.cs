using static WunderkindLC.Application.Services.FinanceReports2.StyledExcel;

namespace WunderkindLC.Application.Services.FinanceReports2;

/// <summary>Pul oqimi 2.0 → .xlsx — ekrandagi jadval AYNAN shunday (bir xil qatorlar, ranglar, Jami ustuni).</summary>
public static class CashFlowExport
{
    public static string FileName(int year, int? month) =>
        month is >= 1 and <= 12 ? $"pul-oqimi-{year:0000}-{month:00}.xlsx" : $"pul-oqimi-{year:0000}.xlsx";

    public static byte[] Year(CashFlowReport.YearDto y)
    {
        var rows = new List<IReadOnlyList<Cell>>();
        var merges = new List<(int, int, int)>();
        const int cols = 14; // ko'rsatkich + 12 oy + jami

        rows.Add([Cell.T($"Pul oqimi hisoboti — {y.Year}", Style.Title)]);
        rows.Add([Cell.T("Summalar so'mda. Kassadan kassaga ko'chirish va bekor qilingan amallar kirmaydi.", Style.Muted)]);
        rows.Add([]);
        var header = new List<Cell> { Cell.T("Ko'rsatkich", Style.Header) };
        header.AddRange(y.Months.Select(m => Cell.T(m, Style.Header)));
        header.Add(Cell.T("Jami", Style.Header));
        rows.Add(header);

        void Line(string label, decimal[] data, decimal total, Style l, Style n) =>
            rows.Add([Cell.T(label, l), .. data.Select(v => Cell.N(v, n)), Cell.N(total, n)]);

        Line("Boshlang'ich qoldiq", y.StartBalances, y.StartBalances[0], Style.Bold, Style.BoldNumber);
        foreach (var s in y.Sections)
        {
            if (s.In.Count == 0 && s.Out.Count == 0) continue;
            rows.Add([Cell.T(s.Title, Style.Section), .. Enumerable.Repeat(Cell.T("", Style.Section), cols - 1)]);
            merges.Add((rows.Count, 0, cols - 1));
            Line("Kirim — Jami", s.TotalIn, s.TotalIn.Sum(), Style.IncomeLabel, Style.IncomeNumber);
            foreach (var l in s.In) Line(l.Name, l.Data, l.Total, Style.Indent, Style.Number);
            Line("Chiqim — Jami", s.TotalOut, s.TotalOut.Sum(), Style.ExpenseLabel, Style.ExpenseNumber);
            foreach (var l in s.Out) Line(l.Name, l.Data, l.Total, Style.Indent, Style.Number);
            Line("Sof", s.Net, s.Net.Sum(), Style.NetLabel, Style.NetNumber);
        }
        Line("Sof pul oqimi", y.NetCashFlow, y.NetCashFlow.Sum(), Style.NetLabel, Style.NetNumber);
        Line("Yakuniy qoldiq", y.EndBalances, y.EndBalances[11], Style.Bold, Style.BoldNumber);

        var widths = new List<double> { 38 };
        widths.AddRange(Enumerable.Repeat(15.0, 13));
        return Build(new SheetSpec($"Pul oqimi {y.Year}", rows, widths, merges, FreezeColumns: 1, FreezeRows: 4));
    }

    public static byte[] Month(CashFlowReport.MonthDto m)
    {
        var rows = new List<IReadOnlyList<Cell>>
        {
            new[] { Cell.T($"Pul oqimi hisoboti — {m.MonthName} {m.Year}", Style.Title) },
            new[] { Cell.T("Summalar so'mda.", Style.Muted) },
            Array.Empty<Cell>(),
            new[] { Cell.T("Ko'rsatkich", Style.Header), Cell.T("Summa", Style.Header) },
            new[] { Cell.T("Boshlang'ich qoldiq", Style.Bold), Cell.N(m.StartBalance, Style.BoldNumber) },
        };
        foreach (var s in m.Sections)
        {
            if (s.CashIn.Categories.Count == 0 && s.CashOut.Categories.Count == 0) continue;
            rows.Add([Cell.T(s.Title, Style.Section), Cell.T("", Style.Section)]);
            rows.Add([Cell.T("Kirim — Jami", Style.IncomeLabel), Cell.N(s.CashIn.Total, Style.IncomeNumber)]);
            rows.AddRange(s.CashIn.Categories.Select(c => (IReadOnlyList<Cell>)[Cell.T(c.Name, Style.Indent), Cell.N(c.Amount)]));
            rows.Add([Cell.T("Chiqim — Jami", Style.ExpenseLabel), Cell.N(s.CashOut.Total, Style.ExpenseNumber)]);
            rows.AddRange(s.CashOut.Categories.Select(c => (IReadOnlyList<Cell>)[Cell.T(c.Name, Style.Indent), Cell.N(c.Amount)]));
            rows.Add([Cell.T("Sof", Style.NetLabel), Cell.N(s.Net, Style.NetNumber)]);
        }
        rows.Add([Cell.T("Sof pul oqimi", Style.NetLabel), Cell.N(m.NetCashFlow, Style.NetNumber)]);
        rows.Add([Cell.T("Yakuniy qoldiq", Style.Bold), Cell.N(m.EndBalance, Style.BoldNumber)]);
        return Build(new SheetSpec($"Pul oqimi {m.Year}-{m.Month:00}", rows, [40, 18], FreezeColumns: 0, FreezeRows: 4));
    }
}

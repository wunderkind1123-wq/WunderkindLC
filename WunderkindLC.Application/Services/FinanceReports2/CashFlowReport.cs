using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.FinanceReports2;

/// <summary>
/// PUL OQIMI 2.0 (to'g'ridan-to'g'ri usul, <c>docs/finance-reports/DECISIONS.md</c> D10–D13).
///
/// <list type="bullet">
/// <item>Manba — haqiqiy kassa amallari (<c>FinanceTransaction</c>). Bekor qilinganlar global filtr bilan tushadi,
/// kassadan kassaga ko'chirish alohida jadvalda — kirim ham, chiqim ham emas (hisobotga KIRMAYDI).</item>
/// <item>Har amal o'z qatorining faoliyatiga (<see cref="FinanceCategoryRules.Activities"/>) tushadi; <c>excluded</c> —
/// umuman kirmaydi.</item>
/// <item>Sana — amalning <c>Date</c> ("yyyy-MM-dd", markaz vaqti bo'yicha saqlangan kun) — vaqt zonasi o'girilmaydi.</item>
/// <item>Yil boshidagi qoldiq — <see cref="CashboxService.YearCarry"/> (kassa boshlang'ich qoldiqlari bilan), faqat
/// <c>excluded</c> bo'lmagan qatorlar.</item>
/// </list>
/// Hisobning o'zi — <see cref="Build"/> (sof, <c>CashFlowReportTests</c>).
/// </summary>
public static class CashFlowReport
{
    public static readonly string[] MonthNames =
        ["Yanvar", "Fevral", "Mart", "Aprel", "May", "Iyun", "Iyul", "Avgust", "Sentabr", "Oktabr", "Noyabr", "Dekabr"];

    public readonly record struct Row(string Date, string Direction, decimal Amount, string Key);

    public sealed record LineDto(string RowKey, string Name, decimal[] Data, decimal Total);
    public sealed record SectionDto(
        string Key, string Title, List<LineDto> In, List<LineDto> Out,
        decimal[] TotalIn, decimal[] TotalOut, decimal[] Net);
    public sealed record ReconciliationDto(decimal CashboxBalanceNow, decimal ReportBalanceNow, decimal Difference, bool Applicable);
    public sealed record YearDto(
        int Year, string Currency, string[] Months, int CurrentMonth, decimal InitialBalance,
        List<SectionDto> Sections, decimal[] StartBalances, decimal[] EndBalances, decimal[] NetCashFlow,
        ReconciliationDto Reconciliation, int FirstYear);

    public sealed record MonthLineDto(string RowKey, string Name, decimal Amount);
    public sealed record MonthSideDto(decimal Total, List<MonthLineDto> Categories);
    public sealed record MonthSectionDto(string Key, string Title, MonthSideDto CashIn, MonthSideDto CashOut, decimal Net);
    public sealed record MonthDto(
        int Year, int Month, string MonthName, decimal StartBalance, decimal EndBalance,
        List<MonthSectionDto> Sections, decimal NetCashFlow, ReconciliationDto Reconciliation, int FirstYear);

    private static readonly (string Key, string Title)[] SectionTitles =
    [
        (FinanceCategoryRules.Operating, "Operatsion faoliyat"),
        (FinanceCategoryRules.Investing, "Investitsiya faoliyati"),
        (FinanceCategoryRules.Financing, "Moliyalashtirish faoliyati"),
    ];

    /// <summary>
    /// Yillik jadval (sof). <paramref name="rows"/> — shu YIL amallari; <paramref name="initialBalance"/> — yil boshidagi
    /// qoldiq. Qator tartibi bo'lim ichida: SortOrder, keyin yillik summa kamayishi; yil bo'yi 0 bo'lgan qator yo'q.
    /// </summary>
    public static YearDto Build(
        int year, IEnumerable<Row> rows, FinanceCategoryCatalog.Catalog catalog, decimal initialBalance,
        int currentMonth, ReconciliationDto reconciliation, int firstYear)
    {
        // (bo'lim, yo'nalish, kalit) → 12 oy
        var cells = new Dictionary<(string Act, string Dir, string Key), decimal[]>();
        foreach (var r in rows)
        {
            if (r.Date.Length < 7 || !int.TryParse(r.Date.AsSpan(5, 2), out var m) || m is < 1 or > 12) continue;
            if (!r.Date.StartsWith($"{year:0000}-", StringComparison.Ordinal)) continue;
            var e = catalog.Entries.TryGetValue(r.Key, out var en) ? en : null;
            var act = e?.Activity ?? FinanceCategoryRules.Operating;
            if (act == FinanceCategoryRules.ExcludedActivity) continue;
            var dir = r.Direction == "expense" ? "expense" : "income";
            var k = (act, dir, r.Key);
            if (!cells.TryGetValue(k, out var arr)) cells[k] = arr = new decimal[12];
            arr[m - 1] += r.Amount;
        }

        var sections = new List<SectionDto>();
        var net = new decimal[12];
        foreach (var (key, title) in SectionTitles)
        {
            List<LineDto> Lines(string dir) => cells
                .Where(c => c.Key.Act == key && c.Key.Dir == dir && c.Value.Any(v => v != 0))
                .Select(c =>
                {
                    var e = catalog.Entries.TryGetValue(c.Key.Key, out var en) ? en : null;
                    return (Line: new LineDto(c.Key.Key, e?.Name ?? c.Key.Key, c.Value, c.Value.Sum()), Order: e?.SortOrder ?? 1000);
                })
                .OrderBy(x => x.Order).ThenByDescending(x => x.Line.Total).ThenBy(x => x.Line.Name, StringComparer.Ordinal)
                .Select(x => x.Line).ToList();
            var ins = Lines("income");
            var outs = Lines("expense");
            var tIn = Sum12(ins);
            var tOut = Sum12(outs);
            var sNet = new decimal[12];
            for (var i = 0; i < 12; i++) { sNet[i] = tIn[i] - tOut[i]; net[i] += sNet[i]; }
            sections.Add(new SectionDto(key, title, ins, outs, tIn, tOut, sNet));
        }

        var start = new decimal[12];
        var end = new decimal[12];
        var bal = initialBalance;
        for (var i = 0; i < 12; i++)
        {
            start[i] = bal;
            bal += net[i];
            end[i] = bal;
        }
        return new YearDto(year, "UZS", MonthNames, currentMonth, initialBalance, sections, start, end, net, reconciliation, firstYear);
    }

    private static decimal[] Sum12(IEnumerable<LineDto> lines)
    {
        var s = new decimal[12];
        foreach (var l in lines) for (var i = 0; i < 12; i++) s[i] += l.Data[i];
        return s;
    }

    /// <summary>Oylik rejim — yillik jadvalning BITTA ustuni (raqamlar ikki xil chiqmasin). Nol qatorlar yashiriladi,
    /// turlar summa kamayishi bo'yicha.</summary>
    public static MonthDto ToMonth(YearDto y, int month)
    {
        var i = month - 1;
        var sections = y.Sections.Select(s =>
        {
            MonthSideDto Side(List<LineDto> lines, decimal[] total) => new(total[i],
                lines.Where(l => l.Data[i] != 0).Select(l => new MonthLineDto(l.RowKey, l.Name, l.Data[i]))
                    .OrderByDescending(l => l.Amount).ThenBy(l => l.Name, StringComparer.Ordinal).ToList());
            return new MonthSectionDto(s.Key, s.Title, Side(s.In, s.TotalIn), Side(s.Out, s.TotalOut), s.Net[i]);
        }).ToList();
        return new MonthDto(y.Year, month, MonthNames[i], y.StartBalances[i], y.EndBalances[i], sections,
            y.NetCashFlow[i], y.Reconciliation, y.FirstYear);
    }

    /// <summary>Bazadan yuklab, yillik jadvalni quradi.</summary>
    public static async Task<YearDto> BuildAsync(IAppDbContext db, int year, CancellationToken ct = default)
    {
        await FinanceCategoryCatalog.EnsureAsync(db, ct);
        var catalog = await FinanceCategoryCatalog.LoadAsync(db, ct);
        var from = $"{year:0000}-01-01";
        var to = $"{year:0000}-12-31";

        var all = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.Date.CompareTo(to) <= 0)
            .Select(t => new { t.Date, t.Direction, t.Amount, t.TypeId, t.Category, t.CashboxId })
            .ToListAsync(ct);
        bool Counted(string? typeId, string dir, string cat) =>
            catalog.Resolve(typeId, dir == "expense" ? "expense" : "income", cat ?? "other").Activity != FinanceCategoryRules.ExcludedActivity;

        var openings = await db.Cashboxes.AsNoTracking()
            .Select(b => new { b.Id, b.OpeningBalance, b.OpeningDate }).ToListAsync(ct);
        var earlier = all.Where(t => string.CompareOrdinal(t.Date ?? "", from) < 0 && Counted(t.TypeId, t.Direction, t.Category));
        var initial = CashboxService.YearCarry(
            openings.Select(b => (b.Id, b.OpeningBalance, b.OpeningDate)),
            earlier.Select(t => (t.Date ?? "", t.Direction ?? "income", t.Amount, t.CashboxId)),
            from);

        var rows = all.Where(t => string.CompareOrdinal(t.Date ?? "", from) >= 0)
            .Select(t => new Row(t.Date ?? "", t.Direction ?? "income", t.Amount,
                FinanceCategoryRules.KeyOf(t.TypeId, t.Direction == "expense" ? "expense" : "income", t.Category ?? "other")))
            .ToList();
        // Katalogda yo'q kalit bo'lsa ham qator nomi chiqsin.
        foreach (var t in all) catalog.Resolve(t.TypeId, t.Direction == "expense" ? "expense" : "income", t.Category ?? "other");

        var today = AppClock.Today;
        var currentMonth = today.Year == year ? today.Month : (year < today.Year ? 12 : 0);
        var firstDate = all.Select(t => t.Date ?? "").Where(d => d.Length >= 4).DefaultIfEmpty($"{today.Year:0000}").Min()!;
        var firstYear = int.TryParse(firstDate.AsSpan(0, 4), out var fy) ? Math.Min(fy, today.Year) : today.Year;

        var y = Build(year, rows, catalog, initial, currentMonth, new ReconciliationDto(0, 0, 0, false), firstYear);

        // KASSA BILAN SOLISHTIRISH — faqat joriy yil: hisobotdagi joriy oy oxiri qoldig'i = barcha kassalar qoldig'i jami
        // (arxivdagilar ham). Farq: «excluded» qatorlar yoki yil ichida qo'yilgan kassa boshlang'ich qoldig'i.
        if (today.Year == year)
        {
            var balances = await CashboxService.BalancesAsync(db, null, null, ct);
            var cashNow = balances.Values.Sum(b => b.Total);
            var reportNow = y.EndBalances[today.Month - 1];
            y = y with { Reconciliation = new ReconciliationDto(cashNow, reportNow, cashNow - reportNow, true) };
        }
        return y;
    }

    // ============================ Drill-down ============================

    public sealed record TxDto(
        string Id, string Date, decimal Amount, string Direction, string? Cashbox, string? Method, string? Person,
        string? Note, string? CreatedBy);
    public sealed record TxPageDto(string RowKey, string Name, int Total, decimal Sum, int Page, int PageSize, List<TxDto> Items);

    /// <summary>Katakcha: shu qator + shu oy (yoki butun yil, <paramref name="month"/> = 0) amallari, sahifalab.</summary>
    public static async Task<TxPageDto> TransactionsAsync(
        IAppDbContext db, int year, int month, string rowKey, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var prefix = month is >= 1 and <= 12 ? $"{year:0000}-{month:00}-" : $"{year:0000}-";
        var q = db.FinanceTransactions.AsNoTracking().Where(t => t.Date.StartsWith(prefix));
        if (rowKey.StartsWith("type:", StringComparison.Ordinal))
        {
            var id = rowKey[5..];
            q = q.Where(t => t.TypeId == id);
        }
        else if (rowKey.StartsWith("cat:", StringComparison.Ordinal) && rowKey.Split(':') is [_, var dir, var cat])
            q = q.Where(t => t.TypeId == null && t.Direction == dir && t.Category == cat);
        else
            q = q.Where(_ => false);

        var list = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .Select(t => new { t.Id, t.Date, t.Amount, t.Direction, t.CashboxId, t.Method, t.StudentId, t.TeacherId, t.StaffUserId, t.Note, t.Comment, t.CreatedBy })
            .ToListAsync(ct);
        var catalog = await FinanceCategoryCatalog.LoadAsync(db, ct);
        var name = catalog.Entries.TryGetValue(rowKey, out var e) ? e.Name : rowKey;

        var slice = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var boxes = await db.Cashboxes.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var sIds = slice.Select(x => x.StudentId).Where(x => x != null).Distinct().ToList();
        var tIds = slice.Select(x => x.TeacherId).Where(x => x != null).Distinct().ToList();
        var uIds = slice.Select(x => x.StaffUserId).Where(x => x != null).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => sIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var teachers = await db.Teachers.AsNoTracking().Where(s => tIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var users = await db.Users.AsNoTracking().Where(s => uIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        var items = slice.Select(x => new TxDto(
            x.Id, x.Date, x.Amount, x.Direction,
            x.CashboxId is not null && boxes.TryGetValue(x.CashboxId, out var bn) ? bn : null,
            x.Method,
            x.StudentId is not null && students.TryGetValue(x.StudentId, out var sn) ? sn
                : x.TeacherId is not null && teachers.TryGetValue(x.TeacherId, out var tn) ? tn
                : x.StaffUserId is not null && users.TryGetValue(x.StaffUserId, out var un) ? un : null,
            string.IsNullOrWhiteSpace(x.Comment) ? x.Note : x.Comment,
            x.CreatedBy)).ToList();
        return new TxPageDto(rowKey, name, list.Count, list.Sum(x => x.Amount), page, pageSize, items);
    }
}

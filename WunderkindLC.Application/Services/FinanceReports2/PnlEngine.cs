using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.FinanceReports2;

/// <summary>
/// P&amp;L 2.0 — HISOBLANMA (accrual) asosidagi foyda/zarar (<c>docs/finance-reports/DECISIONS.md</c> D20–D40).
///
/// <para><b>Yagona hisob funksiyasi</b> — <see cref="Compute"/>: P&amp;L jadvali, Yillik ko'rinish va Rejali xarajatlar tabi
/// AYNAN shu funksiyadan oladi (bir oy uchun ikki tab ikki xil raqam ko'rsatmasin). Ma'lumot bir marta yuklanadi
/// (<see cref="LoadAsync"/>), oylar xotirada hisoblanadi.</para>
///
/// <code>
/// Yalpi kutuv (oy hisoblari, chegirmadan oldin) − Chegirma − Qaytarishlar = Sof kutuv (NTR)
/// Sof foyda = NTR + Boshqa daromad − Jami xarajat
/// </code>
/// Kassa fakti P&amp;L natijasiga KIRMAYDI — faqat «Kassa» blokida va «P&amp;L → Pul oqimi» ko'prigida.
/// </summary>
public static partial class PnlEngine
{
    // ============================ Kirish ma'lumotlari ============================

    public sealed record Tx(
        string Id, string Date, string Direction, decimal Amount, string Key, string? Month,
        string? StudentId, string? TeacherId, string? StaffUserId, FinanceCategoryCatalog.Entry Entry);

    public sealed record Charge(string StudentId, string? GroupId, string Month, decimal Amount, decimal Discount, string Date);

    public sealed record StudentInfo(string Id, string Name, bool IsArchived, string ArchivedAt);

    public sealed record Membership(string StudentId, string GroupId, CourseAnalytics.MembershipRow Row);

    public sealed class Data
    {
        public required FinanceCategoryCatalog.Catalog Catalog { get; init; }
        public required List<Tx> Txs { get; init; }
        public required List<Charge> Charges { get; init; }
        /// <summary>(o'quvchi, guruh, oy) → amaldagi kelmagan darslar chegirmalari yig'indisi.</summary>
        public required Dictionary<(string, string, string), decimal> LessonDiscounts { get; init; }
        public required Dictionary<string, StudentInfo> Students { get; init; }
        public required Dictionary<string, Student> StudentEntities { get; init; }
        public required List<Membership> Memberships { get; init; }
        public required Dictionary<string, Group> Groups { get; init; }
        public required PricingBook Pricing { get; init; }
        public required DiscountBook Discounts { get; init; }
        public required List<PlannedExpenseTemplate> Templates { get; init; }
        public required Dictionary<(string, string), PlannedExpenseAccrual> Accruals { get; init; }
        public required Dictionary<string, PayrollDto> Payroll { get; init; }
        public required string Today { get; init; }
    }

    // ============================ Natija (DTO) ============================

    public sealed record PlanCur(decimal Planned, decimal Current);
    public sealed record IntPlanCur(int Planned, int Current, int Joined, int Left);
    public sealed record DiscountTypeDto(string Key, string Type, string Label, int StudentsCount, decimal Planned, decimal Amount);
    public sealed record DiscountDto(decimal Planned, decimal Current, List<DiscountTypeDto> ByType);
    public sealed record OtherLineDto(string Key, string Name, decimal Amount);
    public sealed record OtherRevenueDto(decimal Planned, decimal Current, List<OtherLineDto> Lines);
    public sealed record RevenueDto(
        PlanCur Gross, DiscountDto Discount, PlanCur Refunds, PlanCur Net, OtherRevenueDto OtherRevenue,
        PlanCur PerStudentNet, PlanCur DiscountRate, decimal NetFlooredBy);
    public sealed record CashDto(decimal Collected, decimal ForThisMonth, decimal ForPriorMonths, decimal ForFutureMonths,
        decimal Uncollected, decimal? CollectionRate);
    public sealed record PeriodCollectionDto(
        decimal OwedForPeriod, decimal CollectedForPeriod, decimal OutstandingForPeriod, decimal? CollectionRateForPeriod,
        int StudentsExpected, int StudentsPaid, int StudentsPartial, int StudentsUnpaid);
    public sealed record TemplateRefDto(string Id, string Name, decimal Planned, decimal Current, bool IsCorrected, bool HasAccrual);
    public sealed record ExpenseLineDto(
        string Id, string Name, string? TypeId, string Group, decimal Planned, decimal Current, decimal Paid,
        bool IsUnplanned, bool IsCorrected, bool IsPayroll, List<TemplateRefDto> Templates);
    public sealed record ExpenseGroupDto(string Name, decimal Planned, decimal Current, decimal Paid, int LineCount);
    public sealed record ExpensesDto(
        decimal Planned, decimal Current, decimal Paid, decimal Unpaid, bool HasExpenseData,
        List<ExpenseGroupDto> Groups, List<ExpenseLineDto> Lines);
    public sealed record ResultDto(
        decimal ProfitPlanned, decimal ProfitCurrent, decimal? MarginPlanned, decimal? MarginCurrent,
        decimal ProfitPerStudentPlanned, decimal ProfitPerStudentCurrent);
    public sealed record ReconciliationDto(decimal SnapshotNet, decimal ChangesSum, decimal ComputedNet, decimal Unexplained, bool SnapshotIsEstimated);
    public sealed record BranchDto(
        string BranchId, string BranchName, IntPlanCur Students, RevenueDto Revenue, CashDto Cash, ExpensesDto Expenses,
        ResultDto Result, PeriodCollectionDto PeriodCollection, ReconciliationDto Reconciliation);
    public sealed record CashBridgeDto(
        decimal Profit, decimal UnpaidExpense, decimal ExpensePaid, decimal UncollectedRevenue, decimal PriorDebtCollected,
        decimal AdvancesCollected, decimal OutsidePnl, decimal CashDelta, decimal ActualCashDelta, decimal Difference,
        bool PriorDebtCollectedAvailable, bool CashDeltaIsComplete);
    public sealed record PeriodDto(int Year, int Month, string MonthName, string From, string AsOf, int DaysInMonth);
    public sealed record PnlDto(
        PeriodDto Period, string AsOf, bool IsForecast, bool IsCurrentMonth, string Currency, string RevenueSource,
        bool PlanIsEstimated, BranchDto Total, CashBridgeDto CashBridge);

    // ============================ Yuklash ============================

    /// <summary>[<paramref name="fromMonth"/> .. <paramref name="toMonth"/>] oylar uchun barcha kerakli ma'lumot (bir marta).</summary>
    public static async Task<Data> LoadAsync(IAppDbContext db, string fromMonth, string toMonth, CancellationToken ct = default)
    {
        await FinanceCategoryCatalog.EnsureAsync(db, ct);
        var catalog = await FinanceCategoryCatalog.LoadAsync(db, ct);
        var fromDate = fromMonth + "-01";
        var toDate = toMonth + "-31";

        // Amallar: sanasi oraliqda YOKI oy tegi oraliqda (shu oy uchun to'lov keyinroq kelgan bo'lishi mumkin).
        var rawTx = await db.FinanceTransactions.AsNoTracking()
            .Where(t => (t.Date.CompareTo(fromDate) >= 0 && t.Date.CompareTo(toDate) <= 0)
                        || (t.Month != null && t.Month.CompareTo(fromMonth) >= 0 && t.Month.CompareTo(toMonth) <= 0))
            .Select(t => new { t.Id, t.Date, t.Direction, t.Amount, t.TypeId, t.Category, t.Month, t.StudentId, t.TeacherId, t.StaffUserId })
            .ToListAsync(ct);
        var txs = rawTx.Select(t =>
        {
            var dir = t.Direction == "expense" ? "expense" : "income";
            var cat = string.IsNullOrWhiteSpace(t.Category) ? "other" : t.Category;
            var entry = catalog.Resolve(t.TypeId, dir, cat);
            return new Tx(t.Id, t.Date ?? "", dir, t.Amount, entry.Key, t.Month, t.StudentId, t.TeacherId, t.StaffUserId, entry);
        }).ToList();

        var charges = (await db.MonthlyCharges.AsNoTracking()
                .Where(c => c.Month.CompareTo(fromMonth) >= 0 && c.Month.CompareTo(toMonth) <= 0)
                .Select(c => new { c.StudentId, c.GroupId, c.Month, c.Amount, c.Discount, c.Date }).ToListAsync(ct))
            .Select(c => new Charge(c.StudentId, c.GroupId, c.Month, c.Amount, c.Discount, c.Date ?? "")).ToList();

        var lessonDiscounts = (await db.LessonDiscounts.AsNoTracking()
                .Where(l => l.CancelledAt == null && l.Month.CompareTo(fromMonth) >= 0 && l.Month.CompareTo(toMonth) <= 0)
                .Select(l => new { l.StudentId, l.GroupId, l.Month, l.Amount }).ToListAsync(ct))
            .GroupBy(l => (l.StudentId, l.GroupId, l.Month))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var studentEntities = await db.Students.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var students = studentEntities.Values.ToDictionary(s => s.Id,
            s => new StudentInfo(s.Id, s.FullName, s.IsArchived, s.ArchivedAt ?? ""));
        var memberships = (await db.StudentGroups.AsNoTracking().ToListAsync(ct))
            .Select(m => new Membership(m.StudentId, m.GroupId, new CourseAnalytics.MembershipRow(
                m.StudentId, "", m.JoinedAt ?? "", m.ActivatedAt ?? "", m.LeftAt, m.FrozenAt ?? "",
                m.Status ?? "", m.IsActive, 0m, m.PastPeriods)))
            .ToList();
        var groups = await db.Classes.AsNoTracking().ToDictionaryAsync(g => g.Id, ct);
        var pricing = await PricingBook.LoadAsync(db, ct);
        var discounts = await DiscountBook.LoadAllAsync(db);

        var templates = await db.PlannedExpenseTemplates.AsNoTracking().Where(t => !t.IsDeleted).ToListAsync(ct);
        var accruals = (await db.PlannedExpenseAccruals.AsNoTracking()
                .Where(a => a.Month.CompareTo(fromMonth) >= 0 && a.Month.CompareTo(toMonth) <= 0).ToListAsync(ct))
            .ToDictionary(a => (a.TemplateId, a.Month));

        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var payroll = new Dictionary<string, PayrollDto>(StringComparer.Ordinal);
        foreach (var m in MonthsBetween(fromMonth, toMonth))
            if (string.CompareOrdinal(m, today[..7]) <= 0) payroll[m] = await PayrollRegister.BuildAsync(db, m, ct);

        return new Data
        {
            Catalog = catalog, Txs = txs, Charges = charges, LessonDiscounts = lessonDiscounts, Students = students,
            StudentEntities = studentEntities, Memberships = memberships, Groups = groups, Pricing = pricing,
            Discounts = discounts, Templates = templates, Accruals = accruals, Payroll = payroll, Today = today,
        };
    }

    public static IEnumerable<string> MonthsBetween(string from, string to)
    {
        if (!DateOnly.TryParse(from + "-01", out var d) || !DateOnly.TryParse(to + "-01", out var end)) yield break;
        for (var i = 0; d <= end && i < 240; i++, d = d.AddMonths(1)) yield return d.ToString("yyyy-MM");
    }

    /// <summary>Holat sanasi: oy ichiga qisiladi, bugundan keyin bo'lmaydi; bo'sh/buzuq — oy oxiri yoki bugun.</summary>
    public static string ClampAsOf(string month, string? asOf, string today)
    {
        var first = month + "-01";
        var last = DateOnly.Parse(first).AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");
        var a = DateOnly.TryParse(asOf, out var p) ? p.ToString("yyyy-MM-dd") : last;
        if (string.CompareOrdinal(a, last) > 0) a = last;
        if (string.CompareOrdinal(a, first) < 0) a = first;
        if (string.CompareOrdinal(a, today) > 0 && string.CompareOrdinal(first, today) <= 0) a = today;
        return a;
    }

    // ============================ Hisob (sof) ============================

    public const string PayrollGroup = "Ish haqi";

    private static bool ActiveAt(Data d, string studentId, IEnumerable<Membership> mships, string date)
    {
        if (d.Students.TryGetValue(studentId, out var s) && s.IsArchived && s.ArchivedAt.Length >= 10
            && string.CompareOrdinal(s.ArchivedAt[..10], date) <= 0) return false;
        return mships.Any(m => CourseAnalytics.WasActiveAt(m.Row, date));
    }

    private static decimal? Pct(decimal part, decimal whole) => whole == 0 ? null : Math.Round(part / whole * 100m, 2);
    private static decimal Div(decimal a, int b) => b == 0 ? 0 : Math.Round(a / b, 0);

    /// <summary>Bitta oy, <paramref name="asOf"/> holatiga (oy 1-kunidan shu sanagacha).</summary>
    public static PnlDto Compute(Data d, string month, string asOf)
    {
        var m01 = month + "-01";
        var monthStart = DateOnly.Parse(m01);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");
        var isForecast = string.CompareOrdinal(m01, d.Today) > 0;
        var isCurrent = d.Today.StartsWith(month, StringComparison.Ordinal);
        bool InWin(string date) => date.Length >= 10 && date.StartsWith(month, StringComparison.Ordinal)
                                   && string.CompareOrdinal(date, asOf) <= 0;
        var win = d.Txs.Where(t => InWin(t.Date)).ToList();

        // ---- O'quvchilar (faol = kamida bitta a'zolik o'sha kuni FAOL — sanalar bo'yicha tiklanadi) ----
        var byStudent = d.Memberships.GroupBy(m => m.StudentId).ToList();
        var planned = new HashSet<string>(StringComparer.Ordinal);
        var current = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in byStudent)
        {
            if (ActiveAt(d, g.Key, g, m01)) planned.Add(g.Key);
            if (!isForecast && ActiveAt(d, g.Key, g, asOf)) current.Add(g.Key);
        }
        if (isForecast) current.UnionWith(planned);
        var joined = current.Count(x => !planned.Contains(x));
        var left = planned.Count(x => !current.Contains(x));

        // ---- Kutilayotgan tushum: oy hisoblari (A rejim) ----
        var charges = d.Charges.Where(c => c.Month == month && (c.Date.Length < 10 || string.CompareOrdinal(c.Date, asOf) <= 0)).ToList();
        var gross = charges.Sum(c => c.Amount);
        var chargeDiscount = charges.Sum(c => c.Discount);
        var netFlooredBy = charges.Sum(c => Math.Max(0, c.Discount - c.Amount));

        var discTypes = new Dictionary<string, (string Type, string Label, HashSet<string> Students, decimal Planned, decimal Amount)>(StringComparer.Ordinal);
        void AddDisc(string key, string type, string label, string? studentId, decimal planned2, decimal amount)
        {
            if (!discTypes.TryGetValue(key, out var v)) v = (type, label, new HashSet<string>(StringComparer.Ordinal), 0m, 0m);
            if (studentId is not null && amount > 0) v.Students.Add(studentId);
            discTypes[key] = (v.Type, v.Label, v.Students, v.Planned + planned2, v.Amount + amount);
        }
        string RegLabel(string studentId, string? groupId)
        {
            var w = DiscountRules.Resolve(d.Discounts.For(studentId), month, groupId);
            return w is null ? "Chegirma (tarixiy)" : string.IsNullOrWhiteSpace(w.Reason) ? "Chegirma (sababi ko'rsatilmagan)" : w.Reason.Trim();
        }
        foreach (var c in charges.Where(c => c.Discount > 0))
        {
            var lesson = c.GroupId is not null && d.LessonDiscounts.TryGetValue((c.StudentId, c.GroupId, month), out var ld) ? Math.Min(ld, c.Discount) : 0m;
            if (lesson > 0) AddDisc("lesson", "lesson", "Kelmagan darslar chegirmasi", c.StudentId, 0, lesson);
            var reg = c.Discount - lesson;
            if (reg > 0)
            {
                var label = RegLabel(c.StudentId, c.GroupId);
                AddDisc("reason:" + label, "catalog", label, c.StudentId, 0, reg);
            }
        }
        var txDiscount = 0m;
        foreach (var t in win.Where(t => t.Entry.IsStudentDiscount))
        {
            txDiscount += t.Amount;
            AddDisc(t.Key, "transaction", t.Entry.Name, t.StudentId, 0, t.Amount);
        }
        var refunds = win.Where(t => t.Direction == "expense" && t.Entry.Treatment == FinanceCategoryRules.RevenueContra).Sum(t => t.Amount);
        var discountCurrent = chargeDiscount + txDiscount;
        var netCurrent = gross - discountCurrent - refunds;

        var otherLines = win
            .Where(t => (t.Direction == "income" && t.Entry.Treatment == FinanceCategoryRules.OtherRevenue)
                        || (t.Direction == "expense" && t.Entry.Treatment == FinanceCategoryRules.OtherRevenueContra))
            .GroupBy(t => t.Key)
            .Select(g => new OtherLineDto(g.Key, g.First().Entry.Name, g.Sum(t => t.Direction == "income" ? t.Amount : -t.Amount)))
            .Where(l => l.Amount != 0).OrderByDescending(l => l.Amount).ToList();
        var otherCurrent = otherLines.Sum(l => l.Amount);

        // ---- «Reja» (oy boshi): 1-kuni faol a'zoliklar × oylik narx − chegirma (oylik hisob bilan AYNAN bir xil narx/
        //      chegirma qoidasi — PricingBook + DiscountBook). Snapshot 2-bosqichda; hozircha TAXMINIY (PlanIsEstimated). ----
        decimal grossPlanned = 0, discountPlanned = 0;
        foreach (var g in byStudent)
        {
            if (!planned.Contains(g.Key) || !d.StudentEntities.TryGetValue(g.Key, out var st)) continue;
            foreach (var m in g.Where(m => CourseAnalytics.WasActiveAt(m.Row, m01)))
            {
                if (!d.Groups.TryGetValue(m.GroupId, out var grp)) continue;
                var fee = d.Pricing.MonthlyFee(grp, st, month);
                if (fee <= 0) continue;
                var disc = Math.Min(fee, d.Discounts.DiscountFor(g.Key, fee, month, m.GroupId));
                grossPlanned += fee;
                discountPlanned += disc;
                if (disc > 0)
                {
                    var label = RegLabel(g.Key, m.GroupId);
                    AddDisc("reason:" + label, "catalog", label, null, disc, 0);
                }
            }
        }
        var netPlanned = grossPlanned - discountPlanned;
        if (isForecast)
        {
            // Kelajak oy: «Joriy» = prognoz = reja.
            gross = grossPlanned; chargeDiscount = discountPlanned; discountCurrent = discountPlanned; netCurrent = netPlanned;
            refunds = 0; otherCurrent = 0; otherLines = [];
            foreach (var k in discTypes.Keys.ToList()) discTypes[k] = discTypes[k] with { Amount = discTypes[k].Planned };
        }

        var byType = discTypes
            .Select(x => new DiscountTypeDto(x.Key, x.Value.Type, x.Value.Label, x.Value.Students.Count, x.Value.Planned, x.Value.Amount))
            .OrderByDescending(x => Math.Max(x.Amount, x.Planned)).ToList();

        var revenue = new RevenueDto(
            new PlanCur(grossPlanned, gross),
            new DiscountDto(discountPlanned, discountCurrent, byType),
            new PlanCur(0, refunds),
            new PlanCur(netPlanned, netCurrent),
            new OtherRevenueDto(0, otherCurrent, otherLines),
            new PlanCur(Div(netPlanned, planned.Count), Div(netCurrent, current.Count)),
            new PlanCur(Pct(discountPlanned, grossPlanned) ?? 0, Pct(discountCurrent, gross) ?? 0),
            netFlooredBy);

        // ---- Kassa ----
        var tuitionIn = win.Where(t => t.Direction == "income" && t.Entry.Treatment == FinanceCategoryRules.TuitionRevenue).ToList();
        var collected = tuitionIn.Sum(t => t.Amount);
        var forThis = tuitionIn.Where(t => string.IsNullOrEmpty(t.Month) || t.Month == month).Sum(t => t.Amount);
        var forPrior = tuitionIn.Where(t => !string.IsNullOrEmpty(t.Month) && string.CompareOrdinal(t.Month, month) < 0).Sum(t => t.Amount);
        var forFuture = tuitionIn.Where(t => !string.IsNullOrEmpty(t.Month) && string.CompareOrdinal(t.Month, month) > 0).Sum(t => t.Amount);
        var owedTotal = gross - chargeDiscount;
        var uncollected = isForecast ? owedTotal : owedTotal - forThis;
        var cash = new CashDto(collected, forThis, forPrior, forFuture, uncollected, Pct(forThis, owedTotal));

        // Shu oy uchun yig'ilish — HOZIRGI holat (to'lovning oy tegi bo'yicha, sanasidan qat'i nazar).
        var owedBy = charges.GroupBy(c => c.StudentId).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount - c.Discount));
        var paidBy = d.Txs.Where(t => t.Month == month && t.StudentId is not null
                                      && ((t.Direction == "income" && t.Entry.Treatment == FinanceCategoryRules.TuitionRevenue)
                                          || (t.Direction == "expense" && t.Entry.Treatment == FinanceCategoryRules.RevenueContra)))
            .GroupBy(t => t.StudentId!).ToDictionary(g => g.Key, g => g.Sum(t => t.Direction == "income" ? t.Amount : -t.Amount));
        int exp = 0, paidN = 0, partial = 0, unpaid = 0;
        decimal owed = 0, coll = 0, outstanding = 0;
        foreach (var (sid, o) in owedBy)
        {
            if (o <= 0) continue;
            exp++;
            owed += o;
            var p = Math.Max(0, paidBy.GetValueOrDefault(sid));
            coll += Math.Min(p, o);
            outstanding += Math.Max(0, o - p);
            if (p >= o) paidN++; else if (p > 0) partial++; else unpaid++;
        }
        var period = new PeriodCollectionDto(owed, coll, outstanding, Pct(coll, owed), exp, paidN, partial, unpaid);

        // ---- Xarajatlar ----
        var expenses = Expenses(d, month, monthEnd, win);

        // ---- Natija ----
        var revenueTotalCur = netCurrent + otherCurrent;
        var profitCur = revenueTotalCur - expenses.Current;
        var profitPlan = netPlanned - expenses.Planned;
        var result = new ResultDto(profitPlan, profitCur, Pct(profitPlan, netPlanned), Pct(profitCur, revenueTotalCur),
            Div(profitPlan, planned.Count), Div(profitCur, current.Count));

        // ---- Ko'prik: P&L → pul o'zgarishi (oxirgi so'mgacha yopiladi; farq = sozlamada «excluded» qilingan pul harakati) ----
        var outside = win.Where(t => t.Entry.Activity != FinanceCategoryRules.ExcludedActivity
                                     && (t.Entry.Treatment is FinanceCategoryRules.Excluded
                                         || (t.Entry.Treatment == FinanceCategoryRules.NonCash && !t.Entry.IsStudentDiscount)))
            .Sum(t => t.Direction == "income" ? t.Amount : -t.Amount);
        // ⚠️ Xarajat farqi QISILMAYDI (hisoblangan − to'langan): avans yoki keyingi oy uchun oldindan to'langan pul manfiy
        // bo'lib kiradi — KPI dagi «To'lanmagan» (Unpaid) esa ≥ 0 qatorlar yig'indisi (D33).
        var expenseGap = expenses.Current - expenses.Paid;
        var bridgeCash = profitCur + expenseGap - uncollected + forPrior + forFuture + outside;
        var actual = win.Where(t => t.Entry.Activity != FinanceCategoryRules.ExcludedActivity)
            .Sum(t => t.Direction == "income" ? t.Amount : -t.Amount);
        var bridge = isForecast
            ? new CashBridgeDto(profitCur, 0, 0, 0, 0, 0, 0, 0, 0, 0, true, true)
            : new CashBridgeDto(profitCur, expenseGap, expenses.Paid, uncollected, forPrior, forFuture, outside,
                bridgeCash, actual, actual - bridgeCash, true, actual - bridgeCash == 0);

        var total = new BranchDto("all", "Markaz",
            new IntPlanCur(planned.Count, current.Count, joined, left), revenue, cash, expenses, result, period,
            new ReconciliationDto(netPlanned, 0, netCurrent, netCurrent - netPlanned, true));

        return new PnlDto(
            new PeriodDto(monthStart.Year, monthStart.Month, CashFlowReport.MonthNames[monthStart.Month - 1], m01, asOf,
                DateTime.DaysInMonth(monthStart.Year, monthStart.Month)),
            asOf, isForecast, isCurrent, "UZS", "charges", true, total, bridge);
    }

    /// <summary>Shablon shu oyda faolmi.</summary>
    public static bool TemplateActive(PlannedExpenseTemplate t, string m01, string monthEnd) =>
        !t.IsDeleted && t.Status == "active"
        && t.StartDate.Length >= 10 && string.CompareOrdinal(t.StartDate, monthEnd) <= 0
        && (string.IsNullOrEmpty(t.EndDate) || string.CompareOrdinal(t.EndDate, m01) >= 0);

    public static string TemplateKey(PlannedExpenseTemplate t) =>
        FinanceCategoryRules.KeyOf(t.TypeId, "expense", string.IsNullOrWhiteSpace(t.Category) ? "other" : t.Category);

    private static ExpensesDto Expenses(Data d, string month, string monthEnd, List<Tx> win)
    {
        var m01 = month + "-01";
        var lines = new List<ExpenseLineDto>();
        var expenseTx = win.Where(t => t.Direction == "expense").ToList();

        // 1) Rejali shablonlar — oy 1-kunida TO'LIQ summada (to'langan-to'lanmaganidan qat'i nazar).
        var active = d.Templates.Where(t => TemplateActive(t, m01, monthEnd)).ToList();
        var templateKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in active.GroupBy(TemplateKey))
        {
            var first = g.First();
            var entry = d.Catalog.Resolve(first.TypeId, "expense", string.IsNullOrWhiteSpace(first.Category) ? "other" : first.Category);
            templateKeys.Add(g.Key);
            var refs = g.Select(t =>
            {
                d.Accruals.TryGetValue((t.Id, month), out var a);
                var plan = a?.Amount ?? t.Amount;
                return new TemplateRefDto(t.Id, t.Name, plan, a?.CorrectedAmount ?? plan, a?.CorrectedAmount is not null, a is not null);
            }).OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
            var paid = expenseTx.Where(t => t.Key == g.Key).Sum(t => t.Amount);
            lines.Add(new ExpenseLineDto(g.Key, entry.Name, entry.TypeId, entry.Group,
                refs.Sum(r => r.Planned), refs.Sum(r => r.Current), paid, false, refs.Any(r => r.IsCorrected), false, refs));
        }

        // 2) Ish haqi — maosh reestridan (o'qituvchi: SalaryLedger, xodim: KPI/qat'iy oylik). To'langani — shu oy oralig'idagi
        //    maosh/avans chiqimlari. ⚠️ Maosh chiqimlari xarajatga ALOHIDA qo'shilmaydi (ikki marta sanalmasin).
        var salaryTx = expenseTx.Where(t => t.Entry.Treatment is FinanceCategoryRules.Salary or FinanceCategoryRules.SalaryAdvance).ToList();
        if (d.Payroll.TryGetValue(month, out var pr))
        {
            // 2026-11-10 (egasi: «oktabr oyligi noyabrda berilsa — oktabr xarajatida»): maosh xarajati ham, «to'langan» ham
            // «QAYSI OY UCHUN» (FinanceTransaction.Month — reestrdagi Paid) bo'yicha, to'lov KUNI bo'yicha EMAS. Xarajat =
            // har kishi uchun max(hisoblangan, shu oy uchun to'langan): oylik kiritilmagan xodimga berilgan pul ham yo'qolmasin.
            // Oyi yo'q eski XODIM maosh chiqimlari — avvalgidek to'lov kuni bo'yicha (shu oy oynasida). O'qituvchida ular
            // maosh varaqasida (SalaryLedger: oyi yo'q to'lov sanasi bo'yicha) — reestrning Paid ichida, qayta qo'shilmaydi.
            var sUntagged = salaryTx.Where(t => string.IsNullOrEmpty(t.Month) && t.TeacherId is null).Sum(t => t.Amount);
            var tAcc = pr.Teachers.Sum(r => r.Accrued);
            var sAcc = pr.Staff.Sum(r => r.Accrued);
            var tCur = pr.Teachers.Sum(r => Math.Max(r.Accrued, r.Paid));
            var sCur = pr.Staff.Sum(r => Math.Max(r.Accrued, r.Paid)); // eski (oyi yo'q) to'lov — faqat «to'langan»da (avvalgidek)
            var tPaid = pr.Teachers.Sum(r => r.Paid);
            var sPaid = pr.Staff.Sum(r => r.Paid) + sUntagged;
            if (tCur != 0 || tPaid != 0)
                lines.Add(new ExpenseLineDto("payroll:teachers", "O'qituvchilar maoshi", null, PayrollGroup, tAcc, tCur, tPaid, false, true, true, []));
            if (sCur != 0 || sPaid != 0)
                lines.Add(new ExpenseLineDto("payroll:staff", "Xodimlar maoshi", null, PayrollGroup, sAcc, sCur, sPaid, false, true, true, []));
        }
        else if (salaryTx.Count > 0)
        {
            // Reestr yo'q (kelajak oy) — zaxira: maosh chiqimlari rejasiz xarajat sifatida, avans baribir chiqariladi.
            var s = salaryTx.Where(t => t.Entry.Treatment == FinanceCategoryRules.Salary).Sum(t => t.Amount);
            if (s != 0) lines.Add(new ExpenseLineDto("payroll:cash", "Ish haqi (to'lovlar bo'yicha)", null, PayrollGroup, 0, s, s, true, false, true, []));
        }

        // 3) Rejasiz — oddiy xarajat chiqimlari, shu tur uchun faol shablon YO'Q bo'lsa.
        foreach (var g in expenseTx.Where(t => t.Entry.Treatment == FinanceCategoryRules.Expense && !templateKeys.Contains(t.Key)).GroupBy(t => t.Key))
        {
            var e = g.First().Entry;
            var sum = g.Sum(t => t.Amount);
            lines.Add(new ExpenseLineDto(g.Key, e.Name, e.TypeId, e.Group, 0, sum, sum, true, false, false, []));
        }

        lines = lines.OrderByDescending(l => l.IsPayroll).ThenByDescending(l => l.Current).ThenBy(l => l.Name, StringComparer.Ordinal).ToList();
        var groups = lines.GroupBy(l => l.Group)
            .Select(g => new ExpenseGroupDto(g.Key, g.Sum(l => l.Planned), g.Sum(l => l.Current), g.Sum(l => l.Paid), g.Count()))
            .OrderByDescending(g => g.Name == PayrollGroup).ThenByDescending(g => g.Current).ToList();
        return new ExpensesDto(
            lines.Sum(l => l.Planned), lines.Sum(l => l.Current), lines.Sum(l => l.Paid),
            lines.Sum(l => Math.Max(0, l.Current - l.Paid)), lines.Count > 0, groups, lines);
    }

    /// <summary>Bazadan yuklab bitta oyni hisoblaydi.</summary>
    public static async Task<PnlDto> ComputeAsync(IAppDbContext db, string month, string? asOf, CancellationToken ct = default)
    {
        var data = await LoadAsync(db, month, month, ct);
        return Compute(data, month, ClampAsOf(month, asOf, data.Today));
    }

    // ============================ Yillik ko'rinish ============================

    public sealed record YearPlanDto(decimal Gross, decimal Discount, decimal Net, decimal Expense, decimal Profit, bool IsEstimated);
    public sealed record YearMonthDto(
        int Month, string Name, bool IsForecast, bool IsCurrent, bool HasData, int Students, decimal Gross, decimal Discount,
        decimal Refunds, decimal OtherRevenue, decimal Net, decimal Expense, decimal Collected, decimal Profit,
        decimal? Margin, decimal? DiscountRate, YearPlanDto Plan);
    public sealed record BestDto(int Month, decimal Profit);
    public sealed record YearTotalsDto(
        int Students, decimal Gross, decimal Discount, decimal Refunds, decimal OtherRevenue, decimal Net, decimal Expense,
        decimal Collected, decimal Profit, decimal? Margin, decimal? DiscountRate, decimal PlanExpense, decimal PlanProfit,
        BestDto? BestMonth, BestDto? WorstMonth);
    public sealed record YearDto(int Year, int CurrentMonth, List<YearMonthDto> Months, YearTotalsDto Totals);

    /// <summary>
    /// 12 oy — har oy <see cref="Compute"/> bilan OY OXIRI holatiga (joriy oy — bugun). Kelajak oylar PROGNOZ: sof kutuv =
    /// joriy (yoki oxirgi fakt) oyning kutuvi, xarajat = oxirgi ≤3 fakt oyning o'rtachasi. YILLIK: summalar — fakt oylar
    /// yig'indisi; o'quvchilar — fakt va &gt;0 oylar o'rtachasi; marja = yillik foyda / yillik daromad.
    /// </summary>
    public static YearDto Yearly(Data d, int year)
    {
        var today = DateOnly.Parse(d.Today);
        var currentMonth = today.Year == year ? today.Month : (year < today.Year ? 12 : 0);
        var months = new List<YearMonthDto>();
        for (var m = 1; m <= 12; m++)
        {
            var month = $"{year:0000}-{m:00}";
            var isForecast = year > today.Year || (year == today.Year && m > today.Month);
            if (isForecast) { months.Add(null!); continue; }
            var asOf = ClampAsOf(month, null, d.Today);
            var p = Compute(d, month, asOf);
            var t = p.Total;
            var hasData = t.Revenue.Gross.Current != 0 || t.Expenses.Current != 0 || t.Revenue.OtherRevenue.Current != 0 || t.Students.Current != 0;
            months.Add(new YearMonthDto(m, CashFlowReport.MonthNames[m - 1], false, p.IsCurrentMonth, hasData,
                t.Students.Current, t.Revenue.Gross.Current, t.Revenue.Discount.Current, t.Revenue.Refunds.Current,
                t.Revenue.OtherRevenue.Current, t.Revenue.Net.Current, t.Expenses.Current, t.Cash.Collected,
                t.Result.ProfitCurrent, t.Result.MarginCurrent, Pct(t.Revenue.Discount.Current, t.Revenue.Gross.Current),
                new YearPlanDto(t.Revenue.Gross.Planned, t.Revenue.Discount.Planned, t.Revenue.Net.Planned, t.Expenses.Planned,
                    t.Result.ProfitPlanned, p.PlanIsEstimated)));
        }

        var facts = months.Where(x => x is not null).ToList();
        var lastFact = facts.LastOrDefault(x => x.HasData) ?? facts.LastOrDefault();
        var avgExpense = facts.Where(x => x.HasData).TakeLast(3).Select(x => x.Expense).DefaultIfEmpty(0).Average();
        for (var m = 1; m <= 12; m++)
        {
            if (months[m - 1] is not null) continue;
            var net = lastFact?.Net ?? 0;
            var gross = lastFact?.Gross ?? 0;
            var disc = lastFact?.Discount ?? 0;
            var profit = net - avgExpense;
            months[m - 1] = new YearMonthDto(m, CashFlowReport.MonthNames[m - 1], true, false, false, lastFact?.Students ?? 0,
                gross, disc, 0, 0, net, Math.Round(avgExpense, 0), 0, Math.Round(profit, 0), Pct(profit, net), Pct(disc, gross),
                new YearPlanDto(gross, disc, net, Math.Round(avgExpense, 0), Math.Round(profit, 0), true));
        }

        var studentsAvg = facts.Where(x => x.Students > 0).Select(x => (decimal)x.Students).DefaultIfEmpty(0).Average();
        decimal S(Func<YearMonthDto, decimal> f) => facts.Sum(f);
        var revenue = S(x => x.Net) + S(x => x.OtherRevenue);
        var withData = facts.Where(x => x.HasData).ToList();
        var totals = new YearTotalsDto(
            (int)Math.Round(studentsAvg, 0, MidpointRounding.AwayFromZero), S(x => x.Gross), S(x => x.Discount), S(x => x.Refunds),
            S(x => x.OtherRevenue), S(x => x.Net), S(x => x.Expense), S(x => x.Collected), S(x => x.Profit),
            Pct(S(x => x.Profit), revenue), Pct(S(x => x.Discount), S(x => x.Gross)), S(x => x.Plan.Expense), S(x => x.Plan.Profit),
            withData.Count == 0 ? null : withData.OrderByDescending(x => x.Profit).Select(x => new BestDto(x.Month, x.Profit)).First(),
            withData.Count == 0 ? null : withData.OrderBy(x => x.Profit).Select(x => new BestDto(x.Month, x.Profit)).First());
        return new YearDto(year, currentMonth, months, totals);
    }

    public static async Task<YearDto> YearlyAsync(IAppDbContext db, int year, CancellationToken ct = default)
    {
        var data = await LoadAsync(db, $"{year:0000}-01", $"{year:0000}-12", ct);
        return Yearly(data, year);
    }
}

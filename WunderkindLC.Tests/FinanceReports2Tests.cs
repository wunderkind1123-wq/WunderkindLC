using WunderkindLC.Application.Services.FinanceReports2;
using WunderkindLC.Domain;
using Xunit;
using R = WunderkindLC.Application.Services.FinanceReports2.FinanceCategoryRules;

namespace WunderkindLC.Tests;

/// <summary>
/// Pul oqimi 2.0 · P&amp;L 2.0 (<c>docs/finance-reports/</c>): avtomatik tasniflash, pul oqimi qoldiqlari, P&amp;L formulalari,
/// ko'prik (oxirgi so'mgacha yopiladi) va «Yillik = P&amp;L jadvali».
/// </summary>
public class FinanceReports2Tests
{
    // ============================ Avtomatik tasniflash ============================

    [Theory]
    [InlineData("O'quvchi oylik to'lovi", "income", "kirim", "other", R.Operating, R.TuitionRevenue)]
    [InlineData("SAT oylik to'lovlari", "income", "kirim", "tuition", R.Operating, R.TuitionRevenue)]
    [InlineData("Hodimga avans", "expense", "chiqim", "salary", R.Operating, R.SalaryAdvance)]
    [InlineData("Hodimga oylik", "expense", "chiqim", "salary", R.Operating, R.Salary)]
    [InlineData("Vaucher", "expense", "chiqim", "other", R.Operating, R.NonCash)]
    [InlineData("Chegirma (Do'stini olib kelganligi uchun )", "expense", "chiqim", "other", R.Operating, R.NonCash)]
    [InlineData("O'qituvchiga bonus", "income", "vaucher", "other", R.Operating, R.NonCash)]
    [InlineData("O'qituvchiga jarima", "expense", "jarima", "penalty", R.Operating, R.NonCash)]
    [InlineData("Adashib kiritilgan pulni balansdan chiqarish", "expense", "chiqim", "other", R.Operating, R.Excluded)]
    [InlineData("Pulni qaytarish", "expense", "chiqim", "other", R.Operating, R.RevenueContra)]
    [InlineData("Arenda", "expense", "chiqim", "rent", R.Operating, R.Expense)]
    [InlineData("Kitob uchun", "income", "kirim", "book", R.Operating, R.OtherRevenue)]
    [InlineData("Kredit qaytarish", "expense", "chiqim", "other", R.Financing, R.Excluded)]
    [InlineData("Kompyuter va Printer uchun", "expense", "chiqim", "supplies", R.Investing, R.Expense)]
    [InlineData("Kommunal to'lovlar", "expense", "chiqim", "utilities", R.Operating, R.Expense)]
    [InlineData("Fond", "income", "kirim", "other", R.Operating, R.OtherRevenue)]
    public void Avto_tasniflash(string name, string dir, string kind, string cat, string activity, string treatment)
    {
        var r = R.Classify(new(name, dir, kind, cat));
        Assert.Equal((activity, treatment), (r.Activity, r.Treatment));
        Assert.StartsWith("Qoida #", r.Reason);
    }

    [Fact]
    public void Turi_yoq_toifalar_va_kitob_qaytarish()
    {
        Assert.Equal(R.TuitionRevenue, R.Classify(new("O'quvchi to'lovi", "income", null, "tuition")).Treatment);
        Assert.Equal(R.RevenueContra, R.Classify(new("Vozvrat", "expense", null, "refund")).Treatment);
        // Kitob qaytarishda berilgan pul — xarajat EMAS, «Boshqa daromad» dan ayiriladi.
        Assert.Equal(R.OtherRevenueContra, R.Classify(new("Kitob qaytarish", "expense", null, "book")).Treatment);
        // Apostrof variantlari bir xil o'qiladi.
        Assert.Equal(R.TuitionRevenue, R.Classify(new("O‘quvchi oylik toʻlovi", "income", null, "other")).Treatment);
    }

    [Fact]
    public async Task Ensure_idempotent_va_qolda_ozgartirilganga_TEGMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.TransactionTypes.Add(new TransactionType { Id = "t-arenda", Name = "Arenda", Kind = "chiqim", Direction = "expense", BaseCategory = "rent" });
        ctx.FinanceTransactions.Add(new FinanceTransaction { Date = "2026-09-02", Direction = "income", Category = "tuition", Amount = 100 });
        await ctx.SaveChangesAsync();

        Assert.Equal(2, await FinanceCategoryCatalog.EnsureAsync(ctx));
        Assert.Equal(0, await FinanceCategoryCatalog.EnsureAsync(ctx));

        var rent = ctx.FinanceCategoryConfigs.Single(c => c.Key == "type:t-arenda");
        rent.CashFlowActivity = R.Investing;
        rent.IsAutoClassified = false;
        var tuition = ctx.FinanceCategoryConfigs.Single(c => c.Key == "cat:income:tuition");
        tuition.PnlTreatment = R.OtherRevenue; // avtomatik yozuvni "buzamiz" — qayta tasniflash tiklaydi
        await ctx.SaveChangesAsync();

        Assert.Equal(1, await FinanceCategoryCatalog.ReclassifyAutoAsync(ctx));
        await ctx.SaveChangesAsync();
        Assert.Equal(R.Investing, ctx.FinanceCategoryConfigs.Single(c => c.Key == "type:t-arenda").CashFlowActivity);
        Assert.Equal(R.TuitionRevenue, ctx.FinanceCategoryConfigs.Single(c => c.Key == "cat:income:tuition").PnlTreatment);
    }

    // ============================ Pul oqimi ============================

    [Fact]
    public async Task PulOqimi_boshlangich_qoldiq_kochirish_va_bekor_qilingan_amal_TASIR_QILMAYDI()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        // 2025: 5 560 000 + 11 680 000 + 2 920 000 = 20 160 000 → 2026 boshlang'ich qoldiq.
        ctx.FinanceTransactions.AddRange(
            new FinanceTransaction { Date = "2025-03-10", Direction = "income", Category = "tuition", Amount = 5_560_000 },
            new FinanceTransaction { Date = "2025-07-10", Direction = "income", Category = "tuition", Amount = 11_680_000 },
            new FinanceTransaction { Date = "2025-12-30", Direction = "income", Category = "book", Amount = 2_920_000 },
            new FinanceTransaction { Date = "2025-12-30", Direction = "income", Category = "tuition", Amount = 999, IsVoided = true },
            new FinanceTransaction { Date = "2026-01-05", Direction = "income", Category = "tuition", Amount = 1_000_000 },
            new FinanceTransaction { Date = "2026-01-20", Direction = "expense", Category = "rent", Amount = 400_000 },
            new FinanceTransaction { Date = "2026-02-03", Direction = "expense", Category = "supplies", Amount = 300_000 });
        ctx.Cashboxes.AddRange(new Cashbox { Id = "a", Name = "A" }, new Cashbox { Id = "b", Name = "B" });
        ctx.CashboxTransfers.Add(new CashboxTransfer { FromCashboxId = "a", ToCashboxId = "b", Amount = 500_000, Date = "2026-01-10" });
        await ctx.SaveChangesAsync();

        var y = await CashFlowReport.BuildAsync(ctx, 2026);

        Assert.Equal(20_160_000m, y.InitialBalance);
        var op = y.Sections.Single(s => s.Key == R.Operating);
        Assert.Equal(1_000_000m, op.TotalIn[0]);
        Assert.Equal(400_000m, op.TotalOut[0]);
        Assert.Equal(600_000m, op.Net[0]);
        var inv = y.Sections.Single(s => s.Key == R.Investing);
        Assert.Equal(300_000m, inv.TotalOut[1]); // jihoz/materiallar → investitsiya
        for (var i = 0; i < 12; i++)
        {
            Assert.Equal(y.StartBalances[i] + y.NetCashFlow[i], y.EndBalances[i]);
            if (i > 0) Assert.Equal(y.EndBalances[i - 1], y.StartBalances[i]);
        }
        Assert.Equal(20_160_000m + 600_000m - 300_000m, y.EndBalances[11]);

        var m = CashFlowReport.ToMonth(y, 1);
        Assert.Equal(20_160_000m, m.StartBalance);
        Assert.Equal(20_760_000m, m.EndBalance);
    }

    [Fact]
    public async Task PulOqimi_excluded_qator_kirmaydi_va_kassa_bilan_farq_korsatiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var y0 = DateTime.Now.Year;
        ctx.TransactionTypes.Add(new TransactionType { Id = "fix", Name = "Tuzatish", Kind = "chiqim", Direction = "expense", BaseCategory = "other" });
        ctx.Cashboxes.Add(new Cashbox { Id = "main", Name = "Asosiy" });
        ctx.FinanceTransactions.AddRange(
            new FinanceTransaction { Date = $"{y0}-01-05", Direction = "income", Category = "tuition", Amount = 1_000, CashboxId = "main" },
            new FinanceTransaction { Date = $"{y0}-01-06", Direction = "expense", Category = "other", TypeId = "fix", Amount = 100, CashboxId = "main" });
        await ctx.SaveChangesAsync();
        await FinanceCategoryCatalog.EnsureAsync(ctx);
        ctx.FinanceCategoryConfigs.Single(c => c.Key == "type:fix").CashFlowActivity = R.ExcludedActivity;
        await ctx.SaveChangesAsync();

        var y = await CashFlowReport.BuildAsync(ctx, y0);
        Assert.Equal(1_000m, y.EndBalances[11]);
        Assert.True(y.Reconciliation.Applicable);
        Assert.Equal(900m, y.Reconciliation.CashboxBalanceNow);
        Assert.Equal(-100m, y.Reconciliation.Difference);
    }

    // ============================ P&L ============================

    private static async Task<TestDb> PnlFixtureAsync()
    {
        var db = TestDb.Sqlite();
        var ctx = db.Context;
        var g = new Group { Id = "g1", Name = "A", MonthlyFee = 1_000_000 };
        ctx.Classes.Add(g);
        ctx.Students.AddRange(new Student { Id = "s1", FullName = "Ali" }, new Student { Id = "s2", FullName = "Vali" });
        ctx.StudentGroups.AddRange(
            new StudentGroup { StudentId = "s1", GroupId = "g1", Status = "active", ActivatedAt = "2026-08-01", JoinedAt = "2026-08-01", IsActive = true },
            new StudentGroup { StudentId = "s2", GroupId = "g1", Status = "active", ActivatedAt = "2026-09-16", JoinedAt = "2026-09-16", IsActive = true });
        ctx.MonthlyCharges.AddRange(
            new MonthlyCharge { StudentId = "s1", GroupId = "g1", Month = "2026-09", Amount = 1_000_000, Discount = 100_000, Date = "2026-09-01" },
            new MonthlyCharge { StudentId = "s2", GroupId = "g1", Month = "2026-09", Amount = 500_000, Discount = 0, Date = "2026-09-16" });
        ctx.TransactionTypes.Add(new TransactionType { Id = "rent", Name = "Arenda", Kind = "chiqim", Direction = "expense", BaseCategory = "rent" });
        ctx.FinanceTransactions.AddRange(
            new FinanceTransaction { Date = "2026-09-05", Direction = "income", Category = "tuition", Amount = 900_000, StudentId = "s1", Month = "2026-09" },
            new FinanceTransaction { Date = "2026-09-06", Direction = "income", Category = "tuition", Amount = 100_000, StudentId = "s2", Month = "2026-08" },
            new FinanceTransaction { Date = "2026-09-07", Direction = "expense", Category = "refund", Amount = 50_000, StudentId = "s1", Month = "2026-09" },
            new FinanceTransaction { Date = "2026-09-08", Direction = "income", Category = "book", Amount = 75_000 },
            new FinanceTransaction { Date = "2026-09-09", Direction = "expense", Category = "book", Amount = 25_000 },
            new FinanceTransaction { Date = "2026-09-10", Direction = "expense", Category = "rent", TypeId = "rent", Amount = 3_000_000 },
            new FinanceTransaction { Date = "2026-09-11", Direction = "expense", Category = "internet", Amount = 200_000 },
            new FinanceTransaction { Date = "2026-09-12", Direction = "expense", Category = "salary", Amount = 500_000, SalaryPayKind = "advance" });
        ctx.PlannedExpenseTemplates.Add(new PlannedExpenseTemplate
        {
            Id = "tpl", Name = "Ijara", TypeId = "rent", Category = "rent", Amount = 4_000_000, StartDate = "2026-01-01", Status = "active",
        });
        await ctx.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task PnL_formulalari_va_kopriк_oxirgi_somgacha_yopiladi()
    {
        using var db = await PnlFixtureAsync();
        var p = await PnlEngine.ComputeAsync(db.Context, "2026-09", null);
        var t = p.Total;

        Assert.Equal("2026-09-30", p.AsOf);
        Assert.Equal(1_500_000m, t.Revenue.Gross.Current);
        Assert.Equal(100_000m, t.Revenue.Discount.Current);
        Assert.Equal(50_000m, t.Revenue.Refunds.Current);
        Assert.Equal(1_350_000m, t.Revenue.Net.Current);                 // 1 500 000 − 100 000 − 50 000
        Assert.Equal(50_000m, t.Revenue.OtherRevenue.Current);           // kitob 75 000 − qaytarish 25 000

        // Xarajat: shablon 4 000 000 (to'langani 3 000 000 — xarajatga ikkinchi marta qo'shilmaydi) + rejasiz internet 200 000.
        // Avans (maosh) P&L ga kirmaydi.
        Assert.Equal(4_200_000m, t.Expenses.Current);
        Assert.Equal(1_000_000m, t.Expenses.Unpaid);
        Assert.Contains(t.Expenses.Lines, l => l.Id == "type:rent" && l.Planned == 4_000_000 && l.Paid == 3_000_000 && !l.IsUnplanned);
        Assert.Contains(t.Expenses.Lines, l => l.Id == "cat:expense:internet" && l.IsUnplanned);

        Assert.Equal(1_350_000m + 50_000m - 4_200_000m, t.Result.ProfitCurrent);
        Assert.Equal(Math.Round(t.Result.ProfitCurrent / 1_400_000m * 100m, 2), t.Result.MarginCurrent);

        // O'quvchilar: oy boshida 1 ta faol, 16-sanada yana biri → 2 ta, 1 ta yangi.
        Assert.Equal((1, 2, 1, 0), (t.Students.Planned, t.Students.Current, t.Students.Joined, t.Students.Left));

        // Kassa: 900 000 shu oy uchun, 100 000 o'tgan oy qarzidan.
        Assert.Equal(1_000_000m, t.Cash.Collected);
        Assert.Equal(900_000m, t.Cash.ForThisMonth);
        Assert.Equal(100_000m, t.Cash.ForPriorMonths);
        Assert.Equal(1_400_000m - 900_000m, t.Cash.Uncollected);

        // Shu oy uchun yig'ilish (oy tegi): s1 — hisob 900 000, to'lagan 900 − 50 (vozvrat) = 850 000 → qisman; s2 — to'lamagan.
        Assert.Equal((2, 0, 1, 1), (t.PeriodCollection.StudentsExpected, t.PeriodCollection.StudentsPaid,
            t.PeriodCollection.StudentsPartial, t.PeriodCollection.StudentsUnpaid));

        // Ko'prik: haqiqiy pul o'zgarishiga AYNAN teng.
        var b = p.CashBridge;
        Assert.Equal(1_000_000m + 75_000m - 25_000m - 50_000m - 3_000_000m - 200_000m - 500_000m, b.ActualCashDelta);
        Assert.Equal(0m, b.Difference);
        Assert.True(b.CashDeltaIsComplete);
    }

    [Fact]
    public async Task PnL_tuzatilgan_shablon_va_holat_sanasi()
    {
        using var db = await PnlFixtureAsync();
        db.Context.PlannedExpenseAccruals.Add(new PlannedExpenseAccrual { TemplateId = "tpl", Month = "2026-09", Amount = 4_000_000, CorrectedAmount = 3_500_000 });
        await db.Context.SaveChangesAsync();

        var p = await PnlEngine.ComputeAsync(db.Context, "2026-09", "2026-09-10");
        var rent = p.Total.Expenses.Lines.Single(l => l.Id == "type:rent");
        Assert.Equal((4_000_000m, 3_500_000m, true), (rent.Planned, rent.Current, rent.IsCorrected));
        // 10-sanagacha: s2 hali faol emas, uning hisobi (16-sana) kutuvga kirmaydi, internet (11-sana) ham.
        Assert.Equal(1_000_000m, p.Total.Revenue.Gross.Current);
        Assert.Equal(1, p.Total.Students.Current);
        Assert.DoesNotContain(p.Total.Expenses.Lines, l => l.Id == "cat:expense:internet");
        Assert.Equal(0m, p.CashBridge.Difference);
    }

    [Fact]
    public async Task Yillik_oy_qiymati_PnL_jadvali_bilan_BIR_XIL()
    {
        using var db = await PnlFixtureAsync();
        var month = await PnlEngine.ComputeAsync(db.Context, "2026-09", null);
        var year = await PnlEngine.YearlyAsync(db.Context, 2026);
        var sep = year.Months[8];
        Assert.Equal(month.Total.Revenue.Net.Current, sep.Net);
        Assert.Equal(month.Total.Expenses.Current, sep.Expense);
        Assert.Equal(month.Total.Result.ProfitCurrent, sep.Profit);
        Assert.Equal(month.Total.Students.Current, sep.Students);
        // YILLIK marja = yillik foyda / yillik daromad (sof kutuv + boshqa daromad).
        var revenue = year.Months.Where(m => !m.IsForecast).Sum(m => m.Net + m.OtherRevenue);
        Assert.Equal(Math.Round(year.Totals.Profit / revenue * 100m, 2), year.Totals.Margin);
    }

    [Fact]
    public void Shablon_ish_haqi_va_vozvrat_turiga_qoyilmaydi()
    {
        Assert.Null(PlannedExpenseRules.TreatmentError(R.Expense));
        Assert.NotNull(PlannedExpenseRules.TreatmentError(R.Salary));
        Assert.NotNull(PlannedExpenseRules.TreatmentError(R.RevenueContra));
        Assert.Equal("Summa 0 dan katta bo'lsin",
            PlannedExpenseRules.Validate(new("Ijara", null, "rent", 0, "2026-01-01", null, null, null, null)));
        Assert.Equal("Tugash sanasi boshlanishdan oldin bo'lmasin",
            PlannedExpenseRules.Validate(new("Ijara", null, "rent", 10, "2026-05-01", "2026-04-01", null, null, null)));
    }
}

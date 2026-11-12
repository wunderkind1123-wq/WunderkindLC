using WunderkindLC.Application.Services.FinanceReports2;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Egasi (2026-11-12): «xarajatlarning hammasi qaysi oy uchun deb yozilgan bo'lsa o'sha oy uchun hisoblansin». P&L xarajati
/// `FinanceTransaction.Month` bo'yicha (oyi yo'q eski chiqim — to'lov kuni bo'yicha); P&L → pul ko'prigi esa KASSA bo'yicha
/// yopiladi. `billing.md` «XARAJAT — QAYSI OY UCHUN».
/// </summary>
public class PnlExpenseMonthTests
{
    [Fact]
    public async Task Oktabr_ijarasi_noyabrda_tolansa__PnL_da_OKTABR_xarajati_noyabrda_yoq_va_koprik_yopiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.FinanceTransactions.AddRange(
            // oktabr ijarasi — 4-noyabrda to'langan
            new FinanceTransaction { Direction = "expense", Category = "rent", Month = "2026-10", Amount = 2_000_000m, Date = "2026-11-04" },
            // eski yozuv (oyi yo'q) — sana bo'yicha noyabrda
            new FinanceTransaction { Direction = "expense", Category = "utilities", Amount = 300_000m, Date = "2026-11-06" });
        await ctx.SaveChangesAsync();

        var oct = await PnlEngine.ComputeAsync(ctx, "2026-10", "2026-10-31");
        var rentOct = Assert.Single(oct.Total.Expenses.Lines, l => l.Id.Contains("rent"));
        Assert.Equal(2_000_000m, rentOct.Current);
        Assert.DoesNotContain(oct.Total.Expenses.Lines, l => l.Id.Contains("utilities"));

        var nov = await PnlEngine.ComputeAsync(ctx, "2026-11", "2026-11-30");
        Assert.DoesNotContain(nov.Total.Expenses.Lines, l => l.Id.Contains("rent"));
        Assert.Equal(300_000m, Assert.Single(nov.Total.Expenses.Lines, l => l.Id.Contains("utilities")).Current);
        // Ko'prik kassa bo'yicha: noyabrda kassadan 2 300 000 chiqdi — farq 0.
        Assert.Equal(2_300_000m, nov.CashBridge.ExpensePaid);
        Assert.Equal(0m, nov.CashBridge.Difference);
    }

    [Theory]
    [InlineData("2026-10", "2026-11-04", "2026-10", true)]   // oy tegi — sanadan qat'i nazar
    [InlineData("2026-10", "2026-10-04", "2026-11", false)]
    [InlineData(null, "2026-11-04", "2026-11", true)]        // oyi yo'q — sana bo'yicha
    [InlineData(null, "2026-10-30", "2026-11", false)]
    [InlineData("", "2026-11-04", "2026-11", true)]
    public void ExpenseInMonth__oy_tegi_ustun_yoqsa_sana(string? txMonth, string date, string month, bool expected)
    {
        var entry = new FinanceCategoryCatalog.Entry("rent", "Ijara", "expense", "operating", FinanceCategoryRules.Expense,
            "Xarajat", 0, null, "rent");
        var t = new PnlEngine.Tx("1", date, "expense", 1m, "rent", txMonth, null, null, null, entry);
        Assert.Equal(expected, PnlEngine.ExpenseInMonth(t, month, d => d.StartsWith(month, StringComparison.Ordinal)));
    }
}

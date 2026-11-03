using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.FinanceReports2;

/// <summary>Rejali xarajat shablonlari — tekshiruv va oylik hisob (sof qismi testlangan: <c>PnlEngineTests</c>).</summary>
public static class PlannedExpenseRules
{
    public sealed record Payload(
        string? Name, string? TypeId, string? Category, decimal Amount, string? StartDate, string? EndDate,
        string? Status, int? ReminderDay, string? Note);

    /// <summary>Shablonga ruxsat etilgan P&amp;L o'rni — FAQAT oddiy xarajat. ⚠️ Ish haqi maosh reestridan O'ZI hisoblanadi
    /// (shablon bilan ikki marta sanalardi), vozvrat/chegirma/kitob qaytarish — xarajat emas.</summary>
    public static string? TreatmentError(string treatment) => treatment == FinanceCategoryRules.Expense ? null
        : treatment is FinanceCategoryRules.Salary or FinanceCategoryRules.SalaryAdvance
            ? "Ish haqi shablon bilan kiritilmaydi — u maosh reestridan avtomatik hisoblanadi"
            : "Bu tur P&L da xarajat emas — shablon faqat xarajat turi uchun (Moliya → Hisobot toifalari)";

    /// <summary>Maydonlar tekshiruvi (turi/toifasi alohida — controller katalog bilan tekshiradi).</summary>
    public static string? Validate(Payload p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return "Nomini kiriting";
        if (p.Name.Trim().Length > 200) return "Nomi 200 belgidan oshmasin";
        if (p.Amount <= 0) return "Summa 0 dan katta bo'lsin";
        if (p.Amount > 100_000_000_000m) return "Summa juda katta";
        if (!DateOnly.TryParseExact(p.StartDate, "yyyy-MM-dd", out var start)) return "Boshlanish sanasini kiriting";
        if (!string.IsNullOrWhiteSpace(p.EndDate))
        {
            if (!DateOnly.TryParseExact(p.EndDate, "yyyy-MM-dd", out var end)) return "Tugash sanasi noto'g'ri";
            if (end < start) return "Tugash sanasi boshlanishdan oldin bo'lmasin";
        }
        if (p.Status is not (null or "" or "active" or "inactive")) return "Holat noto'g'ri";
        if (p.ReminderDay is { } d && d is < 1 or > 28) return "Eslatma kuni 1–28 oralig'ida bo'lsin";
        if (string.IsNullOrWhiteSpace(p.TypeId) && string.IsNullOrWhiteSpace(p.Category)) return "Tranzaksiya turini tanlang";
        return null;
    }

    /// <summary>Joriy oy uchun faol shablonlarning oylik yozuvini yaratadi (idempotent). Saqlaydi. Qaytadi: nechta yaratildi.</summary>
    public static async Task<int> EnsureAccrualsAsync(IAppDbContext db, string month, CancellationToken ct = default)
    {
        var m01 = month + "-01";
        var monthEnd = DateOnly.Parse(m01).AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");
        var templates = (await db.PlannedExpenseTemplates.AsNoTracking().Where(t => !t.IsDeleted && t.Status == "active").ToListAsync(ct))
            .Where(t => PnlEngine.TemplateActive(t, m01, monthEnd)).ToList();
        if (templates.Count == 0) return 0;
        var have = (await db.PlannedExpenseAccruals.AsNoTracking().Where(a => a.Month == month).Select(a => a.TemplateId).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var created = 0;
        foreach (var t in templates.Where(t => !have.Contains(t.Id)))
        {
            db.PlannedExpenseAccruals.Add(new PlannedExpenseAccrual
            {
                TemplateId = t.Id, Month = month, Amount = t.Amount, CreatedAt = AppClock.Now.ToString("o"),
            });
            created++;
        }
        if (created == 0) return 0;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Parallel yugurish AYNI (shablon, oy) ni yozgan — unikal indeks; o'zimiznikini ajratamiz.
            foreach (var e in db.PlannedExpenseAccruals.Local.Where(x => db.PlannedExpenseAccruals.Entry(x).State == EntityState.Added).ToList())
                db.PlannedExpenseAccruals.Entry(e).State = EntityState.Detached;
            return 0;
        }
        return created;
    }
}

/// <summary>
/// Har oy boshida (startupdan 1 daqiqa keyin va har 6 soatda) faol shablonlarning JORIY oy yozuvini yaratadi — keyin shablon
/// summasi tahrirlansa o'tgan oylar qayta yozilmaydi. Yozuv yo'q oy uchun P&amp;L baribir shablon summasini oladi.
/// </summary>
public sealed class PlannedExpenseAccrualService(IServiceScopeFactory scopes, ILogger<PlannedExpenseAccrualService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                var n = await PlannedExpenseRules.EnsureAccrualsAsync(db, AppClock.Today.ToString("yyyy-MM"), stoppingToken);
                if (n > 0) logger.LogInformation("Rejali xarajat: {Count} ta oylik yozuv yaratildi", n);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Rejali xarajat oylik yozuvlari yaratilmadi"); }
            try { await Task.Delay(TimeSpan.FromHours(6), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}

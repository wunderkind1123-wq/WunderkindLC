using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.FinanceReports2;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// PUL OQIMI 2.0 · P&amp;L 2.0 (<c>docs/finance-reports/</c>). Moliya → Hisobotlar ostida, eski «P&amp;L» va «Pul oqimi»
/// sahifalari o'z joyida qoladi.
///
/// <para>Ruxsat: o'qish ham darvozalangan (javobda o'quvchi ismlari, summalar, maosh). Sahifa kalitlari
/// <c>finance.cashflow</c> / <c>finance.pnl</c>; mavjud <c>finance.main</c> ruxsatli xodim ham avtomatik ko'radi.
/// Sozlamalar (toifalar, shablonlar, tuzatish) — <c>finance.main</c> ning yozish amallari.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance.main", ReadRequiresPerm = true)]
[Route("api/admin/finance/v2")]
public class FinanceReports2Controller(AppDbContext db, AuditService audit, DataCache dataCache) : ControllerBase
{
    private static readonly string[] CashDeps =
    [
        nameof(FinanceTransaction), nameof(TransactionType), nameof(FinanceCategoryConfig), nameof(Cashbox), nameof(CashboxTransfer),
    ];

    private static readonly string[] PnlDeps =
    [
        nameof(FinanceTransaction), nameof(TransactionType), nameof(FinanceCategoryConfig), nameof(MonthlyCharge), nameof(LessonDiscount),
        nameof(StudentDiscount), nameof(StudentGroup), nameof(Student), nameof(Group), nameof(Subject), nameof(PlannedExpenseTemplate),
        nameof(PlannedExpenseAccrual), nameof(Teacher), nameof(StaffSalary), nameof(KpiMonthResult), nameof(JournalEntry), nameof(LessonNote),
    ];

    private string Actor => User.FindFirst(ClaimTypes.Name)?.Value ?? "";

    // ============================ PUL OQIMI ============================

    [HttpGet("cash-flow")]
    [AdminPerm("finance.cashflow", "finance.main", ReadRequiresPerm = true)]
    public async Task<IActionResult> CashFlow([FromQuery] int? year, [FromQuery] int? month)
    {
        var y = year ?? AppClock.Today.Year;
        if (y is < 2000 or > 2100) return BadRequest(new { message = "Yil noto'g'ri" });
        if (month is not null and (< 1 or > 12)) return BadRequest(new { message = "Oy noto'g'ri" });
        await FinanceCategoryCatalog.EnsureAsync(db);
        var data = await dataCache.GetOrCreateAsync($"fin2:cf:{y}:{AppClock.Today:yyyy-MM-dd}", CashDeps, TimeSpan.FromMinutes(5),
            x => CashFlowReport.BuildAsync(x, y));
        return month is null ? Ok(data) : Ok(CashFlowReport.ToMonth(data, month.Value));
    }

    [HttpGet("cash-flow/transactions")]
    [AdminPerm("finance.cashflow", "finance.main", ReadRequiresPerm = true)]
    public async Task<IActionResult> CashFlowTransactions(
        [FromQuery] int year, [FromQuery] int? month, [FromQuery] string rowKey, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (string.IsNullOrWhiteSpace(rowKey)) return BadRequest(new { message = "Qator ko'rsatilmagan" });
        return Ok(await CashFlowReport.TransactionsAsync(db, year, month ?? 0, rowKey, page, pageSize));
    }

    [HttpGet("cash-flow/export")]
    [AdminPerm("finance.cashflow", "finance.main", ReadRequiresPerm = true)]
    public async Task<IActionResult> CashFlowExport([FromQuery] int? year, [FromQuery] int? month)
    {
        var y = year ?? AppClock.Today.Year;
        if (y is < 2000 or > 2100) return BadRequest(new { message = "Yil noto'g'ri" });
        var data = await CashFlowReport.BuildAsync(db, y);
        var bytes = month is >= 1 and <= 12
            ? Application.Services.FinanceReports2.CashFlowExport.Month(CashFlowReport.ToMonth(data, month.Value))
            : Application.Services.FinanceReports2.CashFlowExport.Year(data);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Application.Services.FinanceReports2.CashFlowExport.FileName(y, month));
    }

    // ============================ P&L ============================

    private static bool TryMonth(int year, int month, out string ym)
    {
        ym = $"{year:0000}-{month:00}";
        return year is >= 2000 and <= 2100 && month is >= 1 and <= 12;
    }

    [HttpGet("pnl")]
    [AdminPerm("finance.pnl", "finance.main", ReadRequiresPerm = true)]
    public async Task<IActionResult> Pnl([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? asOf)
    {
        var today = AppClock.Today;
        if (!TryMonth(year ?? today.Year, month ?? today.Month, out var ym)) return BadRequest(new { message = "Davr noto'g'ri" });
        var a = PnlEngine.ClampAsOf(ym, asOf, today.ToString("yyyy-MM-dd"));
        await FinanceCategoryCatalog.EnsureAsync(db);
        return Ok(await dataCache.GetOrCreateAsync($"fin2:pnl:{ym}:{a}:{today:yyyy-MM-dd}", PnlDeps, TimeSpan.FromMinutes(5),
            x => PnlEngine.ComputeAsync(x, ym, a)));
    }

    [HttpGet("pnl/drilldown")]
    [AdminPerm("finance.pnl", "finance.main", ReadRequiresPerm = true)]
    public async Task<IActionResult> PnlDrilldown(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] string? asOf, [FromQuery] string metric,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (!TryMonth(year, month, out var ym)) return BadRequest(new { message = "Davr noto'g'ri" });
        if (string.IsNullOrWhiteSpace(metric)) return BadRequest(new { message = "Ko'rsatkich ko'rsatilmagan" });
        var data = await PnlEngine.LoadAsync(db, ym, ym);
        return Ok(PnlEngine.Drilldown(data, ym, PnlEngine.ClampAsOf(ym, asOf, data.Today), metric, page, pageSize));
    }

    [HttpGet("pnl/yearly")]
    [AdminPerm("finance.pnl", "finance.main", ReadRequiresPerm = true)]
    public async Task<IActionResult> PnlYearly([FromQuery] int? year)
    {
        var y = year ?? AppClock.Today.Year;
        if (y is < 2000 or > 2100) return BadRequest(new { message = "Yil noto'g'ri" });
        await FinanceCategoryCatalog.EnsureAsync(db);
        return Ok(await dataCache.GetOrCreateAsync($"fin2:pnly:{y}:{AppClock.Today:yyyy-MM-dd}", PnlDeps, TimeSpan.FromMinutes(5),
            x => PnlEngine.YearlyAsync(x, y)));
    }

    // ============================ HISOBOT TOIFALARI ============================

    public sealed record CategoryRowDto(
        string Id, string Key, string Name, string BaseName, string Direction, string? TypeId, string? Category, string? Kind,
        string CashFlowActivity, string PnlTreatment, string? PnlGroup, int SortOrder, bool IsAutoClassified, string AutoReason,
        int Count, decimal Last12Months, string UpdatedAt, string? UpdatedBy);

    [HttpGet("categories")]
    public async Task<ActionResult<List<CategoryRowDto>>> Categories()
    {
        await FinanceCategoryCatalog.EnsureAsync(db);
        var types = await db.TransactionTypes.AsNoTracking().ToDictionaryAsync(t => t.Id);
        var since = AppClock.Today.AddMonths(-12).ToString("yyyy-MM-dd");
        var stats = (await db.FinanceTransactions.AsNoTracking()
                .Select(t => new { t.TypeId, t.Direction, t.Category, t.Amount, t.Date }).ToListAsync())
            .GroupBy(t => FinanceCategoryRules.KeyOf(t.TypeId, t.Direction == "expense" ? "expense" : "income",
                string.IsNullOrWhiteSpace(t.Category) ? "other" : t.Category))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Sum: g.Where(x => string.CompareOrdinal(x.Date ?? "", since) >= 0).Sum(x => x.Amount)));
        var rows = await db.FinanceCategoryConfigs.AsNoTracking().ToListAsync();
        return rows.Select(c =>
            {
                var t = c.TypeId is not null && types.TryGetValue(c.TypeId, out var tt) ? tt : null;
                var baseName = t?.Name ?? FinanceCategoryRules.CategoryLabel(c.SystemCategory ?? "", c.Direction);
                var s = stats.GetValueOrDefault(c.Key);
                return new CategoryRowDto(c.Id, c.Key, string.IsNullOrWhiteSpace(c.DisplayName) ? baseName : c.DisplayName, baseName,
                    c.Direction, c.TypeId, c.SystemCategory ?? t?.BaseCategory, t?.Kind, c.CashFlowActivity, c.PnlTreatment, c.PnlGroup,
                    c.SortOrder, c.IsAutoClassified, c.AutoReason, s.Count, s.Sum, c.UpdatedAt, c.UpdatedBy);
            })
            .OrderBy(r => r.Direction == "income" ? 0 : 1).ThenBy(r => r.SortOrder).ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }

    public sealed record CategoryPayload(string? DisplayName, string? CashFlowActivity, string? PnlTreatment, string? PnlGroup, int? SortOrder);

    [HttpPut("categories/{id}")]
    public async Task<IActionResult> UpdateCategory(string id, [FromBody] CategoryPayload p)
    {
        var c = await db.FinanceCategoryConfigs.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound(new { message = "Toifa topilmadi" });
        var activity = p.CashFlowActivity ?? c.CashFlowActivity;
        var treatment = p.PnlTreatment ?? c.PnlTreatment;
        if (!FinanceCategoryRules.Activities.Contains(activity)) return BadRequest(new { message = "Pul oqimi faoliyati noto'g'ri" });
        if (!FinanceCategoryRules.Treatments.Contains(treatment)) return BadRequest(new { message = "P&L dagi o'rni noto'g'ri" });
        if (!FinanceCategoryRules.TreatmentFits(c.Direction, treatment))
            return BadRequest(new { message = c.Direction == "income" ? "Kirim turi bu o'ringa qo'yilmaydi" : "Chiqim turi bu o'ringa qo'yilmaydi" });
        if (p.DisplayName is { Length: > 200 } || p.PnlGroup is { Length: > 120 }) return BadRequest(new { message = "Matn juda uzun" });
        if (treatment != FinanceCategoryRules.Expense && c.Direction == "expense"
            && await db.PlannedExpenseTemplates.AnyAsync(t => !t.IsDeleted && (c.TypeId != null ? t.TypeId == c.TypeId : t.TypeId == null && t.Category == c.SystemCategory)))
            return BadRequest(new { message = "Bu turda rejali xarajat shabloni bor — avval shablonni o'chiring yoki boshqa turga o'tkazing" });

        var before = $"{c.CashFlowActivity}/{c.PnlTreatment}/{c.PnlGroup}";
        c.CashFlowActivity = activity;
        c.PnlTreatment = treatment;
        if (p.DisplayName is not null) c.DisplayName = p.DisplayName.Trim();
        if (p.PnlGroup is not null) c.PnlGroup = string.IsNullOrWhiteSpace(p.PnlGroup) ? null : p.PnlGroup.Trim();
        if (p.SortOrder is { } so) c.SortOrder = Math.Clamp(so, 0, 100_000);
        c.IsAutoClassified = false;
        c.UpdatedAt = AppClock.Now.ToString("o");
        c.UpdatedBy = Actor;
        audit.Record("FinanceCategoryConfig", c.Id, "update",
            $"Hisobot toifasi o'zgartirildi: {c.Key} — {before} → {c.CashFlowActivity}/{c.PnlTreatment}/{c.PnlGroup}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("categories/reclassify")]
    public async Task<IActionResult> Reclassify()
    {
        await FinanceCategoryCatalog.EnsureAsync(db);
        var n = await FinanceCategoryCatalog.ReclassifyAutoAsync(db);
        if (n > 0)
        {
            audit.Record("FinanceCategoryConfig", "reclassify", "update", $"Hisobot toifalari qayta avtomatik tasniflandi: {n} ta yozuv o'zgardi");
            await db.SaveChangesAsync();
        }
        return Ok(new { changed = n });
    }

    // ============================ REJALI XARAJAT SHABLONLARI ============================

    public sealed record TemplateDto(
        string Id, string Name, string? TypeId, string Category, string TypeName, decimal Amount, string Frequency, string StartDate,
        string EndDate, string Status, int? ReminderDay, string? Note, string CreatedAt, string? CreatedBy);

    private async Task<Dictionary<string, string>> TypeNamesAsync() =>
        await db.TransactionTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name);

    private static TemplateDto Map(PlannedExpenseTemplate t, IReadOnlyDictionary<string, string> types) => new(
        t.Id, t.Name, t.TypeId, t.Category,
        t.TypeId is not null && types.TryGetValue(t.TypeId, out var n) ? n : FinanceCategoryRules.CategoryLabel(t.Category, "expense"),
        t.Amount, t.Frequency, t.StartDate, t.EndDate, t.Status, t.ReminderDay, t.Note, t.CreatedAt, t.CreatedBy);

    [HttpGet("planned-templates")]
    public async Task<ActionResult<List<TemplateDto>>> Templates()
    {
        var types = await TypeNamesAsync();
        return (await db.PlannedExpenseTemplates.AsNoTracking().Where(t => !t.IsDeleted).ToListAsync())
            .OrderBy(t => t.Status == "active" ? 0 : 1).ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => Map(t, types)).ToList();
    }

    /// <summary>Turi/toifasi tekshiruvi — FAQAT xarajat sifatida tasniflangan chiqim turi.</summary>
    private async Task<string?> TypeErrorAsync(PlannedExpenseRules.Payload p)
    {
        var catalog = await FinanceCategoryCatalog.LoadAsync(db);
        if (!string.IsNullOrWhiteSpace(p.TypeId))
        {
            var t = await db.TransactionTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p.TypeId);
            if (t is null) return "Tranzaksiya turi topilmadi";
            if (t.Direction != "expense") return "Faqat chiqim turi tanlanadi";
            return PlannedExpenseRules.TreatmentError(catalog.Resolve(t.Id, "expense", t.BaseCategory).Treatment);
        }
        var cat = p.Category!.Trim();
        return PlannedExpenseRules.TreatmentError(catalog.Resolve(null, "expense", cat).Treatment);
    }

    [HttpPost("planned-templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] PlannedExpenseRules.Payload p)
    {
        if (PlannedExpenseRules.Validate(p) is { } err) return BadRequest(new { message = err });
        if (await TypeErrorAsync(p) is { } terr) return BadRequest(new { message = terr });
        var t = new PlannedExpenseTemplate
        {
            Name = p.Name!.Trim(), TypeId = string.IsNullOrWhiteSpace(p.TypeId) ? null : p.TypeId,
            Category = string.IsNullOrWhiteSpace(p.TypeId) ? p.Category!.Trim() : (await db.TransactionTypes.AsNoTracking()
                .Where(x => x.Id == p.TypeId).Select(x => x.BaseCategory).FirstAsync()),
            Amount = p.Amount, StartDate = p.StartDate!, EndDate = p.EndDate?.Trim() ?? "", Status = string.IsNullOrEmpty(p.Status) ? "active" : p.Status,
            ReminderDay = p.ReminderDay, Note = p.Note?.Trim(), CreatedAt = AppClock.Now.ToString("o"), CreatedBy = Actor,
        };
        db.PlannedExpenseTemplates.Add(t);
        audit.Record("PlannedExpenseTemplate", t.Id, "create",
            $"Rejali xarajat shabloni qo'shildi: {t.Name} — {AuditService.Money(t.Amount)} so'm/oy ({t.StartDate}{(t.EndDate == "" ? "" : " — " + t.EndDate)})");
        await db.SaveChangesAsync();
        return Ok(Map(t, await TypeNamesAsync()));
    }

    [HttpPut("planned-templates/{id}")]
    public async Task<IActionResult> UpdateTemplate(string id, [FromBody] PlannedExpenseRules.Payload p)
    {
        var t = await db.PlannedExpenseTemplates.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (t is null) return NotFound(new { message = "Shablon topilmadi" });
        if (PlannedExpenseRules.Validate(p) is { } err) return BadRequest(new { message = err });
        if (await TypeErrorAsync(p) is { } terr) return BadRequest(new { message = terr });
        var before = $"{t.Name} · {AuditService.Money(t.Amount)} · {t.StartDate}—{t.EndDate} · {t.Status}";
        t.Name = p.Name!.Trim();
        t.TypeId = string.IsNullOrWhiteSpace(p.TypeId) ? null : p.TypeId;
        t.Category = t.TypeId is null ? p.Category!.Trim()
            : await db.TransactionTypes.AsNoTracking().Where(x => x.Id == t.TypeId).Select(x => x.BaseCategory).FirstAsync();
        t.Amount = p.Amount;
        t.StartDate = p.StartDate!;
        t.EndDate = p.EndDate?.Trim() ?? "";
        t.Status = string.IsNullOrEmpty(p.Status) ? t.Status : p.Status;
        t.ReminderDay = p.ReminderDay;
        t.Note = p.Note?.Trim();
        audit.Record("PlannedExpenseTemplate", t.Id, "update",
            $"Rejali xarajat shabloni tahrirlandi: {before} → {t.Name} · {AuditService.Money(t.Amount)} · {t.StartDate}—{t.EndDate} · {t.Status}. O'tgan oylarning hisobi o'zgarmaydi.");
        await db.SaveChangesAsync();
        return Ok(Map(t, await TypeNamesAsync()));
    }

    [HttpDelete("planned-templates/{id}")]
    public async Task<IActionResult> DeleteTemplate(string id)
    {
        var t = await db.PlannedExpenseTemplates.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (t is null) return NotFound(new { message = "Shablon topilmadi" });
        t.IsDeleted = true;
        t.Status = "inactive";
        audit.Record("PlannedExpenseTemplate", t.Id, "delete", $"Rejali xarajat shabloni o'chirildi: {t.Name} — {AuditService.Money(t.Amount)} so'm/oy");
        await db.SaveChangesAsync();
        return NoContent();
    }

    public sealed record CorrectionPayload(decimal? CorrectedAmount, string? Note);

    /// <summary>«Tuzatish» — shablonning shu oy uchun haqiqiy summasi (hisob-faktura bo'yicha). <c>null</c> — tuzatish olib tashlanadi.</summary>
    [HttpPut("planned-templates/{id}/accruals/{month}")]
    public async Task<IActionResult> CorrectAccrual(string id, string month, [FromBody] CorrectionPayload p)
    {
        if (!PaymentFields.IsMonth(month)) return BadRequest(new { message = "Oy noto'g'ri" });
        if (p.CorrectedAmount is < 0 or > 100_000_000_000m) return BadRequest(new { message = "Summa noto'g'ri" });
        var t = await db.PlannedExpenseTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (t is null) return NotFound(new { message = "Shablon topilmadi" });
        var m01 = month + "-01";
        var monthEnd = DateOnly.Parse(m01).AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");
        var a = await db.PlannedExpenseAccruals.FirstOrDefaultAsync(x => x.TemplateId == id && x.Month == month);
        if (a is null)
        {
            if (!PnlEngine.TemplateActive(t, m01, monthEnd)) return BadRequest(new { message = "Shablon bu oyda faol emas" });
            a = new PlannedExpenseAccrual { TemplateId = id, Month = month, Amount = t.Amount, CreatedAt = AppClock.Now.ToString("o") };
            db.PlannedExpenseAccruals.Add(a);
        }
        var old = a.CorrectedAmount;
        a.CorrectedAmount = p.CorrectedAmount;
        a.CorrectionNote = p.Note?.Trim();
        a.CorrectedBy = Actor;
        a.CorrectedAt = AppClock.Now.ToString("o");
        audit.Record("PlannedExpenseTemplate", id, "update",
            $"Rejali xarajat tuzatildi ({month}): {t.Name} — reja {AuditService.Money(a.Amount)}, tuzatish {(old is null ? "yo'q" : AuditService.Money(old.Value))} → {(p.CorrectedAmount is null ? "olib tashlandi" : AuditService.Money(p.CorrectedAmount.Value))} so'm{(string.IsNullOrWhiteSpace(p.Note) ? "" : " — " + p.Note.Trim())}");
        await db.SaveChangesAsync();
        return NoContent();
    }
}

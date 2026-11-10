using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <param name="Amount">Oylik summa (0 — oylik to'xtatildi).</param>
/// <param name="FromMonth">Shu oydan boshlab ("yyyy-MM"); bo'sh — joriy oy.</param>
public record StaffSalaryPayload(decimal Amount, string? FromMonth, string? Note);

public record PayrollPersonDto(string Id, string FullName, string Role, string Position);

/// <summary>
/// XODIMLAR MAOSHI REESTRI (SPEC-backlog-2026-10-17 F3 K3; hisob — <see cref="PayrollRegister"/>). O'qish va xodim oyligini
/// kiritish — `finance.main` (O'QISH ham: maosh summalari). To'lov — odatdagi maosh chiqimi
/// (<c>POST finance/transactions</c>, <c>staffUserId</c> / <c>salaryPayKind</c>; chegaradan katta bo'lsa — tasdiq).
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("finance.main", ReadRequiresPerm = true)]
[Route("api/admin/finance/payroll")]
public class PayrollController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    [AdminPerm("finance.main", ReadRequiresPerm = true)]
    public async Task<ActionResult<PayrollDto>> Get([FromQuery] string? month, CancellationToken ct)
    {
        var m = PaymentFields.IsMonth(month) ? month! : TuitionService.CurrentMonth();
        return await PayrollRegister.BuildAsync(db, m, ct);
    }

    /// <summary>Oylik belgilash mumkin bo'lgan panel akkauntlari (xodim/admin/superadmin) — reestrda hali qatori yo'q
    /// admin/superadmin oyligini ham kiritish uchun (reestr ularni faqat oylik yoki to'lov bo'lsa ko'rsatadi).</summary>
    [HttpGet("people")]
    [AdminPerm("finance.main", ReadRequiresPerm = true)]
    public async Task<ActionResult<IEnumerable<PayrollPersonDto>>> People(CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.Staff || u.Role == Roles.Admin || u.Role == Roles.SuperAdmin)
            .OrderBy(u => u.FullName)
            .Select(u => new PayrollPersonDto(u.Id, u.FullName, u.Role, u.Position))
            .ToListAsync(ct);

    /// <summary>Xodimning qat'iy oyligi — shu oydan boshlab (versiya; o'tgan oylar o'zgarmaydi). Bir oyga bitta versiya.</summary>
    [HttpPut("staff/{userId}/salary")]
    [AdminPerm("finance.main")]
    public async Task<IActionResult> SetStaffSalary(string userId, StaffSalaryPayload p, CancellationToken ct)
    {
        if (p.Amount < 0) return BadRequest(new { message = "Oylik manfiy bo'lmasin" });
        var from = string.IsNullOrWhiteSpace(p.FromMonth) ? TuitionService.CurrentMonth() : p.FromMonth.Trim();
        if (!PaymentFields.IsMonth(from)) return BadRequest(new { message = "Oy noto'g'ri (yyyy-MM)" });
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && (u.Role == Roles.Staff || u.Role == Roles.Admin || u.Role == Roles.SuperAdmin))
            .Select(u => new { u.Id, u.FullName, u.Role }).FirstOrDefaultAsync(ct);
        if (user is null) return NotFound(new { message = "Xodim topilmadi" });
        // 2026-10-17 (F3 revyu): superadmin bo'lmagan xodim O'Z oyligini, superadmin oyligini va O'TGAN oylarni
        // o'zgartira olmaydi — aks holda "hisoblangan" summani o'zi oshirib, katta to'lovni tasdiqqa olib chiqardi.
        if (!User.IsInRole(Roles.SuperAdmin))
        {
            var me = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (user.Id == me)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "O'zingizning oyligingizni superadmin belgilaydi" });
            if (user.Role == Roles.SuperAdmin)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Superadmin oyligini faqat superadmin belgilaydi" });
            if (string.CompareOrdinal(from, TuitionService.CurrentMonth()) < 0)
                return BadRequest(new { message = "O'tgan oylar uchun oylikni faqat superadmin o'zgartiradi" });
        }

        var row = await db.StaffSalaries.FirstOrDefaultAsync(s => s.UserId == userId && s.EffectiveFrom == from, ct);
        var old = row?.Amount;
        if (row is null)
        {
            row = new StaffSalary { UserId = userId, EffectiveFrom = from };
            db.StaffSalaries.Add(row);
        }
        row.Amount = decimal.Round(p.Amount, 0);
        row.Note = string.IsNullOrWhiteSpace(p.Note) ? null : p.Note.Trim();
        row.CreatedBy = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        row.CreatedAt = AppClock.Iso();
        audit.Record("Staff", userId, "update",
            $"Xodim oyligi: {user.FullName} — {from} dan {AuditService.Money(row.Amount)} so'm"
            + (old is null ? "" : $" (avval {AuditService.Money(old.Value)})"));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Ikki marta bosilgan "Saqlash": (UserId, EffectiveFrom) unikal — ikkinchisi shu yerda, 500 emas.
            return Conflict(new { message = "Bu oy uchun oylik hozirgina saqlandi — sahifani yangilang" });
        }
        return Ok(new { ok = true });
    }

    // ============================== Qo'lda bonus / jarima (2026-11-10) ==============================

    /// <summary>Xodimning shu oydagi bonus va jarimalari (sababi bilan). <c>userId</c> bo'sh — oydagi hammasi (hisobot).</summary>
    [HttpGet("adjustments")]
    [AdminPerm("finance.main", ReadRequiresPerm = true)]
    public async Task<ActionResult<IEnumerable<StaffSalaryAdjustmentDto>>> Adjustments(
        [FromQuery] string? month, [FromQuery] string? userId, CancellationToken ct)
    {
        var m = PaymentFields.IsMonth(month) ? month! : TuitionService.CurrentMonth();
        var q = db.StaffSalaryAdjustments.AsNoTracking().Where(a => a.Month == m);
        if (!string.IsNullOrWhiteSpace(userId)) q = q.Where(a => a.UserId == userId);
        return await q.OrderBy(a => a.CreatedAt)
            .Select(a => new StaffSalaryAdjustmentDto(a.Id, a.UserId, a.Month, a.Kind, a.Amount, a.Reason, a.CreatedByName, a.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>Bonus yoki jarima qo'shish (sabab majburiy). Ruxsat — oylik bilan bir xil + o'tgan oyga ham (oy yopilishi).</summary>
    [HttpPost("staff/{userId}/adjustments")]
    [AdminPerm("finance.main")]
    public async Task<ActionResult<StaffSalaryAdjustmentDto>> AddAdjustment(string userId, StaffSalaryAdjustmentPayload p, CancellationToken ct)
    {
        var month = string.IsNullOrWhiteSpace(p.Month) ? TuitionService.CurrentMonth() : p.Month.Trim();
        if (PayrollRegister.ValidateAdjustment(p.Kind, p.Amount, p.Reason, month) is { } error) return BadRequest(new { message = error });
        var user = await StaffUserAsync(userId, ct);
        if (user is null) return NotFound(new { message = "Xodim topilmadi" });
        if (AdjustmentDenied(user.Value.Id, user.Value.Role, month) is { } denied) return denied;

        var row = new StaffSalaryAdjustment
        {
            UserId = userId, Month = month, Kind = p.Kind!, Amount = decimal.Round(p.Amount, 0), Reason = p.Reason!.Trim(),
            CreatedBy = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            CreatedByName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value,
        };
        db.StaffSalaryAdjustments.Add(row);
        audit.Record("Staff", userId, "create",
            $"Xodim oyligiga {KindLabel(row.Kind)}: {user.Value.FullName} — {month}, {AuditService.Money(row.Amount)} so'm ({row.Reason})");
        await db.SaveChangesAsync(ct);
        return new StaffSalaryAdjustmentDto(row.Id, row.UserId, row.Month, row.Kind, row.Amount, row.Reason, row.CreatedByName, row.CreatedAt);
    }

    [HttpDelete("adjustments/{id}")]
    [AdminPerm("finance.main")]
    public async Task<IActionResult> DeleteAdjustment(string id, CancellationToken ct)
    {
        var row = await db.StaffSalaryAdjustments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null) return NotFound(new { message = "Yozuv topilmadi" });
        var user = await StaffUserAsync(row.UserId, ct);
        if (user is not null && AdjustmentDenied(user.Value.Id, user.Value.Role, row.Month) is { } denied) return denied;
        db.StaffSalaryAdjustments.Remove(row);
        audit.Record("Staff", row.UserId, "delete",
            $"Xodim oyligidan {KindLabel(row.Kind)} o'chirildi: {user?.FullName ?? row.UserId} — {row.Month}, "
            + $"{AuditService.Money(row.Amount)} so'm ({row.Reason})");
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    private static string KindLabel(string kind) => kind == StaffSalaryAdjustmentKinds.Fine ? "jarima" : "bonus";

    private async Task<(string Id, string FullName, string Role)?> StaffUserAsync(string userId, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking()
            .Where(x => x.Id == userId && (x.Role == Roles.Staff || x.Role == Roles.Admin || x.Role == Roles.SuperAdmin))
            .Select(x => new { x.Id, x.FullName, x.Role }).FirstOrDefaultAsync(ct);
        return u is null ? null : (u.Id, u.FullName, u.Role);
    }

    /// <summary>
    /// Oylik qoidasi bilan bir xil (F3 revyu): superadmin bo'lmagan O'Z oyligiga va superadmin oyligiga bonus/jarima qo'ya
    /// olmaydi. Oy: joriy yoki O'TGAN oy (oy yopilishida jarima/bonus odatda keyingi oy boshida yoziladi); undan eski —
    /// faqat superadmin. Kelajak oy — rad.
    /// </summary>
    private ObjectResult? AdjustmentDenied(string targetId, string targetRole, string month)
    {
        var current = TuitionService.CurrentMonth();
        if (string.CompareOrdinal(month, current) > 0)
            return BadRequest(new { message = "Kelgusi oy uchun bonus/jarima kiritilmaydi" });
        if (User.IsInRole(Roles.SuperAdmin)) return null;
        var me = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (targetId == me)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "O'zingizning oyligingizga bonus/jarimani superadmin kiritadi" });
        if (targetRole == Roles.SuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Superadmin oyligini faqat superadmin o'zgartiradi" });
        if (string.CompareOrdinal(month, PreviousMonth(current)) < 0)
            return BadRequest(new { message = "O'tgan oydan eski oylar uchun bonus/jarimani faqat superadmin o'zgartiradi" });
        return null;
    }

    private static string PreviousMonth(string month)
    {
        var d = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).AddMonths(-1);
        return d.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <param name="Kind"><c>bonus</c> | <c>fine</c>.</param>
/// <param name="Month">Qaysi oy maoshiga ("yyyy-MM"); bo'sh — joriy oy.</param>
public record StaffSalaryAdjustmentPayload(string? Kind, decimal Amount, string? Reason, string? Month);

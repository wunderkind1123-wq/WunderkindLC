using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// «SINOVDAN QAYTGANLAR» (2026-11-13) — «Birinchi darsga yozilganlar» sahifasining o'quvchilar qismi: «Sinovga qaytarish»
/// bilan guruhdan chiqqan o'quvchilar (<see cref="StudentTrialReturn"/>). Ruxsat — o'sha sahifaniki (lidlar yoki Call Center),
/// O'QISH ham darvozalangan: javobda telefonlar bor. Bir martalik ESKI qaytarilganlarni o'tkazish — FAQAT superadmin (hard rol).
/// Qoidalar — `membership-periods.md` §12.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("leads.list", "leads.callcenter", ReadRequiresPerm = true)]
[Route("api/admin/trial-returns")]
public class TrialReturnsController(AppDbContext db, AuditService audit) : ControllerBase
{
    private string Actor => User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Admin";
    private string? ActorId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    public sealed record TrialReturnDto(
        string Id, string StudentId, string StudentName, string Phone, string FatherPhone, string MotherPhone,
        string GroupId, string GroupName, string TeacherName, string CourseName,
        string Reason, string CreatedAt, string CreatedByName, decimal CanceledAmount);

    /// <summary>OCHIQ yozuvlar (o'quvchi arxivda emas va keyin hech qaysi guruhga qo'shilmagan), eng yangisi tepada.</summary>
    [HttpGet]
    public async Task<ActionResult<List<TrialReturnDto>>> List()
    {
        var rows = await db.StudentTrialReturns.AsNoTracking().Where(r => r.ResolvedAt == null).ToListAsync();
        if (rows.Count == 0) return new List<TrialReturnDto>();
        var sids = rows.Select(r => r.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => sids.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.Phone, s.FatherPhone, s.MotherPhone, s.IsArchived })
            .ToDictionaryAsync(s => s.Id);
        var members = (await db.StudentGroups.AsNoTracking().Where(m => sids.Contains(m.StudentId))
                .Select(m => new { m.StudentId, m.IsActive, m.RecordedAt, m.JoinedAt }).ToListAsync())
            .ToLookup(m => m.StudentId);
        var gids = rows.Select(r => r.GroupId).Distinct().ToList();
        var groups = await db.Classes.AsNoTracking().Where(g => gids.Contains(g.Id))
            .Select(g => new { g.Id, g.TeacherId, g.CourseId }).ToDictionaryAsync(g => g.Id);
        var teacherNames = await db.Teachers.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.FullName);
        var courseNames = await db.Subjects.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);

        return rows
            .Where(r => students.TryGetValue(r.StudentId, out var st)
                        && TrialReturnService.IsOpen(r, st.IsArchived,
                            members[r.StudentId].Select(m => (m.IsActive, (string?)m.RecordedAt, m.JoinedAt))))
            .OrderByDescending(r => r.CreatedAt, StringComparer.Ordinal)
            .Select(r =>
            {
                var st = students[r.StudentId];
                var g = groups.GetValueOrDefault(r.GroupId);
                return new TrialReturnDto(r.Id, r.StudentId, st.FullName, st.Phone ?? "", st.FatherPhone ?? "", st.MotherPhone ?? "",
                    r.GroupId, r.GroupName,
                    g is null ? "" : teacherNames.GetValueOrDefault(g.TeacherId ?? "", ""),
                    g is null ? "" : courseNames.GetValueOrDefault(g.CourseId ?? "", ""),
                    r.Reason, r.CreatedAt, r.CreatedByName, r.CanceledAmount);
            })
            .ToList();
    }

    /// <summary>Ro'yxatdan qo'lda olib tashlash (masalan o'quvchi umuman qaytmaydi). Yozuv tarixda qoladi.</summary>
    [HttpPut("{id}/resolve")]
    public async Task<IActionResult> Resolve(string id)
    {
        var r = await db.StudentTrialReturns.FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound(new { message = "Yozuv topilmadi" });
        if (await TrialReturnService.ResolveAsync(db, id, $"Ro'yxatdan olib tashlandi ({Actor})") == 0) return NoContent();
        audit.Record("Membership", $"{r.GroupId}:{r.StudentId}", "update",
            $"«Sinovdan qaytganlar» ro'yxatidan olib tashlandi (guruh: {r.GroupName})", studentId: r.StudentId);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ============================ ESKI qaytarilganlar — bir martalik o'tkazish (FAQAT superadmin) ============================

    public sealed record LegacyDto(
        string Key, string StudentId, string StudentName, string GroupId, string GroupName,
        string ReturnedAt, string ReturnedBy, string Reason);

    /// <summary>
    /// 2026-11-13 gacha «sinovga qaytarilgan» va hamon guruhida SINOVDA turgan a'zoliklar: a'zolik tirik, holati «trial» va
    /// shu (guruh, o'quvchi) bo'yicha ENG OXIRGI a'zolik yozuvi — «Sinovga qaytarildi». Hech narsa yozmaydi.
    /// </summary>
    [HttpGet("legacy")]
    public async Task<ActionResult<List<LegacyDto>>> Legacy()
    {
        if (!User.IsInRole(Roles.SuperAdmin)) return StatusCode(403, new { message = "Faqat superadmin" });
        return await LegacyCandidatesAsync();
    }

    public sealed record LegacyApplyRequest(List<string> Keys);

    /// <summary>
    /// Belgilangan eski qaytarilganlarni yangi tartibga o'tkazadi: a'zolik YOPILADI (guruhdan chiqadi, ketish emas) va
    /// «Sinovdan qaytganlar»ga yozuv (asl sana, sabab, kim — tarixdan). ⚠️ PULGA TEGILMAYDI: sinovdagi a'zolikka hisob yozilmaydi.
    /// Ro'yxat serverda QAYTA quriladi (klientga ishonilmaydi).
    /// </summary>
    [HttpPost("legacy")]
    public async Task<ActionResult<object>> ApplyLegacy(LegacyApplyRequest req)
    {
        if (!User.IsInRole(Roles.SuperAdmin)) return StatusCode(403, new { message = "Faqat superadmin" });
        var keys = (req.Keys ?? []).ToHashSet(StringComparer.Ordinal);
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var moved = 0;
        foreach (var c in (await LegacyCandidatesAsync()).Where(c => keys.Contains(c.Key)))
        {
            var sg = await db.StudentGroups.FirstOrDefaultAsync(x =>
                x.GroupId == c.GroupId && x.StudentId == c.StudentId && x.IsActive && x.Status == "trial");
            var student = await db.Students.FindAsync(c.StudentId);
            if (sg is null || student is null) continue;
            sg.IsActive = false;
            sg.LeftAt = today;
            sg.ReturnedToTrial = true;
            var grp = await db.Classes.FindAsync(c.GroupId);
            if (grp is not null && string.Equals(student.ClassName, grp.Name, StringComparison.Ordinal))
                student.ClassName = await (from m in db.StudentGroups
                                           join g in db.Classes on m.GroupId equals g.Id
                                           where m.StudentId == student.Id && m.IsActive && m.GroupId != grp.Id
                                           orderby m.JoinedAt
                                           select g.Name).FirstOrDefaultAsync() ?? "";
            db.StudentTrialReturns.Add(new StudentTrialReturn
            {
                StudentId = c.StudentId, GroupId = c.GroupId, GroupName = c.GroupName, Reason = c.Reason,
                CreatedAt = c.ReturnedAt, CreatedByName = c.ReturnedBy,
            });
            audit.Record("Membership", c.Key, "update",
                $"Sinovga qaytarilgan (eski, {c.ReturnedAt[..Math.Min(10, c.ReturnedAt.Length)]}) — guruhdan chiqdi, «Birinchi darsga yozilganlar»ga o'tdi (guruh: {c.GroupName}); pulga tegilmadi",
                studentId: c.StudentId);
            await db.SaveChangesAsync();
            moved++;
        }
        return new { moved };
    }

    private async Task<List<LegacyDto>> LegacyCandidatesAsync()
    {
        const string prefix = "Sinovga qaytarildi (guruh:";
        var returns = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "Membership" && a.Summary.StartsWith(prefix))
            .Select(a => new { a.EntityId, a.Timestamp, a.ActorName, a.Summary }).ToListAsync();
        if (returns.Count == 0) return [];
        var entityIds = returns.Select(a => a.EntityId).Distinct().ToList();
        // Har (guruh, o'quvchi) bo'yicha ENG OXIRGI a'zolik yozuvi — undan keyin qayta qo'shilgan/aktivlashgan bo'lsa nomzod emas.
        var latest = (await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "Membership" && entityIds.Contains(a.EntityId))
                .Select(a => new { a.EntityId, a.Timestamp, a.Summary }).ToListAsync())
            .GroupBy(a => a.EntityId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Timestamp, StringComparer.Ordinal).First());

        var result = new List<LegacyDto>();
        foreach (var ret in returns.GroupBy(a => a.EntityId).Select(g => g.OrderByDescending(a => a.Timestamp, StringComparer.Ordinal).First()))
        {
            if (!latest.TryGetValue(ret.EntityId, out var last) || !last.Summary.StartsWith(prefix)) continue;
            var parts = ret.EntityId.Split(':');
            if (parts.Length != 2) continue;
            var (gid, sid) = (parts[0], parts[1]);
            var sg = await db.StudentGroups.AsNoTracking().FirstOrDefaultAsync(x =>
                x.GroupId == gid && x.StudentId == sid && x.IsActive && x.Status == "trial");
            if (sg is null) continue;
            var st = await db.Students.AsNoTracking().Where(s => s.Id == sid && !s.IsArchived)
                .Select(s => s.FullName).FirstOrDefaultAsync();
            if (st is null) continue;
            var gname = await db.Classes.AsNoTracking().Where(g => g.Id == gid).Select(g => g.Name).FirstOrDefaultAsync() ?? gid;
            var reasonAt = ret.Summary.IndexOf(" — sabab: ", StringComparison.Ordinal);
            result.Add(new LegacyDto(ret.EntityId, sid, st, gid, gname, ret.Timestamp, ret.ActorName ?? "",
                reasonAt >= 0 ? ret.Summary[(reasonAt + " — sabab: ".Length)..] : ""));
        }
        return result.OrderByDescending(r => r.ReturnedAt, StringComparer.Ordinal).ToList();
    }
}

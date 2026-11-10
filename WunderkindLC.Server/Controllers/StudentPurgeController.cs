using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Infrastructure.Auth;
using WunderkindLC.Infrastructure.Data;
using WunderkindLC.Domain;
using WunderkindLC.Application.Services;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// O'quvchini BUTUNLAY o'chirish (xato kiritilgan / dublikat) — profil «⋮» menyusidagi amal.
/// Qoidalar: <see cref="StudentPurge"/> va <c>.claude/rules/student-purge.md</c>.
///
/// <para><b>RUXSAT — «O'quvchilar ro'yxati va profili» → «O'chirish»</b> (<c>students.list:delete</c>):
/// admin/superadmin va Rollar'da shu ruxsat berilgan istalgan xodim. Arxiv tabidagi eski o'chirish
/// bilan AYNAN bir xil kalit — yangi kalit ochilmadi. GET (preview) ham shu kalit bilan
/// darvozalanadi: <c>[AdminPerm]</c> GET'ni har qanday xodimga ochiq qoldirardi.</para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students.list")]
[Route("api/admin/students")]
public class StudentPurgeController(AppDbContext db, AuditService audit, ILogger<StudentPurgeController> logger)
    : ControllerBase
{
    /// <summary>Admin/superadmin yoki <c>students.list:delete</c> (meros bilan <c>students</c>,
    /// <c>students:delete</c>, <c>students.list</c> ham) berilgan xodim.</summary>
    // 2026-11-10 (egasi): FAQAT superadmin va direktor (admin ROLI) — `students.list:delete` ruxsatli xodim ham EMAS
    // (ilgari — 2026-09-30 qarori — ruxsat berilgan istalgan xodim). Qoida — `LeadDeleteRights`.
    private bool MayPurge => LeadDeleteRights.CanDelete(User);

    private ObjectResult Forbidden() => StatusCode(403, new { message = LeadDeleteRights.Denied });

    /// <summary>Nimalar o'chishini oldindan ko'rsatadi (tasdiqlash oynasi uchun).</summary>
    [HttpGet("{id}/purge-preview")]
    public async Task<IActionResult> Preview(string id, CancellationToken ct)
    {
        if (!MayPurge) return Forbidden();
        var preview = await StudentPurge.PreviewAsync(db, id, ct);
        return preview is null ? NotFound(new { message = "O'quvchi topilmadi" }) : Ok(preview);
    }

    /// <summary>
    /// O'quvchini va unga tegishli HAMMA narsani o'chiradi. Tirik pul amali bo'lsa — 400 (sabab bilan).
    /// Tarixda faqat BITTA qator qoladi: kim, qachon, kimni o'chirgani.
    /// </summary>
    [HttpDelete("{id}/purge")]
    public async Task<IActionResult> Purge(string id, [FromServices] IWebHostEnvironment env, CancellationToken ct)
    {
        if (!MayPurge) return Forbidden();
        // Faqat ARXIVDAGI o'quvchi (avval arxivlash) — egasi, 2026-11-10.
        var archived = await db.Students.AsNoTracking().Where(s => s.Id == id).Select(s => (bool?)s.IsArchived).FirstOrDefaultAsync(ct);
        if (archived is null) return NotFound(new { message = "O'quvchi topilmadi" });
        if (archived == false) return BadRequest(new { message = LeadDeleteRights.StudentNotArchived });

        StudentPurge.Result? result;
        try
        {
            result = await StudentPurge.PurgeAsync(db, id, ct,
                actorName: User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        if (result is null) return NotFound(new { message = "O'quvchi topilmadi" });

        // Tarixdagi YAGONA iz: o'quvchining o'z yozuvlari yuqorida o'chirildi, bu qator esa
        // "kim o'chirdi" savoliga javob. studentId BERILMAYDI — endi mavjud bo'lmagan o'quvchiga bog'lanmasin.
        audit.Record(AuditService.EntityStudent, id, "delete",
            $"O'quvchi BUTUNLAY o'chirildi (xato kiritilgan / dublikat): {result.FullName}");
        await db.SaveChangesAsync(ct);

        // Fayllar — baza saqlangandan KEYIN (saqlash yiqilsa fayl yo'qolib qolmasin).
        foreach (var url in result.FileUrls)
        {
            var path = StudentPurge.ResolveUploadPath(env.ContentRootPath, url);
            if (path is null || !System.IO.File.Exists(path)) continue;
            try { System.IO.File.Delete(path); }
            catch (Exception ex) { logger.LogWarning(ex, "O'chirilgan o'quvchi faylini o'chirib bo'lmadi: {File}", Path.GetFileName(path)); }
        }

        return NoContent();
    }
}

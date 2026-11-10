using System.Security.Claims;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// LID va O'QUVCHINI BUTUNLAY O'CHIRISH HUQUQI — yagona qoida (egasi, 2026-11-10: «o'chirish tugmasi superadminda ham,
/// direktorda ham bo'lsin, lekin undan oldin arxivlash; menejer, sotuv menejeri va qolgan hamma xodimlarda — faqat
/// arxivlash»). Tarix: 2026-11-04 — lid: superadmin + admin; 2026-11-10 ertalab — lid: faqat superadmin (bekor qilindi).
/// <para>⚠️ HARD ROL — ruxsat kaliti EMAS: `leads.list:delete` / `students.list:delete` berilgan xodim ham o'chira olmaydi.
/// Direktor = <b>admin</b> ROLI.</para>
/// <para>⚠️ FAQAT ARXIVLANGANDAN KEYIN (egasining tanlovi): faol lid/o'quvchini to'g'ridan-to'g'ri o'chirib bo'lmaydi — avval
/// «Arxivlash» (lid: yo'qotilgan deb belgilash, `LostAt`; o'quvchi: `IsArchived`). Tasodifiy o'chirishdan himoya.</para>
/// Qo'llanadigan joylar: <c>LeadsController.Delete</c>, <c>ArchiveController.Purge</c> (lid/o'quvchi surati),
/// <c>StudentsController.Delete</c> (arxiv tabi), <c>StudentPurgeController</c> (profil → «Butunlay o'chirish»).
/// </summary>
public static class LeadDeleteRights
{
    public const string Denied =
        "Butunlay o'chirish faqat superadmin va direktor uchun. Yozuvni «Arxivlash» qiling — u CRM'da (Arxivda) qoladi.";

    public const string LeadNotArchived = "Avval lidni «Arxivlash» qiling — butunlay o'chirish faqat arxivdagi liddan.";

    public const string StudentNotArchived = "Avval o'quvchini arxivlang — butunlay o'chirish faqat arxivdagi o'quvchidan.";

    public static bool CanDelete(ClaimsPrincipal user) =>
        user.IsInRole(Roles.SuperAdmin) || user.IsInRole(Roles.Admin);
}

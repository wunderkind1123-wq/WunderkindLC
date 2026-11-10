using System.Security.Claims;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// LIDNI BUTUNLAY O'CHIRISH HUQUQI (egasi, 2026-11-04): «lid CRM'dan hech qachon o'chib ketmasligi kerak — faqat
/// superadmin yoki direktor bu huquqqa ega bo'lsin». 2026-11-10 dan (egasi: «xodimlardan o'chirishni olib tashla, faqat
/// arxivlash qolsin») — <b>FAQAT superadmin</b>; direktor (admin) ham faqat arxivlaydi.
/// <para>⚠️ HARD ROL — ruxsat kaliti EMAS: `leads.list:delete` (yoki yalang `leads`) berilgan xodim ham o'chira olmaydi.
/// Xodim uchun odatiy yo'l — «Arxivlash» (yo'qotilgan deb belgilash, sabab bilan), lid bazada qoladi.</para>
/// Qo'llanadigan joylar: <c>LeadsController.Delete</c> va <c>ArchiveController.Purge</c> (lid surati va uning tarixi).
/// </summary>
public static class LeadDeleteRights
{
    public const string Denied =
        "Lidni o'chirish faqat superadmin uchun. Lidni «Arxivlash» qiling — u CRM'da (Arxivda) qoladi.";

    public static bool CanDelete(ClaimsPrincipal user) =>
        user.IsInRole(Roles.SuperAdmin);
}

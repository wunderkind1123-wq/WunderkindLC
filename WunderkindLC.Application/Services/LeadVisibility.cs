using System.Security.Claims;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// SHAXSIY KONTAKTLAR ko'rinishi (2026-11-25, Call Center «Import», amoCRM kabi). Egasi: «import qilingan kontaktlar
/// faqat shu operatorga ko'rinsin, boshqalarga yo'q — director, superadmin va manager ko'ra oladi xolos».
///
/// <para>Qoida SOF va bitta joyda: <see cref="ViewerIdFor"/> so'rov egasidan <c>AppDbContext.LeadViewerId</c> ni chiqaradi,
/// global filtr esa begona operatorning <see cref="Lead.IsPrivate"/> lidini HAMMA so'rovdan olib tashlaydi.</para>
///
/// <list type="bullet">
///   <item><b>Cheklovsiz</b> (<c>null</c>): anonim (ommaviy forma, landing, webhook — takror tekshiruvi hamma lidni ko'rishi
///     shart), superadmin, direktor (<c>admin</c> roli) va <see cref="PermissionKey"/> ANIQ berilgan xodim (menejer).</item>
///   <item><b>Cheklangan</b>: qolgan har qanday kirgan foydalanuvchi — o'z id'si (shaxsiy lidlardan faqat o'ziga
///     biriktirilganini ko'radi; oddiy lidlar avvalgidek hammaga).</item>
/// </list>
///
/// <para>⚠️ Kalit QATTIQ (<c>retentionBonus</c>/<c>expenseApproval</c> naqshi): bo'lim merosi YO'Q — yalang <c>leads</c>
/// ruxsatli eski operator ham hammasini ko'rib qolmasin. Faqat aynan shu kalit (yoki uning amal varianti).</para>
/// </summary>
public static class LeadVisibility
{
    /// <summary>«Barcha operatorlar kontaktlarini ko'rish» — menejerga beriladi (Rollar matritsasi).</summary>
    public const string PermissionKey = "leadContactsAll";

    /// <summary>Ruxsat claim turi — Server `AdminPermAttribute.ClaimType` bilan AYNAN bir xil (Application Server'ga havola qilmaydi).</summary>
    public const string PermClaimType = "perm";

    /// <summary>Id'si yo'q (buzuq token) cheklangan foydalanuvchi — hech qaysi shaxsiy lidni ko'rmaydi.</summary>
    public const string NobodyId = "-";

    /// <summary>So'rov egasi uchun <c>LeadViewerId</c>: <c>null</c> — cheklov yo'q.</summary>
    public static string? ViewerIdFor(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return null;
        if (SeesAll(user)) return null;
        // `LeadsController.UserId()` bilan bir xil manba (mas'ul shu id bilan yoziladi).
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        return string.IsNullOrWhiteSpace(id) ? NobodyId : id;
    }

    /// <summary>Hamma operatorning shaxsiy kontaktlarini ko'radimi (va boshqa operatorga IMPORT qila oladimi).</summary>
    public static bool SeesAll(ClaimsPrincipal user) =>
        user.IsInRole(Roles.SuperAdmin)
        || user.IsInRole(Roles.Admin)
        || user.Claims.Any(c => c.Type == PermClaimType
                                && (c.Value == PermissionKey
                                    || c.Value.StartsWith(PermissionKey + ":", StringComparison.Ordinal)));

    /// <summary>Lid shu ko'ruvchiga ko'rinadimi — global filtrning AYNAN o'zi (xotirada, testlar va tekshiruvlar uchun).</summary>
    public static bool Visible(Lead lead, string? viewerId) =>
        viewerId is null || !lead.IsPrivate || lead.AssigneeUserId == viewerId;
}

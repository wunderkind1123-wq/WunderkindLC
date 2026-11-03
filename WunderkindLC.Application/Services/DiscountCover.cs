using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «CHEGIRMANI USTOZ QOPLAYDI» (2026-11-03, egasining talabi; <c>StudentDiscount.TeacherCovers</c>).
///
/// <para>Standartda chegirmani MARKAZ ko'taradi: ustoz maoshi to'liq tarifdan (<c>MonthlyCharge.Amount</c>) hisoblanadi —
/// bu qoida O'ZGARMAGAN. Belgi qo'yilgan chegirmada ustoz maoshi baribir to'liq tarifdan hisoblanadi, lekin undan
/// chegirmaning admin kiritgan foizi markaz hisobiga ushlab qolinadi:</para>
/// <code>ushlanma = chegirma × o'sha oydagi foiz</code>
/// Misol: chegirma 137 500, admin 60% kiritdi → 82 500 ushlanadi. Ustozning maosh foizi yoki qat'iy/foizli rejimi
/// bunga ta'sir QILMAYDI (egasining qarori — formula chalkashlik tug'dirardi).
///
/// <para>⚠️ <b>O'TGAN OYLAR O'ZGARMAYDI</b> — ikki qatlam bilan:
/// (1) foiz TARIXI (<c>StudentDiscount.TeacherCoverHistory</c>): foiz o'zgartirilsa yoki belgi olinsa yangi yozuv
/// JORIY oydan qo'shiladi, oldingi oylar eski foizda qoladi; (2) oyning chegirma qatori <see cref="ResolveForCover"/>
/// bilan topiladi — o'sha oyda AMALDA BO'LGAN qator, keyin bekor qilingan/almashtirilgan bo'lsa ham. Aks holda
/// dekabrda foiz o'zgarsa noyabr maoshi (allaqachon berilgan) jimgina qayta hisoblanardi.</para>
///
/// <para>⚠️ Faqat REGISTR chegirmasi (<c>StudentDiscount</c>) — «kelmagan darslar chegirmasi» (<c>LessonDiscount</c>)
/// markaz hisobida qoladi. Kassaga tegmaydi (<c>FinanceTransaction</c> emas) — jarimalar kabi faqat maosh hisobidan
/// ayriladi.</para>
/// Testlar: <c>DiscountCoverTests</c>.
/// </summary>
public static class DiscountCover
{
    /// <summary>Oynadagi standart qiymat (admin o'zgartiradi).</summary>
    public const int DefaultPct = 60;

    /// <summary>Qaysi oydan qoplaydi — chegirma boshlanishi va belgi qo'yilgan oyning KECHROG'I.</summary>
    public static string CoverFrom(string? startMonth, string? currentMonth)
    {
        var a = PaymentFields.IsMonth(startMonth) ? startMonth! : "";
        var b = PaymentFields.IsMonth(currentMonth) ? currentMonth! : "";
        return string.CompareOrdinal(a, b) >= 0 ? a : b;
    }

    public static decimal Withhold(decimal discount, decimal ratePct) =>
        discount <= 0 || ratePct <= 0 ? 0m : decimal.Round(discount * Math.Min(ratePct, 100m) / 100m, 2);

    /// <summary>Oy shu chegirma uchun «ustoz qoplaydi» oralig'idami (tarixsiz qatorlar uchun).</summary>
    public static bool Covers(bool teacherCovers, string? coversFrom, string month) =>
        teacherCovers && !string.IsNullOrEmpty(coversFrom) && string.CompareOrdinal(month, coversFrom) >= 0;

    /* ══════════════ FOIZ TARIXI ══════════════ */

    /// <summary>"yyyy-MM:pct;…" → oy bo'yicha o'sib boruvchi ro'yxat (buzuq yozuvlar tashlanadi).</summary>
    public static List<(string Month, int Pct)> Parse(string? history)
    {
        var list = new List<(string, int)>();
        if (string.IsNullOrWhiteSpace(history)) return list;
        foreach (var part in history.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split(':');
            if (kv.Length == 2 && PaymentFields.IsMonth(kv[0]) && int.TryParse(kv[1], out var p))
                list.Add((kv[0], Math.Clamp(p, 0, 100)));
        }
        return list.OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
    }

    private static string Format(IEnumerable<(string Month, int Pct)> items) =>
        string.Join(';', items.Select(x => $"{x.Month}:{x.Pct}"));

    /// <summary>Shu oyda amalda bo'lgan foiz (0 — qoplamaydi): oydan oldingi/teng ENG SO'NGGI yozuv.</summary>
    public static int PctFor(string? history, string month)
    {
        var pct = 0;
        foreach (var (m, p) in Parse(history))
        {
            if (string.CompareOrdinal(m, month) > 0) break;
            pct = p;
        }
        return pct;
    }

    /// <summary>
    /// Tarixga o'zgarish: <paramref name="fromMonth"/> dan boshlab foiz <paramref name="pct"/> (0 — o'chirildi).
    /// <paramref name="fromMonth"/> va undan keyingi yozuvlar almashtiriladi, OLDINGILARI tegilmaydi; natija oldingi
    /// holat bilan bir xil bo'lsa ortiqcha yozuv qo'shilmaydi.
    /// </summary>
    public static string WithChange(string? history, string fromMonth, int pct)
    {
        pct = Math.Clamp(pct, 0, 100);
        var kept = Parse(history).Where(x => string.CompareOrdinal(x.Month, fromMonth) < 0).ToList();
        var before = kept.Count == 0 ? 0 : kept[^1].Pct;
        if (before != pct) kept.Add((fromMonth, pct));
        return Format(kept);
    }

    /// <summary>Qatorning shu oydagi foizi: tarixdan; tarixsiz (eski) qatorda — joriy belgi va «qaysi oydan».</summary>
    public static int EffectivePct(StudentDiscount d, string month) =>
        !string.IsNullOrWhiteSpace(d.TeacherCoverHistory)
            ? PctFor(d.TeacherCoverHistory, month)
            : Covers(d.TeacherCovers, d.TeacherCoversFrom, month) ? Math.Clamp(d.TeacherCoverPct, 0, 100) : 0;

    /* ══════════════ QAYSI QATOR ══════════════ */

    /// <summary>
    /// Qator shu OYDA amalda BO'LGANMI — holatiga qaramay: davr ichida va (hali amalda yoki o'sha oyda/keyin yopilgan).
    /// <see cref="DiscountRules.InForce"/> dan farqi — bekor qilingan/almashtirilgan qator o'z oylari uchun qoladi.
    /// </summary>
    public static bool WasInForce(StudentDiscount d, string month)
    {
        if (!TuitionService.DiscountActiveForMonth(d.StartMonth, d.EndMonth, month)) return false;
        if (d.Status == StudentDiscount.StatusActive) return true;
        var ended = (d.EndedAt ?? "").Length >= 7 ? d.EndedAt![..7] : "";
        return ended.Length == 7 && string.CompareOrdinal(ended, month) >= 0;
    }

    /// <summary>
    /// Oy hisobining chegirmasini bergan qator — <see cref="DiscountRules.Resolve"/> ustunligi (AYNAN shu fan → «barcha
    /// guruhlar»), lekin o'sha oyda amalda BO'LGAN qatorlar orasida; bir qamrovda bir nechtasi bo'lsa — hozir amaldagisi,
    /// keyin eng yangisi.
    /// </summary>
    public static StudentDiscount? ResolveForCover(IReadOnlyList<StudentDiscount> rows, string month, string? groupId)
    {
        StudentDiscount? exact = null, all = null;
        foreach (var d in rows)
        {
            if (!WasInForce(d, month)) continue;
            if (d.GroupId is null) { if (all is null || Better(d, all)) all = d; }
            else if (groupId is not null && string.Equals(d.GroupId, groupId, StringComparison.Ordinal))
            {
                if (exact is null || Better(d, exact)) exact = d;
            }
        }
        return exact ?? all;
    }

    private static bool Better(StudentDiscount a, StudentDiscount b)
    {
        var aa = a.Status == StudentDiscount.StatusActive;
        var ba = b.Status == StudentDiscount.StatusActive;
        if (aa != ba) return aa;
        return string.CompareOrdinal(a.CreatedAt ?? "", b.CreatedAt ?? "") > 0;
    }

    public sealed record Item(string StudentId, string StudentName, string GroupId, decimal Discount, int RatePct);

    /// <summary>
    /// (oy, guruh) → ustoz qoplaydigan chegirmalar: oylik hisobdagi REGISTR qismi (hisob chegirmasi − kelmagan darslar
    /// chegirmasi) × o'sha oydagi foiz. Bitta yengil <c>AnyAsync</c> bilan darvozalangan — belgi hech qachon qo'yilmagan
    /// bo'lsa so'rov ketmaydi.
    /// </summary>
    public static async Task<Dictionary<(string Month, string GroupId), List<Item>>> LoadAsync(
        IAppDbContext db, IReadOnlyCollection<string> groupIds, string fromMonth, string toMonth, CancellationToken ct = default)
    {
        var result = new Dictionary<(string, string), List<Item>>();
        if (groupIds.Count == 0
            || !await db.StudentDiscounts.AnyAsync(d => d.TeacherCovers || d.TeacherCoverHistory != "", ct)) return result;

        var gids = groupIds.ToList();
        var charges = await db.MonthlyCharges.AsNoTracking()
            .Where(c => c.GroupId != null && gids.Contains(c.GroupId) && c.Discount > 0
                        && c.Month.CompareTo(fromMonth) >= 0 && c.Month.CompareTo(toMonth) <= 0)
            .Select(c => new { c.StudentId, GroupId = c.GroupId!, c.Month, c.Discount })
            .ToListAsync(ct);
        if (charges.Count == 0) return result;

        var sids = charges.Select(c => c.StudentId).Distinct().ToList();
        // ⚠️ HAMMA holatdagi qatorlar (bekor qilingan/almashtirilgan ham) — o'tgan oy o'sha paytdagi qator bilan.
        var rowsByStudent = (await db.StudentDiscounts.AsNoTracking()
                .Where(d => sids.Contains(d.StudentId)).ToListAsync(ct))
            .GroupBy(d => d.StudentId).ToDictionary(g => g.Key, g => (IReadOnlyList<StudentDiscount>)g.ToList());
        var lesson = (await db.LessonDiscounts.AsNoTracking()
                .Where(l => l.CancelledAt == null && sids.Contains(l.StudentId) && gids.Contains(l.GroupId)
                            && l.Month.CompareTo(fromMonth) >= 0 && l.Month.CompareTo(toMonth) <= 0)
                .Select(l => new { l.StudentId, l.GroupId, l.Month, l.Amount }).ToListAsync(ct))
            .GroupBy(l => (l.StudentId, l.GroupId, l.Month)).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var names = await db.Students.AsNoTracking().Where(s => sids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);

        foreach (var c in charges)
        {
            if (!rowsByStudent.TryGetValue(c.StudentId, out var rows)) continue;
            var w = ResolveForCover(rows, c.Month, c.GroupId);
            if (w is null) continue;
            var pct = EffectivePct(w, c.Month);
            if (pct <= 0) continue;
            var reg = c.Discount - Math.Min(c.Discount, lesson.GetValueOrDefault((c.StudentId, c.GroupId, c.Month)));
            if (reg <= 0) continue;
            var key = (c.Month, c.GroupId);
            if (!result.TryGetValue(key, out var list)) result[key] = list = [];
            list.Add(new Item(c.StudentId, names.GetValueOrDefault(c.StudentId, "—"), c.GroupId, reg, pct));
        }
        return result;
    }
}

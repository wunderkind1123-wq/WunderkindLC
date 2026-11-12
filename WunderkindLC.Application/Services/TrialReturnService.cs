using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// «SINOVGA QAYTARISH» (2026-11-13, egasi: «sinovga qaytarilgan o'quvchi guruhda qolmasin va birinchi darsga o'tsin, puli
/// hisoblanmasin, tarixda qolsin»). Qoidalar — <c>.claude/rules/membership-periods.md</c> §12, testlar — <c>TrialReturnTests</c>.
/// <list type="bullet">
/// <item>A'zolik YOPILADI (<c>IsActive=false</c>, <c>LeftAt</c>=bugun, <c>Status="trial"</c>, <see cref="StudentGroup.ReturnedToTrial"/>)
/// — o'quvchi guruh ro'yxati, jurnal va o'rin sanog'idan chiqadi.</item>
/// <item>PUL: JORIY oy va keyingi oylar hisobi bekor qilinadi (balansga qaytadi; qo'lda tahrirlangan qatorlar tegilmaydi).
/// O'TGAN oylar TEGILMAYDI — u oylarda o'qigan, maosh/hisobotlar yopilgan. Faol davr o'tgan oy oxirigacha tarixga yoziladi
/// (<see cref="PeriodEnd"/>), joriy oyda aktivlashgan bo'lsa — umuman yozilmaydi.</item>
/// <item>KETISH EMAS: «Ketish sabablari»ga yozilmaydi va <c>DashboardSummary.LeftEvents</c> uni sanamaydi.</item>
/// <item><see cref="StudentTrialReturn"/> yoziladi — «Birinchi darsga yozilganlar» → «Sinovdan qaytganlar».</item>
/// </list>
/// SaveChanges — chaqiruvchida (o'quvchi qulfi ostida, balans qayta o'qilgan holda).
/// </summary>
public static class TrialReturnService
{
    public sealed record Result(decimal Canceled, IReadOnlyList<string> Months, StudentTrialReturn Record);

    /// <summary>
    /// Yopilayotgan faol davrning TUGASH sanasi (tarixga, <c>PastPeriods</c>) yoki <c>null</c> — davr yozilmaydi.
    /// Davr O'TGAN oy oxirida yopiladi (joriy oy hisoblanmaydi); joriy oyda (yoki undan keyin) aktivlashgan bo'lsa —
    /// null. Muzlatilgan a'zolikda tugash = muzlatish sanasi (o'tgan oy oxiridan kech bo'lsa — o'tgan oy oxiri).
    /// </summary>
    public static string? PeriodEnd(string? activatedAt, string? frozenAt, string today)
    {
        if (!DateOnly.TryParse(activatedAt, out var act) || !DateOnly.TryParse(today, out var t)) return null;
        var monthStart = new DateOnly(t.Year, t.Month, 1);
        if (act >= monthStart) return null;
        var prevEnd = monthStart.AddDays(-1);
        var end = DateOnly.TryParse(frozenAt, out var fr) && fr < prevEnd ? fr : prevEnd;
        if (end < act) end = act;
        return end.ToString("yyyy-MM-dd");
    }

    /// <summary>Sinovga qaytarish — a'zolikni yopadi, joriy oydan hisobni bekor qiladi va yozuv qo'shadi. SaveChanges YO'Q.</summary>
    public static async Task<Result> ReturnAsync(
        IAppDbContext db, AuditService audit, Student s, Group g, StudentGroup sg,
        string? reasonId, string reason, string? actorId, string actorName)
    {
        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var periodEnd = PeriodEnd(sg.ActivatedAt, sg.FrozenAt, today);
        // `ClosePeriod` muzlatish sanasini birinchi oladi — tugash sanasini o'zimiz (o'tgan oy oxiriga qirqib) beramiz.
        sg.FrozenAt = string.Empty;
        if (periodEnd is not null) MembershipLifecycle.ClosePeriod(sg, periodEnd);

        var (canceled, months) = await TuitionService.PurgeChargesAfterMonthAsync(db, s, g.Id, today[..7], inclusive: true);

        sg.Status = "trial";
        sg.ActivatedAt = string.Empty;
        sg.YearFreeze = false;
        sg.IsActive = false;
        sg.LeftAt = today;
        sg.ReturnedToTrial = true;

        var rec = new StudentTrialReturn
        {
            StudentId = s.Id, GroupId = g.Id, GroupName = g.Name,
            ReasonId = string.IsNullOrWhiteSpace(reasonId) ? null : reasonId, Reason = reason,
            CreatedAt = AppClock.Iso(), CreatedById = actorId, CreatedByName = actorName,
            CanceledAmount = canceled,
        };
        db.StudentTrialReturns.Add(rec);

        // ⚠️ Matn «Sinovga qaytarildi» bilan boshlanadi (eski yozuvlar bilan bir xil) — tarixda va qidiruvda shunday topiladi.
        audit.Record("Membership", $"{g.Id}:{s.Id}", "update",
            $"Sinovga qaytarildi — guruhdan chiqdi, «Birinchi darsga yozilganlar»ga o'tdi (guruh: {g.Name})"
                + (canceled > 0 ? $" — hisob bekor qilindi: {AuditService.Money(canceled)} so'm ({string.Join(", ", months)})" : "")
                + (reason.Length > 0 ? $" — sabab: {reason}" : ""),
            studentId: s.Id);
        return new Result(canceled, months, rec);
    }

    /// <summary>Ochiq yozuv (ro'yxatda turadi)mi — o'quvchi arxivda emas va SHU qaytarishdan keyin hech qaysi guruhga qo'shilmagan.</summary>
    public static bool IsOpen(StudentTrialReturn r, bool studentArchived, IEnumerable<(bool IsActive, string? RecordedAt, string? JoinedAt)> memberships)
    {
        if (r.ResolvedAt is not null || studentArchived) return false;
        var day = r.CreatedAt.Length >= 10 ? r.CreatedAt[..10] : r.CreatedAt;
        // Guruhga qo'shilganda `RecordedAt` = haqiqiy bugungi sana (qo'shish qaysi yo'l bilan bo'lmasin) — shundan keyin yopiladi.
        return !memberships.Any(m => m.IsActive
            && string.CompareOrdinal(string.IsNullOrEmpty(m.RecordedAt) ? m.JoinedAt ?? "" : m.RecordedAt, day) >= 0);
    }

    /// <summary>O'quvchining ochiq yozuvlarini qo'lda yopadi (ro'yxatdan olib tashlash). SaveChanges YO'Q.</summary>
    public static async Task<int> ResolveAsync(IAppDbContext db, string id, string note)
    {
        var r = await db.StudentTrialReturns.FirstOrDefaultAsync(x => x.Id == id && x.ResolvedAt == null);
        if (r is null) return 0;
        r.ResolvedAt = AppClock.Iso();
        r.ResolvedNote = note;
        return 1;
    }
}

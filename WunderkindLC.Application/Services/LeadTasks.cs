using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// LID (SOTUV) TOPSHIRIQLARI — sof qoidalar va lid tomonidagi yon ta'sirlar (`docs/sales/PLAN.md` 1-qism,
/// `.claude/rules/crm-leads.md` «LID TOPSHIRIQLARI»).
///
/// <para>Topshiriqning O'ZI — oddiy <see cref="HrTask"/> («Topshiriqlar» bo'limi, egasi 2026-11-10: «alohida
/// Vazifalar bo'lmasin»), lidga <see cref="HrLeadTask"/> qatori bilan bog'lanadi. Yaratish/bajarish/bekor qilish —
/// <c>HrTasksService.Leads.cs</c> (Telegram xabari o'sha yerda). Bu yerda:</para>
/// <list type="bullet">
///   <item>spetsifikatsiya §15 intizom qoidalari: <b>natija majburiy</b>, <b>faqat OCHIQ lidga</b>;</item>
///   <item>lid yopilganda (yo'qotildi / o'quvchiga aylantirildi / o'chirildi) ochiq topshiriqlarni <b>avto-bekor</b>
///     (<see cref="CancelOpenForLeadAsync"/> — lid servislari <see cref="IAppDbContext"/> bilan chaqiradi);</item>
///   <item>kanban indikatori uchun lidlarning eng yaqin ochiq topshirig'i (<see cref="OpenByLeadAsync"/>).</item>
/// </list>
/// Lid tarixiga hodisa turlari: <see cref="EventCreated"/> · <see cref="EventDone"/> · <see cref="EventCancelled"/> —
/// voronka analitikasi va Telegram lid kartasi ularni O'QIMAYDI.
/// </summary>
public static class LeadTasks
{
    public const string StatusOpen = "open";
    public const string StatusDone = "done";
    public const string StatusCancelled = "cancelled";

    public const string EventCreated = "task";
    public const string EventDone = "task_done";
    public const string EventCancelled = "task_cancel";

    public const int MaxTextLength = 500;
    public const int MinResultLength = 3;
    public const int MaxResultLength = 1000;

    /// <summary>Turlar va yorliqlari (kalitlar — <see cref="HrLeadTaskTypes"/>).</summary>
    public static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>
    {
        [HrLeadTaskTypes.Call] = "Qo'ng'iroq",
        [HrLeadTaskTypes.Meeting] = "Uchrashuv",
        [HrLeadTaskTypes.Message] = "Xabar yozish",
        [HrLeadTaskTypes.Other] = "Boshqa",
    };

    public const string IsoFormat = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>Amalni bajargan odam.</summary>
    public readonly record struct Actor(string? UserId, string Name);

    /// <summary>Yangi topshiriq / tahrir ma'lumoti.</summary>
    public sealed record Draft(string? Type, string? Text, string? DueAt, bool AllDay, string? ResponsibleUserId);

    // ============================== Sof qoidalar ==============================

    /// <summary>Lid OCHIQmi (topshiriq qo'yish mumkinmi) — aylantirilmagan va yo'qotilmagan.</summary>
    public static bool LeadIsOpen(Lead lead) =>
        string.IsNullOrWhiteSpace(lead.ConvertedStudentId) && !LeadLoss.IsLost(lead);

    /// <summary>
    /// Muddat → mahalliy (sana, vaqt). "yyyy-MM-dd" yoki "yyyy-MM-ddTHH:mm[:ss]"; <paramref name="allDay"/> yoki
    /// faqat sana — kun oxiri 23:59. Buzuq — null.
    /// </summary>
    public static (DateOnly Day, TimeOnly Time)? ParseDue(string? raw, bool allDay)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return null;
        if (s.Length == 10)
            return DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? (d, EndOfDay) : null;
        string[] formats = ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];
        if (!DateTime.TryParseExact(s.Length > 19 ? s[..19] : s, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
            return null;
        return (DateOnly.FromDateTime(dt), allDay ? EndOfDay : new TimeOnly(dt.Hour, dt.Minute));
    }

    /// <summary>Butun kunlik topshiriq muddati — kun oxiri (shu vaqtgacha kechikmagan).</summary>
    public static readonly TimeOnly EndOfDay = new(23, 59);

    /// <summary>Yaratish qoidasi (sof). Xato matni yoki null.</summary>
    public static string? ValidateDraft(Lead lead, Draft d)
    {
        if (!LeadIsOpen(lead))
            return string.IsNullOrWhiteSpace(lead.ConvertedStudentId)
                ? "Lid arxivlangan (yo'qotilgan) — topshiriq qo'yish uchun avval uni tiklang."
                : "Lid o'quvchiga aylantirilgan — unga topshiriq qo'yilmaydi.";
        return ValidateFields(d);
    }

    /// <summary>Tur, matn va muddat (tahrirda ham).</summary>
    public static string? ValidateFields(Draft d)
    {
        if (!Types.ContainsKey((d.Type ?? "").Trim())) return "Topshiriq turini tanlang.";
        if ((d.Text ?? "").Trim().Length > MaxTextLength)
            return $"Topshiriq matni juda uzun — ko'pi bilan {MaxTextLength} belgi.";
        if (ParseDue(d.DueAt, d.AllDay) is null) return "Topshiriq muddatini (sana va vaqt) kiriting.";
        return null;
    }

    /// <summary>Natija qoidasi (sof): ≥ <see cref="MinResultLength"/> belgi — "nima bo'ldi"siz yopilmaydi.</summary>
    public static string? ValidateResult(string? result)
    {
        var r = (result ?? "").Trim();
        if (r.Length < MinResultLength) return "Natijani yozing — nima bo'ldi? (kamida 3 belgi)";
        if (r.Length > MaxResultLength) return $"Natija juda uzun — ko'pi bilan {MaxResultLength} belgi.";
        return null;
    }

    /// <summary>HR holati → lid sahifasidagi holat: open/returned → open; done → done; qolgani → cancelled.</summary>
    public static string StatusOf(string hrStatus) => hrStatus switch
    {
        HrTaskStatuses.Open or HrTaskStatuses.Returned => StatusOpen,
        HrTaskStatuses.Done => StatusDone,
        _ => StatusCancelled,
    };

    /// <summary>
    /// Lid sahifasidagi guruh: <c>overdue</c> · <c>today</c> · <c>tomorrow</c> · <c>later</c> (mahalliy vaqtda).
    /// Buzuq sana — <c>later</c> (yolg'on qizil bo'lmasin).
    /// </summary>
    public static string Bucket(string dueLocalIso, DateTime localNow)
    {
        if (TryParse(dueLocalIso) is not { } due) return "later";
        if (due < localNow) return "overdue";
        if (due.Date == localNow.Date) return "today";
        if (due.Date == localNow.Date.AddDays(1)) return "tomorrow";
        return "later";
    }

    /// <summary>Kechikish kunlari (kalendar kunlari; bugun kechikkan = 0).</summary>
    public static int OverdueDays(string dueLocalIso, DateTime localNow) =>
        TryParse(dueLocalIso) is { } due
            ? Math.Max(0, DateOnly.FromDateTime(localNow).DayNumber - DateOnly.FromDateTime(due).DayNumber)
            : 0;

    public static DateTime? TryParse(string? iso) =>
        DateTime.TryParseExact((iso ?? "").Length >= 19 ? iso![..19] : iso ?? "", IsoFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;

    public static string TypeLabel(string type) => Types.TryGetValue(type, out var l) ? l : type;

    /// <summary>"12.11.2026 15:00" yoki butun kun bo'lsa "12.11.2026".</summary>
    public static string DueText(DateTime local, bool allDay) =>
        local.ToString(allDay ? "dd.MM.yyyy" : "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>HR topshirig'i sarlavhasi: «Qo'ng'iroq: Vali Valiyev — narxni aytish» (≤ 200 belgi).</summary>
    public static string Title(string type, string leadName, string text)
    {
        var t = $"{TypeLabel(type)}: {(string.IsNullOrWhiteSpace(leadName) ? "lid" : leadName.Trim())}"
                + (string.IsNullOrWhiteSpace(text) ? "" : " — " + text.Trim());
        return t.Length <= HrLimits.TitleMax ? t : t[..(HrLimits.TitleMax - 1)] + "…";
    }

    /// <summary>HR topshirig'i tavsifi: lidning aloqa ma'lumoti (Telegram xabarida ham shu ko'rinadi).</summary>
    public static string Description(Lead lead, string text)
    {
        var lines = new List<string> { $"Lid: {lead.FullName}" };
        if (!string.IsNullOrWhiteSpace(lead.Phone)) lines.Add($"Telefon: {lead.Phone}");
        if (!string.IsNullOrWhiteSpace(lead.FatherPhone)) lines.Add($"Otasi: {lead.FatherFullName} {lead.FatherPhone}".Replace("  ", " "));
        if (!string.IsNullOrWhiteSpace(lead.MotherPhone)) lines.Add($"Onasi: {lead.MotherFullName} {lead.MotherPhone}".Replace("  ", " "));
        if (!string.IsNullOrWhiteSpace(lead.InterestSubject)) lines.Add($"Kurs: {lead.InterestSubject}");
        if (!string.IsNullOrWhiteSpace(text)) lines.AddRange(["", text.Trim()]);
        lines.AddRange(["", "CRM: Lidlar → lid sahifasi"]);
        return string.Join("\n", lines);
    }

    // ============================== Lid tomoni (IAppDbContext) ==============================

    /// <summary>HR sozlamasidagi vaqt mintaqasi (yo'q bo'lsa — Toshkent).</summary>
    public static async Task<TimeZoneInfo> ZoneAsync(IAppDbContext db, CancellationToken ct = default) =>
        HrValues.Zone(await db.HrSettings.AsNoTracking().Select(s => s.Timezone).FirstOrDefaultAsync(ct));

    /// <summary>Kanban kartasi uchun: lidning eng yaqin muddatli ochiq topshirig'i + ochiqlar soni.</summary>
    public sealed record NextTask(string DueAt, string Type, string Text, bool AllDay, int OpenCount);

    /// <summary>Barcha lidlar bo'yicha ochiq topshiriqlar (bitta so'rov). Kalit — lid id.</summary>
    public static async Task<Dictionary<string, NextTask>> OpenByLeadAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var tz = await ZoneAsync(db, ct);
        var rows = await (from l in db.HrLeadTasks
                          join t in db.HrTasks on l.TaskId equals t.Id
                          where t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned
                          select new { l.LeadId, l.Type, l.Text, l.AllDay, t.DueAt })
            .AsNoTracking().ToListAsync(ct);
        return rows.GroupBy(r => r.LeadId).ToDictionary(g => g.Key, g =>
        {
            var first = g.OrderBy(r => r.DueAt).First();
            return new NextTask(HrValues.Local(first.DueAt, tz).ToString(IsoFormat, CultureInfo.InvariantCulture),
                first.Type, first.Text, first.AllDay, g.Count());
        });
    }

    /// <summary>
    /// Lidning BARCHA ochiq topshiriqlarini bekor qiladi (lid yo'qotildi / aylantirildi / o'chirildi). Bitta umumiy
    /// hodisa yoziladi (<paramref name="recordEvent"/> = false — lid O'CHIRILAYOTGANDA: uning hodisalari arxivga olinib
    /// o'chiriladi). SaveChanges QILMAYDI. Nechta bekor qilingani qaytadi.
    /// </summary>
    public static async Task<int> CancelOpenForLeadAsync(
        IAppDbContext db, string leadId, string reason, Actor actor, CancellationToken ct = default, bool recordEvent = true)
    {
        var open = await (from l in db.HrLeadTasks
                          join t in db.HrTasks on l.TaskId equals t.Id
                          where l.LeadId == leadId
                              && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned)
                          select t)
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var count = open.Count(t => t.Cancel(now, reason));
        if (count > 0 && recordEvent)
            AddEvent(db, leadId, EventCancelled, $"{count} ta ochiq topshiriq avtomatik bekor qilindi — {reason}", actor);
        return count;
    }

    /// <summary>Lid tarixiga hodisa (SaveChanges QILMAYDI).</summary>
    public static void AddEvent(IAppDbContext db, string leadId, string type, string text, Actor actor) =>
        db.LeadEvents.Add(new LeadEvent
        {
            LeadId = leadId, Type = type, Text = text, ActorName = actor.Name,
            ActorUserId = actor.UserId, CreatedAt = AppClock.Iso(),
        });
}

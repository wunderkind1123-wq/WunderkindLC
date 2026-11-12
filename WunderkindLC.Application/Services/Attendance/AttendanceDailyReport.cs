using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services.Messaging;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// KUNLIK DAVOMAT HISOBOTI (2026-11-13, egasi: «menejer/direktorga necha kishi kelmagani haqidagi xabar o'rniga har kuni
/// kechqurun qaysi ustozlar davomat qilgani/qilmagani chiroyli ro'yxat bo'lib kelsin, 19:30 da»). Sof qism — matn
/// (<see cref="BuildText"/>, <c>AttendanceDailyReportTests</c>).
/// <para>Darslar — davomat intizomi bilan BIR XIL ro'yxat (<see cref="AttendanceDisciplineService.LessonsAsync"/>: ko'chirilgan
/// dars yangi vaqtida, o'rinbosar o'tgan dars — o'rinbosarniki, kutilgan o'quvchisi bor guruhlar). Hisobot vaqtidan
/// KEYIN boshlanadigan dars sanalmaydi (davomat hali kutilmaydi) — faqat soni ko'rsatiladi.</para>
/// </summary>
public static class AttendanceDailyReport
{
    /// <summary>Yuborish vaqti (markaz vaqti).</summary>
    public const string ReportTime = "19:30";

    public const string NotificationTitle = "Kunlik davomat hisoboti";
    public const string NotificationType = "attendance_daily_report";

    /// <summary>Bitta dars: kim o'tdi, qaysi guruh, qachon boshlandi, davomat qachon olindi (null — olinmagan).</summary>
    public sealed record Row(string TeacherName, string GroupName, string StartTime, DateTime? TakenAt);

    public static string MarkKey(string date) => $"attreport:{date}";

    /// <summary>Telegram HTML matni. <paramref name="later"/> — hisobot vaqtidan keyin boshlanadigan darslar soni.</summary>
    public static string BuildText(string date, IReadOnlyCollection<Row> rows, int later)
    {
        var sb = new StringBuilder();
        sb.Append($"📋 <b>Davomat hisoboti — {AttendanceDiscipline.Dmy(date)}</b>\n");

        if (rows.Count == 0)
        {
            sb.Append("\nBugun hisobot vaqtigacha dars bo'lmadi.");
            if (later > 0) sb.Append($"\n⏳ {later} ta dars {ReportTime} dan keyin boshlanadi.");
            return sb.ToString();
        }

        var teachers = rows
            .GroupBy(r => r.TeacherName.Trim().Length == 0 ? "O'qituvchi biriktirilmagan" : r.TeacherName.Trim(),
                StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Lessons: g.OrderBy(r => ScheduleRules.ParseTime(r.StartTime) ?? 0).ToList()))
            .OrderBy(t => t.Name, StringComparer.CurrentCulture)
            .ToList();
        var missed = teachers.Where(t => t.Lessons.Any(l => l.TakenAt is null)).ToList();
        var done = teachers.Where(t => t.Lessons.All(l => l.TakenAt is not null)).ToList();

        if (missed.Count > 0)
        {
            sb.Append($"\n❌ <b>Davomat qilmaganlar ({missed.Count})</b>\n");
            foreach (var (name, lessons) in missed)
            {
                sb.Append($"• <b>{TelegramHtml.Escape(name)}</b>\n");
                foreach (var l in lessons)
                    sb.Append(l.TakenAt is null
                        ? $"    ✖️ {TelegramHtml.Escape(l.GroupName)} ({l.StartTime})\n"
                        : $"    ✔️ {TelegramHtml.Escape(l.GroupName)} ({l.StartTime}) — {l.TakenAt:HH:mm}\n");
            }
        }

        if (done.Count > 0)
        {
            sb.Append($"\n✅ <b>Davomat qilganlar ({done.Count})</b>\n");
            foreach (var (name, lessons) in done)
            {
                var groups = string.Join(", ", lessons.Select(l => $"{TelegramHtml.Escape(l.GroupName)} ({l.TakenAt:HH:mm})"));
                sb.Append($"• {TelegramHtml.Escape(name)} — {groups}\n");
            }
        }

        var taken = rows.Count(r => r.TakenAt is not null);
        sb.Append($"\n📊 Jami: {rows.Count} ta darsdan {taken} tasida davomat olindi");
        sb.Append(taken == rows.Count ? " ✅" : $", {rows.Count - taken} tasida olinmadi.");
        if (later > 0) sb.Append($"\n⏳ Yana {later} ta dars {ReportTime} dan keyin boshlanadi (hisobotga kirmadi).");
        return sb.ToString();
    }

    /// <summary>
    /// Oluvchilar: superadmin, admin va «Davomat qilinmagan guruhlar» (<c>classes.notAttended</c>) yoki «Davomat
    /// xabarlari nazorati» (<c>students.notices</c>) ruxsatli xodim — ya'ni avvalgi «tasdiq kutmoqda» ogohlantirishini
    /// olganlar ham.
    /// </summary>
    public static async Task<List<string>> RecipientsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var a = await AttendanceDisciplineService.RecipientsAsync(db, ct);
        var b = await AttendanceNotify.NoticeRecipientsAsync(db, ct);
        return a.Union(b, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Bitta yugurish: <paramref name="now"/> bugungi <see cref="ReportTime"/> dan keyin bo'lsa va bugun hali
    /// yuborilmagan bo'lsa — hisobot (qo'ng'iroqcha + Telegram). Qaytaradi: yuborildimi.
    /// </summary>
    public static async Task<bool> RunOnceAsync(
        IAppDbContext db, Func<long, string, CancellationToken, Task>? send, DateTime now, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(now);
        var date = today.ToString("yyyy-MM-dd");
        if (AttendanceDiscipline.StartOf(date, ReportTime) is not { } at || now < at) return false;
        if (await db.LessonReminderMarks.AsNoTracking().AnyAsync(m => m.Key == MarkKey(date), ct)) return false;

        var lessons = await AttendanceDisciplineService.LessonsAsync(db, today, ct);
        var reportMinute = ScheduleRules.ParseTime(ReportTime) ?? 0;
        var due = lessons.Where(l => (ScheduleRules.ParseTime(l.Lesson.StartTime) ?? 0) <= reportMinute).ToList();
        var later = lessons.Count - due.Count;

        var taken = await AttendanceTaking.FirstMarksAsync(db, date, due.Select(l => l.Lesson.Group.Id).ToList(), ct);
        var teacherIds = due.Select(l => LessonAttendanceReminderService.RecipientTeacherId(l.Lesson))
            .Where(id => id.Length > 0).Distinct().ToList();
        var names = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id))
            .Select(t => new { t.Id, t.FullName }).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        var rows = due.Select(l => new Row(
                names.GetValueOrDefault(LessonAttendanceReminderService.RecipientTeacherId(l.Lesson), ""),
                l.Lesson.Group.Name, l.Lesson.StartTime,
                taken.TryGetValue(l.Lesson.Group.Id, out var t) ? t : null))
            .ToList();

        // Belgi YUBORISHDAN OLDIN (atomik): restart yoki ikki nusxa takror yubormaydi.
        if (!await LessonAttendanceReminderService.TryMarkAsync(db, MarkKey(date), date, ct)) return false;
        if (lessons.Count == 0) return false;   // dars yo'q kun — xabar yo'q

        var html = BuildText(date, rows, later);
        var ids = await RecipientsAsync(db, ct);
        if (ids.Count == 0) return false;
        var plain = TelegramHtml.ToPlain(html);
        foreach (var id in ids) NotificationStore.Add(db, id, NotificationTitle, plain, NotificationType);
        await db.SaveChangesAsync(ct);
        if (send is null) return true;
        var chats = await db.TelegramRegistrations.AsNoTracking()
            .Where(r => r.UserId != null && ids.Contains(r.UserId) && r.BlockedAt == null)
            .Select(r => r.ChatId).Distinct().ToListAsync(ct);
        foreach (var chat in chats) await send(chat, html, ct);
        return true;
    }
}

/// <summary>Fon xizmati: har daqiqada <see cref="AttendanceDailyReport.RunOnceAsync"/>.</summary>
public sealed class AttendanceDailyReportService(
    IServiceProvider services,
    TelegramService telegram,
    ILogger<AttendanceDailyReportService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                Func<long, string, CancellationToken, Task>? send = telegram.IsConfigured
                    ? (chatId, text, ct) => telegram.SendMessageAsync(chatId, text, ct: ct, parseMode: "HTML")
                    : null;
                if (await AttendanceDailyReport.RunOnceAsync(db, send, AppClock.Now, stoppingToken))
                    logger.LogInformation("Kunlik davomat hisoboti yuborildi");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Kunlik davomat hisobotida xatolik");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}

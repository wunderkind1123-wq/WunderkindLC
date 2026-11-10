using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// DAVOMAT INTIZOMI (SPEC P3.4) — har daqiqada, faqat <see cref="CenterMeta.AttendanceDisciplineEnabled"/> yoqiq bo'lsa:
/// <list type="bullet">
///   <item>BUGUNGI darslar (<see cref="LessonAttendanceReminderService.LessonsTodayAsync"/>: ko'chirilgan dars yangi
///     vaqtida, o'rinbosar) — dars boshlangach belgilangan daqiqalarda o'qituvchiga eslatma (davomat hali olinmagan
///     bo'lsa), muddat o'tsa (o'z vaqtida olinmagan) — JARIMA qatori + o'qituvchiga xabar + administratorlarga xabar;</item>
///   <item>KECHAGI darslar — faqat to'xtab qolish paytida o'tkazib yuborilgan JARIMA uchun (xabarsiz).</item>
/// </list>
/// "Olindi" = guruhning shu kungi istalgan darsida BIRINCHI davomat belgisi (<see cref="AttendanceTaking"/>).
/// <para>Bir martalik: eslatma/eskalatsiya — <see cref="LessonReminderMark"/> kaliti (bazada, unikal), jarima —
/// <c>(GroupId, LessonDate)</c> unikal indeksi. Restart va ikki nusxa takror bermaydi.</para>
/// <para>Telegram — xodim/o'qituvchiga ishchi xabar (ota-onaga EMAS), shuning uchun to'g'ridan-to'g'ri
/// <see cref="TelegramService"/> (SPEC §0.4). Bildirishnoma (qo'ng'iroqcha) — har doim.</para>
/// </summary>
public sealed class AttendanceDisciplineService(
    IServiceProvider services,
    TelegramService telegram,
    ILogger<AttendanceDisciplineService> logger) : BackgroundService
{
    /// <summary>Bildirishnoma turi (kabinet qo'ng'iroqchasi).</summary>
    public const string NotificationType = "attendance_discipline";

    /// <summary>Bitta yugurish natijasi (testlar va log uchun).</summary>
    public sealed record RunResult(int Reminders, int Fines, int Escalations);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startupda migratsiya/seed tugashini kutish (boshqa fon xizmatlari kabi).
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
                Func<long, string, CancellationToken, Task>? send = telegram.IsConfigured
                    ? (chatId, text, ct) => telegram.SendMessageAsync(chatId, text, ct: ct)
                    : null;
                var r = await RunOnceAsync(db, audit, send, AppClock.Now, stoppingToken, logger);
                if (r.Reminders + r.Fines + r.Escalations > 0)
                    logger.LogInformation("Davomat intizomi: {R} eslatma, {F} jarima, {E} administratorga xabar",
                        r.Reminders, r.Fines, r.Escalations);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Davomat intizomi siklida xatolik");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>
    /// Bitta yugurish (sof bo'lmagan, lekin vaqt va Telegram tashqaridan beriladi — testlanadi).
    /// <paramref name="send"/> = null — Telegram sozlanmagan (faqat qo'ng'iroqcha).
    /// </summary>
    public static async Task<RunResult> RunOnceAsync(
        IAppDbContext db, AuditService audit, Func<long, string, CancellationToken, Task>? send, DateTime now,
        CancellationToken ct = default, ILogger? log = null)
    {
        var meta = await db.CenterMeta.AsNoTracking().FirstOrDefaultAsync(ct);
        if (meta is null || !meta.AttendanceDisciplineEnabled) return new RunResult(0, 0, 0);
        var settings = AttendanceSettingsRules.FromMeta(meta);
        // 2026-11-10: KUNLIK rejim — har dars uchun jarima YOZILMAYDI, o'rniga kuniga bitta (RunDailyAsync).
        var daily = settings.FineMode == AttendanceSettingsRules.ModeDaily;
        var deadlineHm = settings.DailyDeadline ?? "19:30";

        var today = DateOnly.FromDateTime(now);
        int reminders = 0, fines = 0, escalations = 0;

        // ---- BUGUN: eslatma, jarima, eskalatsiya ----
        var todayStr = today.ToString("yyyy-MM-dd");
        var todayLessons = await LessonsAsync(db, today, ct);
        if (todayLessons.Count > 0)
        {
            var taken = await AttendanceTaking.FirstMarksAsync(db, todayStr, todayLessons.Select(l => l.Lesson.Group.Id).ToList(), ct);
            var names = await NamesAsync(db, todayLessons, ct);
            foreach (var item in todayLessons)
            {
                var l = item.Lesson;
                var g = l.Group;
                var takenAt = taken.TryGetValue(g.Id, out var t) ? t : (DateTime?)null;
                var d = AttendanceDiscipline.Decide(todayStr, l.StartTime, now, takenAt, settings, isToday: true,
                    meta.AttendanceDisciplineSince, meta.AttendanceFineSince);
                if (d.Start is null) continue;
                var teacherId = LessonAttendanceReminderService.RecipientTeacherId(l);
                var courseName = names.Courses.GetValueOrDefault(g.CourseId ?? "", "");

                foreach (var m in d.DueReminders)
                {
                    if (teacherId.Length == 0) break;
                    if (!await LessonAttendanceReminderService.TryMarkAsync(
                            db, AttendanceDiscipline.ReminderKey(g.Id, todayStr, m), todayStr, ct)) continue;
                    var text = AttendanceDiscipline.ReminderText(g.Name, courseName, l.StartTime, d.Deadline!.Value,
                        settings.FineEnabled && !daily, settings.FineAmount);
                    await SendTeacherAsync(db, send, names, teacherId, "Davomat eslatmasi", text, ct);
                    reminders++;
                }

                TeacherAttendanceFine? fine = null;
                if (d.FineDue && !daily && teacherId.Length > 0)
                {
                    fine = await TryCreateFineAsync(db, audit, g, teacherId, todayStr, l.StartTime, d.Deadline!.Value,
                        settings.FineAmount, takenAt, now, names, ct, log);
                    if (fine is not null)
                    {
                        fines++;
                        if (await LessonReminderTryMark(db, AttendanceDiscipline.FineKey(g.Id, todayStr), todayStr, ct))
                            await SendTeacherAsync(db, send, names, teacherId, "Davomat jarimasi",
                                AttendanceDiscipline.FineText(todayStr, g.Name, d.Deadline!.Value, fine.Amount), ct);
                    }
                }

                if (d.EscalateDue
                    && await LessonReminderTryMark(db, AttendanceDiscipline.EscalationKey(g.Id, todayStr), todayStr, ct))
                {
                    var activeFine = fine ?? await db.TeacherAttendanceFines.AsNoTracking()
                        .FirstOrDefaultAsync(f => f.GroupId == g.Id && f.LessonDate == todayStr
                                                  && f.Status == AttendanceDiscipline.FineActive, ct);
                    var text = AttendanceDiscipline.EscalationText(g.Name,
                        names.Teachers.GetValueOrDefault(teacherId)?.FullName ?? "", l.StartTime, activeFine?.Amount);
                    await SendAdminsAsync(db, send, "Davomat olinmadi", text, ct);
                    escalations++;
                }

                // Ma'lumot uchun: jarimadan keyin davomat olingan bo'lsa — vaqti bir marta ko'chiriladi.
                if (takenAt is not null)
                {
                    var open = await db.TeacherAttendanceFines.FirstOrDefaultAsync(f =>
                        f.GroupId == g.Id && f.LessonDate == todayStr && f.AttendanceTakenAt == null, ct);
                    if (open is not null)
                    {
                        open.AttendanceTakenAt = takenAt;
                        await db.SaveChangesAsync(ct);
                    }
                }
            }
        }

        // ---- KECHA: faqat o'tkazib yuborilgan jarima (eslatma va xabarsiz) ----
        var yesterday = today.AddDays(-1);
        var yStr = yesterday.ToString("yyyy-MM-dd");
        var yLessons = await LessonsAsync(db, yesterday, ct);
        if (!daily && yLessons.Count > 0 && settings.FineEnabled && settings.FineAmount > 0)
        {
            var taken = await AttendanceTaking.FirstMarksAsync(db, yStr, yLessons.Select(l => l.Lesson.Group.Id).ToList(), ct);
            var names = await NamesAsync(db, yLessons, ct);
            foreach (var item in yLessons)
            {
                var l = item.Lesson;
                var takenAt = taken.TryGetValue(l.Group.Id, out var t) ? t : (DateTime?)null;
                var d = AttendanceDiscipline.Decide(yStr, l.StartTime, now, takenAt, settings, isToday: false,
                    meta.AttendanceDisciplineSince, meta.AttendanceFineSince);
                var teacherId = LessonAttendanceReminderService.RecipientTeacherId(l);
                if (!d.FineDue || teacherId.Length == 0) continue;
                if (await TryCreateFineAsync(db, audit, l.Group, teacherId, yStr, l.StartTime, d.Deadline!.Value,
                        settings.FineAmount, takenAt, now, names, ct, log) is not null)
                    fines++;
            }
        }

        // ---- KUNLIK JARIMA: bugun (muddat o'tgach, xabar bilan) va kecha (to'xtab qolishda o'tkazilgani, xabarsiz) ----
        if (daily && settings.FineEnabled && settings.FineAmount > 0)
        {
            fines += await RunDailyAsync(db, audit, send, today, todayLessons, deadlineHm, settings, meta, now, notify: true, ct, log);
            fines += await RunDailyAsync(db, audit, send, yesterday, yLessons, deadlineHm, settings, meta, now, notify: false, ct, log);
        }

        return new RunResult(reminders, fines, escalations);
    }

    private static Task<bool> LessonReminderTryMark(IAppDbContext db, string key, string date, CancellationToken ct) =>
        LessonAttendanceReminderService.TryMarkAsync(db, key, date, ct);

    /// <summary>
    /// KUNLIK JARIMA (2026-11-10, egasi): <paramref name="day"/> muddatidan (<paramref name="deadlineHm"/>) keyin — har
    /// o'qituvchi (darsni O'TGAN — o'rinbosar bo'lsa u) uchun shu kungi darslaridan birortasining davomati muddatgacha
    /// olinmagan bo'lsa BITTA jarima (unikal: o'qituvchi + kun). Muddat o'tib, kun TO'LIQ qayta ishlangach kun belgisi
    /// qo'yiladi — keyingi daqiqalarda bu kun uchun baza qayta so'ralmaydi (natija muddatdan keyin o'zgarmaydi).
    /// </summary>
    private static async Task<int> RunDailyAsync(
        IAppDbContext db, AuditService audit, Func<long, string, CancellationToken, Task>? send, DateOnly day,
        List<(AttendanceWatch.DayLesson Lesson, int Expected)> lessons, string deadlineHm, AttendanceSettingsDto settings,
        CenterMeta meta, DateTime now, bool notify, CancellationToken ct, ILogger? log)
    {
        var date = day.ToString("yyyy-MM-dd");
        if (AttendanceDiscipline.StartOf(date, deadlineHm) is not { } deadline || now < deadline) return 0;
        var dayKey = $"disc:dday:{date}:{deadlineHm}";
        if (await db.LessonReminderMarks.AsNoTracking().AnyAsync(m => m.Key == dayKey, ct)) return 0;

        var created = 0;
        var failed = false;
        if (lessons.Count > 0)
        {
            var taken = await AttendanceTaking.FirstMarksAsync(db, date, lessons.Select(l => l.Lesson.Group.Id).ToList(), ct);
            var names = await NamesAsync(db, lessons, ct);
            var byTeacher = lessons
                .Select(x => (Teacher: LessonAttendanceReminderService.RecipientTeacherId(x.Lesson), x.Lesson))
                .Where(x => x.Teacher.Length > 0)
                .GroupBy(x => x.Teacher, StringComparer.Ordinal);
            foreach (var g in byTeacher)
            {
                var missed = AttendanceDiscipline.DailyMissed(date, deadlineHm,
                    g.Select(x => new AttendanceDiscipline.DailyLesson(x.Lesson.Group.Id, x.Lesson.Group.Name, x.Lesson.StartTime,
                        taken.TryGetValue(x.Lesson.Group.Id, out var t) ? t : null)),
                    now, settings, meta.AttendanceDisciplineSince, meta.AttendanceFineSince);
                if (missed.Count == 0) continue;
                if (!names.Teachers.TryGetValue(g.Key, out var teacher)) continue;   // arxivlangan/topilmagan — jarima yo'q
                if (await db.TeacherAttendanceFines.AsNoTracking().AnyAsync(f =>
                        f.TeacherId == g.Key && f.LessonDate == date && f.Kind == AttendanceDiscipline.KindDaily, ct))
                    continue;

                var groupNames = missed.Select(m => m.GroupName).ToList();
                var fine = new TeacherAttendanceFine
                {
                    TeacherId = g.Key,
                    GroupId = "",
                    LessonDate = date,
                    Month = date[..7],
                    LessonStart = missed[0].StartTime,
                    Deadline = deadline,
                    Amount = settings.FineAmount,
                    Status = AttendanceDiscipline.FineActive,
                    Kind = AttendanceDiscipline.KindDaily,
                    MissedGroups = Trim(string.Join(", ", groupNames), 1000),
                    CreatedAt = now,
                };
                db.TeacherAttendanceFines.Add(fine);
                audit.Record(AttendanceFineAudit.EntityType, AttendanceFineAudit.EntityId("daily", fine.Id), "create",
                    $"Kunlik davomat jarimasi yozildi: {teacher.FullName}, {AttendanceDiscipline.Dmy(date)} — davomat {deadlineHm} gacha "
                    + $"olinmadi ({fine.MissedGroups}), {AuditService.Money(fine.Amount)} so'm",
                    teacherId: g.Key);
                try
                {
                    await db.SaveChangesAsync(ct);
                    created++;
                }
                catch (DbUpdateException ex)
                {
                    db.TeacherAttendanceFines.Entry(fine).State = EntityState.Detached;
                    var auditId = AttendanceFineAudit.EntityId("daily", fine.Id);
                    foreach (var a in db.AuditLogs.Local.Where(a => a.EntityId == auditId).ToList())
                        db.AuditLogs.Entry(a).State = EntityState.Detached;
                    var raced = await db.TeacherAttendanceFines.AsNoTracking().AnyAsync(f =>
                        f.TeacherId == g.Key && f.LessonDate == date && f.Kind == AttendanceDiscipline.KindDaily, ct);
                    if (!raced)
                    {
                        failed = true;   // kun belgisi qo'yilmaydi — keyingi daqiqada qayta urinadi
                        log?.LogWarning(ex, "Kunlik davomat jarimasi yozilmadi: o'qituvchi {Teacher}, sana {Date}", g.Key, date);
                    }
                    continue;
                }

                if (notify && await LessonReminderTryMark(db, AttendanceDiscipline.DailyFineKey(g.Key, date), date, ct))
                {
                    await SendTeacherAsync(db, send, names, g.Key, "Davomat jarimasi",
                        AttendanceDiscipline.DailyFineText(date, groupNames, deadlineHm, fine.Amount), ct);
                }
            }
        }
        if (!failed) await LessonReminderTryMark(db, dayKey, date, ct);
        return created;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    /// <summary>Kundagi darslar — boshlanish vaqti bor, kursi bor (jurnalsiz guruhda davomat olib bo'lmaydi) va
    /// o'sha kuni KUTILGAN (arxivlanmagan) o'quvchisi bor guruhlar; guruh bo'yicha eng erta dars.</summary>
    private static async Task<List<(AttendanceWatch.DayLesson Lesson, int Expected)>> LessonsAsync(
        IAppDbContext db, DateOnly day, CancellationToken ct)
    {
        var all = await LessonAttendanceReminderService.LessonsTodayAsync(db, day, ct);
        var lessons = all
            .Where(l => !string.IsNullOrEmpty(l.Group.CourseId) && ScheduleRules.ParseTime(l.StartTime) is not null)
            .GroupBy(l => l.Group.Id, StringComparer.Ordinal)
            .Select(g => g.OrderBy(l => ScheduleRules.ParseTime(l.StartTime)).First())
            .ToList();
        if (lessons.Count == 0) return [];

        var dayStr = day.ToString("yyyy-MM-dd");
        var ids = lessons.Select(l => l.Group.Id).ToList();
        var memberships = await db.StudentGroups.AsNoTracking().Where(sg => ids.Contains(sg.GroupId)).ToListAsync(ct);
        var studentIds = memberships.Select(m => m.StudentId).Distinct().ToList();
        var archived = (await db.Students.AsNoTracking()
                .Where(s => studentIds.Contains(s.Id) && s.IsArchived).Select(s => s.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var expected = memberships
            .Where(m => !archived.Contains(m.StudentId) && StudentJournalBuilder.ExpectedOn(m, dayStr))
            .GroupBy(m => m.GroupId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(m => m.StudentId).Distinct().Count(), StringComparer.Ordinal);

        return lessons
            .Select(l => (l, expected.GetValueOrDefault(l.Group.Id)))
            .Where(x => x.Item2 > 0)
            .ToList();
    }

    private sealed record TeacherInfo(string FullName, string? UserId);
    private sealed record Names(Dictionary<string, TeacherInfo> Teachers, Dictionary<string, string> Courses);

    private static async Task<Names> NamesAsync(
        IAppDbContext db, List<(AttendanceWatch.DayLesson Lesson, int Expected)> lessons, CancellationToken ct)
    {
        var teacherIds = lessons
            .SelectMany(x => new[] { x.Lesson.Group.TeacherId ?? "", x.Lesson.SubstituteTeacherId })
            .Where(id => id.Length > 0).Distinct().ToList();
        var courseIds = lessons.Select(x => x.Lesson.Group.CourseId ?? "").Where(id => id.Length > 0).Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id))
            .Select(t => new { t.Id, t.FullName, t.UserId, t.IsArchived }).ToListAsync(ct);
        var courses = await db.Subjects.AsNoTracking().Where(s => courseIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        return new Names(
            teachers.Where(t => !t.IsArchived).ToDictionary(t => t.Id, t => new TeacherInfo(t.FullName, t.UserId), StringComparer.Ordinal),
            courses.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Jarima qatori (guruh+kun bo'yicha UNIKAL) + audit, BITTA SaveChanges. Allaqachon bo'lsa yoki parallel nusxa
    /// ulgurgan bo'lsa — null. Arxivlangan/topilmagan o'qituvchiga jarima yozilmaydi.
    /// </summary>
    private static async Task<TeacherAttendanceFine?> TryCreateFineAsync(
        IAppDbContext db, AuditService audit, Group g, string teacherId, string date, string startTime, DateTime deadline,
        decimal amount, DateTime? takenAt, DateTime now, Names names, CancellationToken ct, ILogger? log = null)
    {
        if (!names.Teachers.TryGetValue(teacherId, out var teacher)) return null;
        if (await db.TeacherAttendanceFines.AsNoTracking().AnyAsync(f => f.GroupId == g.Id && f.LessonDate == date, ct))
            return null;

        var fine = new TeacherAttendanceFine
        {
            TeacherId = teacherId,
            GroupId = g.Id,
            LessonDate = date,
            Month = date[..7],
            LessonStart = startTime,
            Deadline = deadline,
            Amount = amount,
            Status = AttendanceDiscipline.FineActive,
            AttendanceTakenAt = takenAt,
            CreatedAt = now,
        };
        db.TeacherAttendanceFines.Add(fine);
        audit.Record(AttendanceFineAudit.EntityType, AttendanceFineAudit.EntityId(g.Id, fine.Id), "create",
            $"Davomat jarimasi yozildi: {teacher.FullName}, {g.Name}, {AttendanceDiscipline.Dmy(date)} — davomat "
            + $"{deadline:HH:mm} gacha olinmadi ({AuditService.Money(amount)} so'm)",
            teacherId: teacherId);
        try
        {
            await db.SaveChangesAsync(ct);
            return fine;
        }
        catch (DbUpdateException ex)
        {
            // Qatorlarimizni kontekstdan chiqaramiz (aks holda keyingi SaveChanges ham shu buzuq INSERT ni qayta urinardi).
            db.TeacherAttendanceFines.Entry(fine).State = EntityState.Detached;
            var auditId = AttendanceFineAudit.EntityId(g.Id, fine.Id);
            foreach (var a in db.AuditLogs.Local.Where(a => a.EntityId == auditId).ToList())
                db.AuditLogs.Entry(a).State = EntityState.Detached;
            // Parallel nusxa ulgurgan (unikal indeks) — JIM, bu kutilgan poyga. Aks holda (qator YO'Q) — haqiqiy xato:
            // jarima yozilmadi va buni logda ko'rish SHART (keyingi daqiqada qayta urinadi).
            var raced = await db.TeacherAttendanceFines.AsNoTracking().AnyAsync(f => f.GroupId == g.Id && f.LessonDate == date, ct);
            if (!raced)
                log?.LogWarning(ex, "Davomat jarimasi yozilmadi: guruh {Group}, sana {Date}", g.Id, date);
            return null;
        }
    }

    /// <summary>O'qituvchiga: qo'ng'iroqcha (akkaunti bo'lsa) + uning Telegram chatlari.</summary>
    private static async Task SendTeacherAsync(
        IAppDbContext db, Func<long, string, CancellationToken, Task>? send, Names names, string teacherId,
        string title, string body, CancellationToken ct)
    {
        if (!names.Teachers.TryGetValue(teacherId, out var teacher)) return;
        if (teacher.UserId is not null)
        {
            NotificationStore.Add(db, teacher.UserId, title, body, NotificationType);
            await db.SaveChangesAsync(ct);
        }
        if (send is null) return;
        var chats = await db.TelegramRegistrations.AsNoTracking()
            .Where(r => r.TeacherId == teacherId).Select(r => r.ChatId).Distinct().ToListAsync(ct);
        foreach (var chat in chats) await send(chat, body, ct);
    }

    /// <summary>
    /// Administratorlarga: superadmin, admin va «Davomat qilinmagan guruhlar» (<c>classes.notAttended</c>) ruxsatli
    /// xodim — qo'ng'iroqcha + Telegram (<see cref="TelegramRegistration.UserId"/>).
    /// </summary>
    private static async Task SendAdminsAsync(
        IAppDbContext db, Func<long, string, CancellationToken, Task>? send, string title, string body, CancellationToken ct)
    {
        var ids = await RecipientsAsync(db, ct);
        if (ids.Count == 0) return;
        foreach (var id in ids) NotificationStore.Add(db, id, title, body, NotificationType);
        await db.SaveChangesAsync(ct);
        if (send is null) return;
        var chats = await db.TelegramRegistrations.AsNoTracking()
            .Where(r => r.UserId != null && ids.Contains(r.UserId)).Select(r => r.ChatId).Distinct().ToListAsync(ct);
        foreach (var chat in chats) await send(chat, body, ct);
    }

    /// <summary>Eskalatsiya oluvchilari (akkaunt id'lari).</summary>
    public static async Task<List<string>> RecipientsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Role == Roles.SuperAdmin || u.Role == Roles.Admin || u.Role == Roles.Staff)
            .Select(u => new { u.Id, u.Role, u.Permissions })
            .ToListAsync(ct);
        return users
            .Where(u => u.Role != Roles.Staff || PermissionRules.HasSection(u.Permissions, "classes.notAttended"))
            .Select(u => u.Id)
            .ToList();
    }
}

/// <summary>Audit of attendance fines (`AuditSections`: «O'qituvchilar»).</summary>
public static class AttendanceFineAudit
{
    public const string EntityType = "TeacherAttendanceFine";

    /// <summary><c>"{groupId}:{fineId}"</c> — guruh sahifasining «Tarix» tabida ham ko'rinadi (Membership naqshi).</summary>
    public static string EntityId(string groupId, string fineId) => $"{groupId}:{fineId}";

    /// <summary>Kunlik jarimada guruh yo'q — <c>"daily:{fineId}"</c> (o'qituvchi tarixida `teacherId` bilan ko'rinadi).</summary>
    public static string EntityIdOf(TeacherAttendanceFine f) =>
        EntityId(f.Kind == AttendanceDiscipline.KindDaily ? AttendanceDiscipline.KindDaily : f.GroupId, f.Id);
}

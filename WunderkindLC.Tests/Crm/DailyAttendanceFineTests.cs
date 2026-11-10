using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Crm;

/// <summary>
/// KUNLIK DAVOMAT JARIMASI (egasi, 2026-11-10): «o'qituvchi har kuni 19:30 gacha davomat qilmasa — 50 000 so'm;
/// bir kunda bir nechta guruh davomati qilinmasa ham BITTA jarima; Jarimalar bo'limida ko'rinsin».
/// </summary>
public class DailyAttendanceFineTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static readonly AttendanceSettingsDto Daily =
        new(true, 15, [], false, true, 50_000m, 1, "late_only", FineMode: "daily", DailyDeadline: "19:30");

    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");
    private static DateTime At(int h, int m, int dayOffset = 0) => AppClock.Today.AddDays(dayOffset).ToDateTime(new TimeOnly(h, m));
    private static AttendanceDiscipline.DailyLesson L(string name, string start, DateTime? taken) => new(name, name, start, taken);

    // ============================== Sof qoida ==============================

    [Fact]
    public void Muddatgacha_olinmagan_darslar__muddatdan_oldin_va_olinganlar_yoq()
    {
        var lessons = new[]
        {
            L("A1", "14:00", null),               // umuman olinmagan
            L("B2", "15:30", At(15, 40)),         // o'z vaqtida
            L("C3", "16:00", At(20, 10)),         // muddatdan KEYIN — jarimadan qutqarmaydi
            L("D4", "20:00", null),               // muddatdan keyin boshlanadi — hisobga olinmaydi
        };
        Assert.Empty(AttendanceDiscipline.DailyMissed(Today, "19:30", lessons, At(19, 29), Daily));
        var missed = AttendanceDiscipline.DailyMissed(Today, "19:30", lessons, At(19, 31), Daily);
        Assert.Equal(["A1", "C3"], missed.Select(m => m.GroupName));

        Assert.Empty(AttendanceDiscipline.DailyMissed(Today, "19:30", lessons, At(19, 31), Daily with { FineEnabled = false }));
        Assert.Empty(AttendanceDiscipline.DailyMissed(Today, "19:30", lessons, At(19, 31), Daily with { FineAmount = 0 }));
        // Jarima muddatdan KEYIN yoqilgan — shu kun uchun jarima yo'q (retroaktiv emas).
        Assert.Empty(AttendanceDiscipline.DailyMissed(Today, "19:30", lessons, At(20, 0), Daily, fineSince: At(19, 45)));
    }

    [Fact]
    public void Sozlama__rejim_va_vaqt_tekshiriladi()
    {
        Assert.Null(AttendanceSettingsRules.Validate(Daily));
        Assert.NotNull(AttendanceSettingsRules.Validate(Daily with { FineMode = "weekly" }));
        Assert.NotNull(AttendanceSettingsRules.Validate(Daily with { DailyDeadline = "25:00" }));
        Assert.Equal("09:05", AttendanceSettingsRules.NormalizeTime("9:05"));
    }

    // ============================== Fon xizmati ==============================

    private sealed record Seed(Teacher Teacher, Group A, Group B);

    private static Seed SeedDb(TestDb db)
    {
        var c = db.Context;
        c.CenterMeta.Add(new CenterMeta
        {
            AttendanceDisciplineEnabled = true, AttendanceDeadlineMinutes = 15, AttendanceReminderMinutes = "",
            AttendanceEscalateEnabled = false, AttendanceFineEnabled = true, AttendanceFineAmount = 50_000m,
            AttendanceFineMode = "daily", AttendanceDailyDeadline = "19:30",
            // kecha — jarima hali yoqilmagan edi (testni bugungi kun bilan cheklaydi)
            AttendanceFineSince = AppClock.Today.ToDateTime(TimeOnly.MinValue),
        });
        var tu = new AppUser { FullName = "Ali Valiyev", Role = Roles.Teacher, Email = "t@x" };
        var teacher = new Teacher { FullName = "Ali Valiyev", UserId = tu.Id };
        var subject = new Subject { Name = "Ingliz tili" };
        var start = AppClock.Today.AddMonths(-2).ToString("yyyy-MM-dd");
        var a = new Group { Name = "A1", CourseId = subject.Id, TeacherId = teacher.Id, StartTime = "14:00", EndTime = "15:30", StartDate = start, Days = [0, 1, 2, 3, 4, 5, 6] };
        var b = new Group { Name = "B2", CourseId = subject.Id, TeacherId = teacher.Id, StartTime = "15:30", EndTime = "17:00", StartDate = start, Days = [0, 1, 2, 3, 4, 5, 6] };
        c.AddRange(tu, teacher, subject, a, b);
        var since = AppClock.Today.AddMonths(-1).ToString("yyyy-MM-dd");
        foreach (var g in new[] { a, b })
        {
            var st = new Student { FullName = "O'quvchi " + g.Name };
            c.Add(st);
            c.StudentGroups.Add(new StudentGroup
            {
                StudentId = st.Id, GroupId = g.Id, IsActive = true, Status = "active", JoinedAt = since, ActivatedAt = since, RecordedAt = since,
            });
        }
        c.TelegramRegistrations.Add(new TelegramRegistration { TeacherId = teacher.Id, ChatId = 111 });
        c.SaveChanges();
        return new Seed(teacher, a, b);
    }

    private static void Take(TestDb db, Group g, DateTime at)
    {
        db.Context.LessonNotes.Add(new LessonNote
        {
            ClassId = g.Id, SubjectId = g.CourseId!, Quarter = 1, Date = Today, Period = 1,
            Conducted = true, AttendanceTaken = true, AttendanceTakenAt = at,
        });
        db.Context.SaveChanges();
    }

    private static async Task<(AttendanceDisciplineService.RunResult Result, List<(long Chat, string Text)> Sent)> RunAsync(TestDb db, DateTime now)
    {
        var sent = new List<(long, string)>();
        var r = await AttendanceDisciplineService.RunOnceAsync(db.Context, new AuditService(db.Context, new NoHttpContext()),
            (chat, text, _) => { sent.Add((chat, text)); return Task.CompletedTask; }, now);
        return (r, sent);
    }

    [Fact]
    public async Task Ikki_guruhda_ham_olinmagan__kuniga_BITTA_50_000_jarima_va_bitta_xabar()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);

        var (before, _) = await RunAsync(db, At(19, 29));
        Assert.Equal(0, before.Fines);

        var (r, sent) = await RunAsync(db, At(19, 31));
        Assert.Equal(1, r.Fines);
        var fine = Assert.Single(db.Context.TeacherAttendanceFines.AsNoTracking());
        Assert.Equal(("daily", 50_000m, s.Teacher.Id, "A1, B2", Today), (fine.Kind, fine.Amount, fine.TeacherId, fine.MissedGroups, fine.LessonDate));
        Assert.Equal(Today[..7], fine.Month);
        var msg = Assert.Single(sent);
        Assert.Equal(111, msg.Chat);
        Assert.Contains("19:30 gacha olinmadi: A1, B2", msg.Text);

        // keyingi daqiqalar va qayta ishga tushish — takror yo'q
        var (again, sent2) = await RunAsync(db, At(19, 45));
        Assert.Equal(0, again.Fines);
        Assert.Empty(sent2);
        Assert.Single(db.Context.TeacherAttendanceFines.AsNoTracking());

        // «Jarimalar» sahifasi: guruh o'rnida olinmagan guruhlar ro'yxati
        var list = await AttendanceFineQueries.ListAsync(db.Context, Today[..7], null, null);
        var row = Assert.Single(list.Rows);
        Assert.Equal(("daily", "A1, B2", "Ali Valiyev"), (row.Kind, row.GroupName, row.TeacherName));
        Assert.Equal(50_000m, list.TotalActive);
    }

    [Fact]
    public async Task Bittasi_vaqtida_olingan__jarimada_faqat_olinmagani()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        Take(db, s.A, At(14, 10));

        var (r, _) = await RunAsync(db, At(19, 31));
        Assert.Equal(1, r.Fines);
        Assert.Equal("B2", db.Context.TeacherAttendanceFines.AsNoTracking().Single().MissedGroups);
    }

    [Fact]
    public async Task Hammasi_19_30_gacha_olingan__jarima_yoq()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        Take(db, s.A, At(14, 10));
        Take(db, s.B, At(19, 20));

        var (r, sent) = await RunAsync(db, At(19, 31));
        Assert.Equal(0, r.Fines);
        Assert.Empty(sent);
        Assert.Empty(db.Context.TeacherAttendanceFines.AsNoTracking());
    }

    [Fact]
    public async Task Kunlik_rejimda_har_dars_uchun_jarima_YOZILMAYDI()
    {
        using var db = TestDb.Sqlite();
        SeedDb(db);
        // 14:00 darsi + 15 daqiqa o'tdi — «har dars» rejimida jarima bo'lardi
        var (r, _) = await RunAsync(db, At(14, 20));
        Assert.Equal(0, r.Fines);
        Assert.Empty(db.Context.TeacherAttendanceFines.AsNoTracking());
    }

    [Fact]
    public async Task Kunlik_jarima_maoshdan_ushlanadi()
    {
        using var db = TestDb.Sqlite();
        var s = SeedDb(db);
        await RunAsync(db, At(19, 31));
        var teacher = await db.Context.Teachers.SingleAsync(t => t.Id == s.Teacher.Id);
        teacher.SalaryMode = "fixed";
        teacher.Salary = 3_000_000m;
        await db.Context.SaveChangesAsync();

        var ledger = await SalaryLedger.BuildAsync(db.Context, teacher, Today[..7] + "-01", Today[..7] + "-28");
        var m = ledger.Months.Single(x => x.Month == Today[..7]);
        Assert.Equal((50_000m, 1), (m.AttendanceFines, m.AttendanceFineCount));
    }
}

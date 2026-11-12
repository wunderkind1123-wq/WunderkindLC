using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.Crm;

/// <summary>
/// KUNLIK DAVOMAT HISOBOTI (egasi, 2026-11-13): «menejer/direktorga necha kishi kelmagani haqidagi xabar o'rniga har kuni
/// 19:30 da qaysi ustozlar davomat qilgani/qilmagani ro'yxat bo'lib kelsin».
/// </summary>
public class AttendanceDailyReportTests
{
    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");
    private static DateTime At(int h, int m) => AppClock.Today.ToDateTime(new TimeOnly(h, m));

    // ============================== Matn ==============================

    [Fact]
    public void Matn__qilmaganlar_tepada_qilganlar_pastda_jami_bilan()
    {
        var rows = new AttendanceDailyReport.Row[]
        {
            new("Ali Valiyev", "A1", "14:00", At(14, 10)),
            new("Ali Valiyev", "B2", "16:00", null),
            new("Nodira <Karimova>", "C3", "15:00", At(15, 5)),
        };
        var text = AttendanceDailyReport.BuildText(Today, rows, later: 1);

        Assert.Contains("❌ <b>Davomat qilmaganlar (1)</b>", text);
        Assert.Contains("✖️ B2 (16:00)", text);
        Assert.Contains("✔️ A1 (14:00) — 14:10", text);
        Assert.Contains("✅ <b>Davomat qilganlar (1)</b>", text);
        Assert.Contains("Nodira &lt;Karimova&gt; — C3 (15:05)", text);   // HTML escape
        Assert.Contains("Jami: 3 ta darsdan 2 tasida davomat olindi, 1 tasida olinmadi.", text);
        Assert.Contains("Yana 1 ta dars 19:30 dan keyin", text);
        Assert.True(text.IndexOf("qilmaganlar", StringComparison.Ordinal) < text.IndexOf("qilganlar (", StringComparison.Ordinal));
    }

    [Fact]
    public void Matn__hammasi_olingan_bolsa_qilmaganlar_bolimi_yoq()
    {
        var text = AttendanceDailyReport.BuildText(Today, [new("Ali", "A1", "14:00", At(14, 1))], later: 0);
        Assert.DoesNotContain("qilmaganlar", text);
        Assert.Contains("Jami: 1 ta darsdan 1 tasida davomat olindi ✅", text);
    }

    // ============================== Fon ishi ==============================

    private static (Group A, Group B, AppUser Admin) SeedDb(TestDb db)
    {
        var c = db.Context;
        c.CenterMeta.Add(new CenterMeta());
        var admin = new AppUser { FullName = "Direktor", Role = Roles.Admin, Email = "d@x" };
        var staffNo = new AppUser { FullName = "Kassir", Role = Roles.Staff, Email = "k@x", Permissions = ["finance"] };
        var t1 = new Teacher { FullName = "Ali Valiyev" };
        var t2 = new Teacher { FullName = "Nodira Karimova" };
        var subject = new Subject { Name = "Ingliz tili" };
        var start = AppClock.Today.AddMonths(-2).ToString("yyyy-MM-dd");
        var days = new List<int> { 0, 1, 2, 3, 4, 5, 6 };
        var a = new Group { Name = "A1", CourseId = subject.Id, TeacherId = t1.Id, StartTime = "14:00", EndTime = "15:30", StartDate = start, Days = days };
        var b = new Group { Name = "B2", CourseId = subject.Id, TeacherId = t2.Id, StartTime = "16:00", EndTime = "17:30", StartDate = start, Days = days };
        var late = new Group { Name = "Kech", CourseId = subject.Id, TeacherId = t2.Id, StartTime = "20:00", EndTime = "21:00", StartDate = start, Days = days };
        c.AddRange(admin, staffNo, t1, t2, subject, a, b, late);
        var since = AppClock.Today.AddMonths(-1).ToString("yyyy-MM-dd");
        foreach (var g in new[] { a, b, late })
        {
            var st = new Student { FullName = "O'quvchi " + g.Name };
            c.Add(st);
            c.StudentGroups.Add(new StudentGroup
            {
                StudentId = st.Id, GroupId = g.Id, IsActive = true, Status = "active", JoinedAt = since, ActivatedAt = since, RecordedAt = since,
            });
        }
        c.TelegramRegistrations.Add(new TelegramRegistration { UserId = admin.Id, ChatId = 900 });
        c.TelegramRegistrations.Add(new TelegramRegistration { UserId = staffNo.Id, ChatId = 901 });
        c.SaveChanges();
        return (a, b, admin);
    }

    [Fact]
    public async Task Soat_19_30_da_bir_marta__faqat_menejer_va_direktorga()
    {
        using var db = TestDb.Sqlite();
        var (a, _, admin) = SeedDb(db);
        db.Context.LessonNotes.Add(new LessonNote
        {
            ClassId = a.Id, SubjectId = a.CourseId!, Quarter = 1, Date = Today, Period = 1,
            Conducted = true, AttendanceTaken = true, AttendanceTakenAt = At(14, 12),
        });
        db.Context.SaveChanges();

        var sent = new List<(long Chat, string Text)>();
        Task Send(long chat, string text, CancellationToken _) { sent.Add((chat, text)); return Task.CompletedTask; }

        Assert.False(await AttendanceDailyReport.RunOnceAsync(db.Context, Send, At(19, 29)));
        Assert.Empty(sent);

        Assert.True(await AttendanceDailyReport.RunOnceAsync(db.Context, Send, At(19, 30)));
        var msg = Assert.Single(sent);   // ruxsatsiz kassirga (901) yo'q
        Assert.Equal(900, msg.Chat);
        Assert.Contains("Ali Valiyev — A1 (14:12)", msg.Text);
        Assert.Contains("<b>Nodira Karimova</b>", msg.Text);
        Assert.Contains("✖️ B2 (16:00)", msg.Text);
        Assert.Contains("Yana 1 ta dars 19:30 dan keyin", msg.Text);   // 20:00 dagi dars kirmaydi
        Assert.True(await db.Context.UserNotifications.AsNoTracking().AnyAsync(n => n.UserId == admin.Id
            && n.Type == AttendanceDailyReport.NotificationType));

        // takror yo'q (restart ham)
        Assert.False(await AttendanceDailyReport.RunOnceAsync(db.Context, Send, At(20, 15)));
        Assert.Single(sent);
    }
}

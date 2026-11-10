using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.HrTasks;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.HrTasks;

/// <summary>
/// LC (egasi, 2026-11-10): «sinov darsiga kelmaganlar faqat Dilfuza Rahimovaga topshiriq bo'lib yozilsin; servis uchun
/// faqat Munojatga yozilsin» — `HrSettings.TrialTaskEmployeeId` / `ServiceTaskEmployeeId` (`hr-tasks.md` §8, §14).
/// </summary>
public class HrServiceAndFixedTrialTests
{
    private static async Task SetAsync(HrTasksKit kit, string? trialTo = null, string? serviceTo = null)
    {
        await using var ctx = kit.NewContext();
        var s = await kit.Service(HrTasksKit.At(2031, 3, 10, 8, 0), ctx).GetOrCreateSettingsAsync();
        s.TrialTaskEmployeeId = trialTo;
        s.ServiceTaskEmployeeId = serviceTo;
        await ctx.SaveChangesAsync();
    }

    private static async Task<List<HrTask>> TasksOfAsync(HrTasksKit kit, string employeeId)
    {
        await using var db = kit.NewContext();
        return await db.HrTasks.AsNoTracking().Where(t => t.EmployeeId == employeeId).OrderBy(t => t.CreatedAt).ToListAsync();
    }

    // ============================== Sinov darsi — faqat tanlangan xodimga ==============================

    [Fact]
    public async Task Sinov_royxatlari_FAQAT_tanlangan_xodimga__lid_masuliga_emas()
    {
        using var kit = new HrTasksKit();
        var owner = await kit.PersonAsync(telegram: true, name: "Lid mas'uli");
        var dilfuza = await kit.PersonAsync(telegram: true, name: "Dilfuza Rahimova");
        await SetAsync(kit, trialTo: dilfuza.Employee!.Id);

        var teacher = new Teacher { FullName = "Aziz" };
        var course = new Subject { Name = "Ingliz tili" };
        var group = new Group { Name = "IELTS-1", TeacherId = teacher.Id, CourseId = course.Id };
        var lead = new Lead { FullName = "Ali Valiyev", Phone = "998901234567", AssigneeUserId = owner.User.Id, CreatedAt = "2031-03-01T10:00:00" };
        kit.Db.AddRange(teacher, course, group, lead,
            new TrialLesson { LeadId = lead.Id, GroupId = group.Id, ScheduledAt = "2031-03-11T15:00", Result = "pending", CreatedAt = "2031-03-05T10:00:00" },
            new TrialLesson { LeadId = lead.Id, GroupId = group.Id, ScheduledAt = "2031-03-09T15:00", Result = "no_show", CreatedAt = "2031-03-05T10:00:00" });
        await kit.Db.SaveChangesAsync();

        Assert.Equal(2, await kit.Service().RunTrialTasksAsync(HrTasksKit.At(2031, 3, 10, 9, 5)));
        Assert.Equal(2, (await TasksOfAsync(kit, dilfuza.Employee.Id)).Count);   // ertangi + kechagi
        Assert.Empty(await TasksOfAsync(kit, owner.Employee!.Id));
    }

    // ============================== «Service» — kunlik topshiriq ==============================

    private static ContactRequest Req(Student s, string status, string due = "", string reason = "Ketma-ket darsga kelmadi") => new()
    {
        StudentId = s.Id, StudentName = s.FullName, ReasonLabel = reason, Status = status, DueDate = due, CreatedAt = "2031-03-01T10:00:00",
    };

    [Fact]
    public async Task Service__09_00_da_Munojatga_bitta_royxat__faqat_bugungi_ish__hammasi_yopilsa_bajariladi()
    {
        using var kit = new HrTasksKit();
        var munojat = await kit.PersonAsync(telegram: true, name: "Munojat Shermatova");
        await SetAsync(kit, serviceTo: munojat.Employee!.Id);

        var a = new Student { FullName = "Aliyev Sardor", ParentPhone = "998901112233" };
        var b = new Student { FullName = "Karimova Zarina" };
        var c = new Student { FullName = "Ertangi Bola" };
        var d = new Student { FullName = "Yopilgan" };
        kit.Db.AddRange(a, b, c, d);
        var r1 = Req(a, ContactStatuses.New);
        var r2 = Req(b, ContactStatuses.Callback, due: "2031-03-08", reason: "To'lov qarzdorligi");
        kit.Db.ContactRequests.AddRange(r1, r2, Req(c, ContactStatuses.Callback, due: "2031-03-11"), Req(d, ContactStatuses.Done));
        await kit.Db.SaveChangesAsync();

        Assert.Equal(0, await kit.Service().RunServiceTasksAsync(HrTasksKit.At(2031, 3, 10, 8, 55)));   // 09:00 gacha — yo'q
        Assert.Equal(1, await kit.Service().RunServiceTasksAsync(HrTasksKit.At(2031, 3, 10, 9, 5)));
        var task = Assert.Single(await TasksOfAsync(kit, munojat.Employee.Id));
        Assert.Equal("Bog'lanish kerak — 2 ta o'quvchi (10.03)", task.Title);
        Assert.Contains("Aliyev Sardor · +998 90 111 22 33 · Ketma-ket darsga kelmadi", task.Description);
        Assert.Contains("Karimova Zarina · To'lov qarzdorligi — qayta qo'ng'iroq 08.03 (kechikkan)", task.Description);
        Assert.DoesNotContain("Ertangi Bola", task.Description);
        Assert.DoesNotContain("Yopilgan", task.Description);
        Assert.Contains("Berdi: " + HrTasksService.ServiceGiverName, Assert.Single(kit.Sender.To(munojat.Chat!.Value)));

        // takror sikl — ikkinchi ro'yxat yo'q
        Assert.Equal(0, await kit.Service().RunServiceTasksAsync(HrTasksKit.At(2031, 3, 10, 9, 10)));

        // bittasi yopildi — hali ochiq; ikkalasi ham — o'zi bajariladi
        await using (var ctx = kit.NewContext())
        {
            (await ctx.ContactRequests.SingleAsync(x => x.Id == r1.Id)).Status = ContactStatuses.Done;
            await ctx.SaveChangesAsync();
        }
        await kit.Service().RunServiceTasksAsync(HrTasksKit.At(2031, 3, 10, 12, 0));
        Assert.Equal(HrTaskStatuses.Open, (await TasksOfAsync(kit, munojat.Employee.Id)).Single().Status);
        await using (var ctx = kit.NewContext())
        {
            (await ctx.ContactRequests.SingleAsync(x => x.Id == r2.Id)).Status = ContactStatuses.Failed;
            await ctx.SaveChangesAsync();
        }
        Assert.Equal(1, await kit.Service().RunServiceTasksAsync(HrTasksKit.At(2031, 3, 10, 12, 5)));
        Assert.Equal(HrTaskStatuses.Done, (await TasksOfAsync(kit, munojat.Employee.Id)).Single().Status);
    }

    [Fact]
    public async Task Service__ertasi_kuni_yangi_royxat__eskisi_bekor__sozlama_yoq_bolsa_hech_narsa()
    {
        using var kit = new HrTasksKit();
        var munojat = await kit.PersonAsync(name: "Munojat");
        var s = new Student { FullName = "Aliyev Sardor" };
        kit.Db.Add(s);
        kit.Db.ContactRequests.Add(Req(s, ContactStatuses.New));
        await kit.Db.SaveChangesAsync();

        // sozlama bo'sh — topshiriq yaratilmaydi
        Assert.Equal(0, await kit.Service().RunServiceTasksAsync(HrTasksKit.At(2031, 3, 10, 9, 5)));

        await SetAsync(kit, serviceTo: munojat.Employee!.Id);
        await kit.Service().RunServiceTasksAsync(HrTasksKit.At(2031, 3, 10, 9, 5));
        await kit.Service().RunServiceTasksAsync(HrTasksKit.At(2031, 3, 11, 9, 5));
        var tasks = await TasksOfAsync(kit, munojat.Employee.Id);
        Assert.Equal(2, tasks.Count);
        Assert.Equal(HrTaskStatuses.Cancelled, tasks[0].Status);
        Assert.Equal(HrTaskStatuses.Open, tasks[1].Status);
        Assert.EndsWith("(11.03)", tasks[1].Title);
    }

    [Fact]
    public async Task Sozlama__faqat_faol_xodim__bosh_qiymat_olib_tashlaydi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(Roles.Admin, hrRole: null);
        var panel = await kit.PanelAsync(admin.User);
        var munojat = await kit.PersonAsync(name: "Munojat");
        var current = await kit.Service().SettingsAsync(panel);
        HrSettingsInput Input(string? service) => new(current.Timezone, current.DigestTime, [.. current.DigestWeekdays],
            current.MorningListEnabled, current.MorningTime, current.GenerationTime, current.CloseOverdueTime, current.ReminderEnabled,
            [.. current.ReminderOffsets], current.RemindEndOfDay, ServiceTaskEmployeeId: service);

        var saved = await kit.Service().UpdateSettingsAsync(panel, Input(munojat.Employee!.Id));
        Assert.Equal(munojat.Employee.Id, saved.ServiceTaskEmployeeId);
        await Assert.ThrowsAnyAsync<Exception>(() => kit.Service().UpdateSettingsAsync(panel, Input("yoq-xodim")));
        Assert.Null((await kit.Service().UpdateSettingsAsync(panel, Input(""))).ServiceTaskEmployeeId);
    }
}

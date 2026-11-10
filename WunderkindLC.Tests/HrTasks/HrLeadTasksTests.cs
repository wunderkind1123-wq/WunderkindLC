using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.HrTasks;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.HrTasks;

/// <summary>
/// LID (SOTUV) TOPSHIRIQLARI — lid sahifasidan qo'yilgan «keyingi qadam» oddiy HR topshirig'i bo'ladi
/// (egasi 2026-11-10: «Vazifalar alohida bo'lmasin, Topshiriqlar bilan bitta, sotuvnikilari ajralib tursin»).
/// Qoidalar: `.claude/rules/crm-leads.md` «LID TOPSHIRIQLARI», kod `LeadTasks` + `HrTasksService.Leads.cs`.
/// </summary>
public class HrLeadTasksTests
{
    private sealed class NoHttpContext : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => null; set { } }
    }

    private static string Tomorrow(string time = "10:00") => AppClock.Today.AddDays(1).ToString("yyyy-MM-dd") + "T" + time;
    private static LeadTasks.Draft Call(string? due = null, string? responsible = null, string text = "Narxni aytish") =>
        new("call", text, due ?? Tomorrow(), false, responsible);
    private static LeadTasks.Actor ActorOf(HrTasksKit.Person p) => new(p.User.Id, p.User.FullName);

    private static async Task<Lead> LeadAsync(HrTasksKit kit, string? assignee = null)
    {
        var lead = new Lead
        {
            FullName = "Vali Valiyev", Phone = "998901234567", Stage = "st-1", CreatedAt = AppClock.Iso(),
            AssigneeUserId = assignee, InterestSubject = "Ingliz tili",
        };
        if (!await kit.Db.LeadStages.AnyAsync()) kit.Db.LeadStages.Add(new LeadStage { Id = "st-1", Title = "Yangi", Order = 0 });
        kit.Db.Leads.Add(lead);
        await kit.Db.SaveChangesAsync();
        return lead;
    }

    // ============================== Sof qoidalar ==============================

    [Theory]
    [InlineData("2026-11-12", false, "2026-11-12", "23:59")]
    [InlineData("2026-11-12T15:30", false, "2026-11-12", "15:30")]
    [InlineData("2026-11-12T15:30:00", false, "2026-11-12", "15:30")]
    [InlineData("2026-11-12T15:30", true, "2026-11-12", "23:59")]
    public void ParseDue_sana_va_vaqt(string raw, bool allDay, string day, string time)
    {
        var (d, t) = LeadTasks.ParseDue(raw, allDay)!.Value;
        Assert.Equal(DateOnly.Parse(day), d);
        Assert.Equal(TimeOnly.Parse(time), t);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-13-40")]
    [InlineData("ertaga")]
    public void ParseDue_buzuq_sana_null(string raw) => Assert.Null(LeadTasks.ParseDue(raw, false));

    [Fact]
    public void Bucket_kechikkan_bugun_ertaga_keyinroq()
    {
        var now = new DateTime(2026, 11, 10, 12, 0, 0);
        Assert.Equal("overdue", LeadTasks.Bucket("2026-11-10T11:59:00", now));
        Assert.Equal("today", LeadTasks.Bucket("2026-11-10T18:00:00", now));
        Assert.Equal("tomorrow", LeadTasks.Bucket("2026-11-11T09:00:00", now));
        Assert.Equal("later", LeadTasks.Bucket("2026-11-14T09:00:00", now));
        Assert.Equal("later", LeadTasks.Bucket("buzuq", now));
        Assert.Equal(3, LeadTasks.OverdueDays("2026-11-07T23:59:00", now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ok ")]
    public void Natijasiz_yoki_juda_qisqa_natija_rad_etiladi(string? result) => Assert.NotNull(LeadTasks.ValidateResult(result));

    [Fact]
    public void Yopilgan_lidga_topshiriq_qoyilmaydi_va_notogri_maydonlar_rad()
    {
        Assert.NotNull(LeadTasks.ValidateDraft(new Lead { ConvertedStudentId = "s-1" }, Call()));
        Assert.NotNull(LeadTasks.ValidateDraft(new Lead { LostAt = "2026-11-01T10:00:00" }, Call()));
        Assert.NotNull(LeadTasks.ValidateDraft(new Lead(), Call() with { Type = "tur-yoq" }));
        Assert.NotNull(LeadTasks.ValidateDraft(new Lead(), Call() with { DueAt = "" }));
        Assert.Null(LeadTasks.ValidateDraft(new Lead(), Call()));
    }

    [Fact]
    public void HR_holati_lid_holatiga_va_sarlavha_cheklovi()
    {
        Assert.Equal("open", LeadTasks.StatusOf(HrTaskStatuses.Returned));
        Assert.Equal("done", LeadTasks.StatusOf(HrTaskStatuses.Done));
        Assert.Equal("cancelled", LeadTasks.StatusOf(HrTaskStatuses.Missed));
        Assert.Equal("Qo'ng'iroq: Vali — narx", LeadTasks.Title("call", "Vali", "narx"));
        Assert.True(LeadTasks.Title("call", "Vali", new string('x', 500)).Length <= HrLimits.TitleMax);
    }

    // ============================== Yaratish ==============================

    [Fact]
    public async Task Yaratish__HR_topshirigi_lidga_boglanadi_masulga_Telegram_va_lid_tarixi()
    {
        using var kit = new HrTasksKit();
        var manager = await kit.PersonAsync(name: "Menejer Ali");
        var operatorP = await kit.PersonAsync(name: "Operator Vali", telegram: true);
        var lead = await LeadAsync(kit, assignee: operatorP.User.Id);

        var res = await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(manager));

        Assert.Equal(200, res.Status);
        await using var db = kit.NewContext();
        var task = Assert.Single(db.HrTasks);
        Assert.Equal(operatorP.Employee!.Id, task.EmployeeId);          // standart — lid mas'uli
        Assert.Equal(HrTaskSources.Director, task.Source);
        Assert.True(task.ProofRequired);                                  // natija = isbot
        Assert.Equal(manager.User.Id, task.CreatedBy);
        Assert.Contains("Vali Valiyev", task.Title);
        Assert.Contains("998901234567", task.Description);
        var link = Assert.Single(db.HrLeadTasks);
        Assert.Equal((lead.Id, task.Id), (link.LeadId, link.TaskId));
        Assert.Contains(db.LeadEvents, e => e.LeadId == lead.Id && e.Type == LeadTasks.EventCreated);
        // Telegram — to'liq xabar + «✅ Qabul qildim»
        Assert.Contains(kit.Sender.TaskButtons, b => b.Chat == operatorP.Chat && b.TaskId == task.Id);
        Assert.Equal("10:00", res.Task!.DueAt[11..16]);
        Assert.Equal("open", res.Task.Status);
    }

    [Fact]
    public async Task Yaratish__biriktirilmagan_lidga_yaratuvchi_masul_boladi_va_topshiriq_ozida()
    {
        using var kit = new HrTasksKit();
        var me = await kit.PersonAsync(telegram: true);
        var lead = await LeadAsync(kit);

        var res = await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(me));

        Assert.Equal(200, res.Status);
        await using var db = kit.NewContext();
        Assert.Equal(me.User.Id, db.Leads.Single().AssigneeUserId);
        Assert.Equal(me.Employee!.Id, db.HrTasks.Single().EmployeeId);
        // o'ziga qo'ygan topshiriq — Telegram xabari shart emas
        Assert.Empty(kit.Sender.TaskButtons);
    }

    [Fact]
    public async Task Yaratish__HR_xodimi_bolmagan_masul_rad_etiladi()
    {
        using var kit = new HrTasksKit();
        var superadmin = await kit.PersonAsync(role: Roles.SuperAdmin, hrRole: null);
        var lead = await LeadAsync(kit);
        var svc = kit.Service();

        Assert.Equal(400, (await svc.CreateLeadTaskAsync(lead.Id, Call(), ActorOf(superadmin))).Status);
        Assert.Equal(400, (await svc.CreateLeadTaskAsync(lead.Id, Call(responsible: superadmin.User.Id), ActorOf(superadmin))).Status);
        await using var db = kit.NewContext();
        Assert.Empty(db.HrTasks);
        Assert.Empty(db.HrLeadTasks);
    }

    // ============================== Bajarish ==============================

    [Fact]
    public async Task Bajarish__natija_va_keyingi_topshiriq_BITTA_tranzaksiyada()
    {
        using var kit = new HrTasksKit();
        var me = await kit.PersonAsync();
        var other = await kit.PersonAsync(telegram: true);
        var lead = await LeadAsync(kit, assignee: me.User.Id);
        var first = (await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(me))).Task!;

        var res = await kit.Service().CompleteLeadTaskAsync(first.Id, " Ko'tarmadi ",
            Call(responsible: other.User.Id, text: "Qayta qo'ng'iroq"), ActorOf(me));

        Assert.Equal(200, res.Status);
        Assert.Equal("done", res.Task!.Status);
        Assert.Equal("Ko'tarmadi", res.Task.Result);
        Assert.NotNull(res.Next);
        await using var db = kit.NewContext();
        Assert.Equal(HrTaskStatuses.Done, db.HrTasks.Single(t => t.Id == first.Id).Status);
        Assert.Equal(other.Employee!.Id, db.HrTasks.Single(t => t.Id == res.Next!.Id).EmployeeId);
        Assert.Contains(db.LeadEvents, e => e.Type == LeadTasks.EventDone && e.Text.Contains("Ko'tarmadi"));
        Assert.Contains(kit.Sender.TaskButtons, b => b.Chat == other.Chat);
    }

    [Fact]
    public async Task Bajarish__natijasiz_yoki_notogri_keyingi_bilan_HECH_NARSA_ozgarmaydi()
    {
        using var kit = new HrTasksKit();
        var me = await kit.PersonAsync();
        var lead = await LeadAsync(kit, assignee: me.User.Id);
        var first = (await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(me))).Task!;

        Assert.Equal(400, (await kit.Service().CompleteLeadTaskAsync(first.Id, "", null, ActorOf(me))).Status);
        Assert.Equal(400, (await kit.Service().CompleteLeadTaskAsync(first.Id, "Gaplashdim", Call(due: ""), ActorOf(me))).Status);

        await using var db = kit.NewContext();
        Assert.Equal(HrTaskStatuses.Open, db.HrTasks.Single().Status);
        Assert.Single(db.HrLeadTasks);
    }

    [Fact]
    public async Task Xodim_Mening_topshiriqlarimda_bajarsa__natija_lid_tarixiga_tushadi()
    {
        using var kit = new HrTasksKit();
        var me = await kit.PersonAsync();
        var lead = await LeadAsync(kit, assignee: me.User.Id);
        var t = (await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(me))).Task!;

        // natijasiz (isbotsiz) — HR ham rad etadi
        await Assert.ThrowsAnyAsync<Exception>(() => kit.Service().CompleteAsync(me.Employee!, t.Id, new HrCompleteInput(null, null)));
        await kit.Service().CompleteAsync(me.Employee!, t.Id, new HrCompleteInput("Sinovga yozildi", null));

        await using var db = kit.NewContext();
        var ev = Assert.Single(db.LeadEvents, e => e.Type == LeadTasks.EventDone);
        Assert.Contains("Sinovga yozildi", ev.Text);
        Assert.Equal(me.User.Id, ev.ActorUserId);
    }

    // ============================== Avto-bekor, tahrir ==============================

    [Fact]
    public async Task Lid_YOQOTILGANDA_ochiq_topshiriqlar_bekor_bajarilgani_tegilmaydi()
    {
        using var kit = new HrTasksKit();
        var me = await kit.PersonAsync();
        var lead = await LeadAsync(kit, assignee: me.User.Id);
        var a = (await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(me))).Task!;
        var b = (await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(me))).Task!;
        await kit.Service().CompleteLeadTaskAsync(b.Id, "Gaplashdim", null, ActorOf(me));
        var reason = new ActionReason { Category = LeadLoss.ReasonCategory, Label = "Narx qimmatlik qildi" };
        kit.Db.ActionReasons.Add(reason);
        await kit.Db.SaveChangesAsync();

        await using (var ctx = kit.NewContext())
        {
            var res = await LeadLoss.MarkLostAsync(ctx, new AuditService(ctx, new NoHttpContext()),
                lead.Id, reason.Id, null, new LeadLoss.Actor(me.User.Id, me.User.FullName));
            Assert.Equal(200, res.Status);
        }

        await using var db = kit.NewContext();
        var cancelled = db.HrTasks.Single(t => t.Id == a.Id);
        Assert.Equal(HrTaskStatuses.Cancelled, cancelled.Status);
        Assert.Contains("arxivlandi", cancelled.DirectorComment);
        Assert.Equal(HrTaskStatuses.Done, db.HrTasks.Single(t => t.Id == b.Id).Status);
        // Yo'qotilgan lidga yangi topshiriq qo'yilmaydi.
        Assert.Equal(400, (await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(me))).Status);
    }

    [Fact]
    public async Task Tahrir__muddat_va_masul_almashadi_yangi_masulga_xabar()
    {
        using var kit = new HrTasksKit();
        var me = await kit.PersonAsync();
        var other = await kit.PersonAsync(telegram: true);
        var lead = await LeadAsync(kit, assignee: me.User.Id);
        var t = (await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(me))).Task!;

        var res = await kit.Service().UpdateLeadTaskAsync(t.Id,
            Call(due: AppClock.Today.AddDays(3).ToString("yyyy-MM-dd"), responsible: other.User.Id) with { AllDay = true }, ActorOf(me));

        Assert.Equal(200, res.Status);
        Assert.EndsWith("T23:59:00", res.Task!.DueAt);
        Assert.True(res.Task.AllDay);
        Assert.Equal(other.User.Id, res.Task.ResponsibleUserId);
        Assert.Contains(kit.Sender.TaskButtons, b => b.Chat == other.Chat);

        Assert.Equal(200, (await kit.Service().CancelLeadTaskAsync(t.Id, "kerak emas", ActorOf(me))).Status);
        Assert.Equal(400, (await kit.Service().CancelLeadTaskAsync(t.Id, null, ActorOf(me))).Status);
        Assert.Equal(400, (await kit.Service().UpdateLeadTaskAsync(t.Id, Call(), ActorOf(me))).Status);
    }

    [Fact]
    public async Task Tahrir__muddat_siljisa_eslatmalar_yangi_muddat_uchun_QAYTA_ketadi()
    {
        using var kit = new HrTasksKit();
        var me = await kit.PersonAsync();
        var lead = await LeadAsync(kit, assignee: me.User.Id);
        var t = (await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(me))).Task!;
        kit.Db.HrTaskReminders.AddRange(
            new HrTaskReminder { TaskId = t.Id, OffsetMinutes = 30 },
            new HrTaskReminder { TaskId = t.Id, OffsetMinutes = HrTasksService.DueNowOffset });
        await kit.Db.SaveChangesAsync();

        // faqat matn o'zgarsa — eslatma belgilari qoladi
        await kit.Service().UpdateLeadTaskAsync(t.Id, Call(text: "boshqa matn"), ActorOf(me));
        await using (var db = kit.NewContext()) Assert.Equal(2, db.HrTaskReminders.Count(r => r.TaskId == t.Id));

        await kit.Service().UpdateLeadTaskAsync(t.Id, Call(due: Tomorrow("17:00")), ActorOf(me));
        await using (var db = kit.NewContext()) Assert.Empty(db.HrTaskReminders.Where(r => r.TaskId == t.Id));
    }

    [Fact]
    public async Task Xodim_nofaol_bolsa__lid_topshirigi_bekor_va_lid_tarixida_sababi()
    {
        using var kit = new HrTasksKit();
        var me = await kit.PersonAsync();
        var lead = await LeadAsync(kit, assignee: me.User.Id);
        var t = (await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(me))).Task!;

        await using (var ctx = kit.NewContext())
        {
            Assert.Equal(1, await HrTasksService.SetAccountActiveAsync(ctx, me.User.Id, false, DateTimeOffset.UtcNow));
            await ctx.SaveChangesAsync();
        }

        await using var db = kit.NewContext();
        Assert.Equal(HrTaskStatuses.Cancelled, db.HrTasks.Single(x => x.Id == t.Id).Status);
        var ev = Assert.Single(db.LeadEvents, e => e.LeadId == lead.Id && e.Type == LeadTasks.EventCancelled);
        Assert.Contains("faol emas", ev.Text);
    }

    [Fact]
    public async Task Xodim_bajarib_QAYTA_ochsa__lid_tarixida_ham_korinadi()
    {
        using var kit = new HrTasksKit();
        var me = await kit.PersonAsync();
        var lead = await LeadAsync(kit, assignee: me.User.Id);
        var t = (await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(me))).Task!;
        await kit.Service().CompleteAsync(me.Employee!, t.Id, new HrCompleteInput("Gaplashdim", null));

        await kit.Service().ReopenAsync(me.Employee!, t.Id);

        await using var db = kit.NewContext();
        Assert.Contains(db.LeadEvents, e => e.LeadId == lead.Id && e.Text!.StartsWith("Topshiriq qayta ochildi"));
        Assert.Equal(LeadTasks.StatusOpen, (await kit.Service().LeadTasksAsync(lead.Id, "open")).Single().Status);
    }

    // ============================== «Topshiriqlar»da ajralib turishi, kanban ==============================

    [Fact]
    public async Task Panel_va_Mening_topshiriqlarimda_SOTUV_belgisi_va_lid_havolasi()
    {
        using var kit = new HrTasksKit();
        var admin = await kit.PersonAsync(role: Roles.Admin, hrRole: HrRoles.Director);
        var lead = await LeadAsync(kit, assignee: admin.User.Id);
        var leadTask = (await kit.Service().CreateLeadTaskAsync(lead.Id, Call(), ActorOf(admin))).Task!;
        var plain = await kit.TaskAsync(admin.Employee!.Id, DateTimeOffset.UtcNow.AddDays(1), title: "Hisobot tayyorlash");

        var svc = kit.Service();
        var page = await svc.TasksAsync(await kit.PanelAsync(admin.User, svc), new HrTaskFilter(), new HrPaging());
        var sales = Assert.Single(page.Items, i => i.Id == leadTask.Id);
        Assert.Equal("lead", sales.SalesKind);
        Assert.Equal(lead.Id, sales.LeadId);
        Assert.Equal("Vali Valiyev", sales.LeadName);
        Assert.Null(Assert.Single(page.Items, i => i.Id == plain.Id).SalesKind);

        var mine = await kit.Service().MyTaskAsync(admin.Employee!, leadTask.Id);
        Assert.Equal("lead", mine.SalesKind);
        Assert.Equal(lead.Id, mine.LeadId);
    }

    [Fact]
    public async Task Kanban_indikatori__eng_yaqin_ochiq_topshiriq_va_soni()
    {
        using var kit = new HrTasksKit();
        var me = await kit.PersonAsync();
        var lead = await LeadAsync(kit, assignee: me.User.Id);
        var svc = kit.Service();
        await svc.CreateLeadTaskAsync(lead.Id, Call(due: Tomorrow("15:00"), text: "keyin"), ActorOf(me));
        await kit.Service().CreateLeadTaskAsync(lead.Id, Call(due: Tomorrow("09:00"), text: "avval"), ActorOf(me));

        await using var db = kit.NewContext();
        var next = (await LeadTasks.OpenByLeadAsync(db))[lead.Id];
        Assert.Equal("avval", next.Text);
        Assert.Equal(2, next.OpenCount);
        Assert.EndsWith("T09:00:00", next.DueAt);
    }
}

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.HrTasks;

/// <summary>
/// LC: LID (SOTUV) TOPSHIRIQLARI — LMS'da yo'q (`.claude/rules/crm-leads.md` «LID TOPSHIRIQLARI», sotuv rejasi
/// 1-qism). Lid sahifasidan qo'yilgan «keyingi qadam» — oddiy <see cref="HrTask"/> + <see cref="HrLeadTask"/>
/// bog'lanishi: xodimning «Mening topshiriqlarim»ida, panelda (sotuv belgisi bilan) ko'rinadi va Telegram'ga
/// to'liq xabar + «✅ Qabul qildim» bilan boradi. Qoidalar — <see cref="LeadTasks"/>.
///
/// <para>Ruxsat — CHAQIRUVCHI controllerda (<c>leads.list</c>): lid bilan ishlaydigan xodim HR panel kalitisiz ham
/// lidga topshiriq qo'ya oladi (sinov darsi topshiriqlari kabi — tizim yo'li, panel doirasi qo'llanmaydi).</para>
/// </summary>
public sealed partial class HrTasksService
{
    /// <summary>Lid topshirig'i amali natijasi: 200 | 400 | 404.</summary>
    public sealed record LeadTaskResult(int Status, string? Error, LeadTaskDto? Task = null, LeadTaskDto? Next = null)
    {
        public static LeadTaskResult Bad(string e) => new(400, e);
        public static LeadTaskResult NotFound(string e = "Topshiriq topilmadi") => new(404, e);
    }

    /// <summary>Lidning (yoki hammaning) topshiriqlari. <paramref name="status"/>: open | done | cancelled | all.</summary>
    public async Task<List<LeadTaskDto>> LeadTasksAsync(string? leadId, string? status, CancellationToken ct = default)
    {
        var q = from l in db.HrLeadTasks.AsNoTracking()
                join t in db.HrTasks.AsNoTracking() on l.TaskId equals t.Id
                select new { Link = l, Task = t };
        if (!string.IsNullOrWhiteSpace(leadId)) q = q.Where(x => x.Link.LeadId == leadId);
        var st = string.IsNullOrWhiteSpace(status) ? LeadTasks.StatusOpen : status.Trim();
        q = st switch
        {
            LeadTasks.StatusOpen => q.Where(x => x.Task.Status == HrTaskStatuses.Open || x.Task.Status == HrTaskStatuses.Returned),
            LeadTasks.StatusDone => q.Where(x => x.Task.Status == HrTaskStatuses.Done),
            LeadTasks.StatusCancelled => q.Where(x => x.Task.Status == HrTaskStatuses.Cancelled || x.Task.Status == HrTaskStatuses.Missed),
            _ => q,
        };
        var rows = await q.OrderBy(x => x.Task.DueAt).Take(500).ToListAsync(ct);
        return await LeadTaskDtosAsync(rows.Select(r => (r.Link, r.Task)).ToList(), ct);
    }

    /// <summary>Mas'ul tanlovi: FAOL HR xodimlari — panel akkauntlari (staff/admin); o'qituvchilar emas.</summary>
    public async Task<List<LeadTaskPersonDto>> LeadTaskPeopleAsync(CancellationToken ct = default)
    {
        var rows = await (from e in db.HrEmployees.AsNoTracking()
                          join u in db.Users.AsNoTracking() on e.UserId equals u.Id
                          where e.IsActive && (u.Role == Roles.Staff || u.Role == Roles.Admin)
                          orderby e.FullName
                          select new { u.Id, e.FullName }).ToListAsync(ct);
        return [.. rows.Select(r => new LeadTaskPersonDto(r.Id, r.FullName))];
    }

    /// <summary>
    /// Yangi topshiriq. Mas'ul (user id) berilmasa — lid mas'uli, u ham HR xodimi bo'lmasa — yaratuvchi.
    /// Biriktirilmagan lidga yaratuvchi mas'ul bo'ladi (`ChangeStage` qoidasi, bori ustidan yozilmaydi).
    /// </summary>
    public async Task<LeadTaskResult> CreateLeadTaskAsync(
        string leadId, LeadTasks.Draft d, LeadTasks.Actor actor, CancellationToken ct = default)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return LeadTaskResult.NotFound("Lid topilmadi");
        var (res, task, link) = await AddLeadTaskAsync(lead, d, actor, ct);
        if (res is not null) return res;
        await db.SaveChangesAsync(ct);
        await NotifyLeadTaskAsync(task!, actor, ct);
        return new LeadTaskResult(200, null, (await LeadTaskDtosAsync([(link!, task!)], ct))[0]);
    }

    /// <summary>
    /// Lid sahifasidan BAJARISH: natija majburiy; <paramref name="next"/> — o'sha tranzaksiyada keyingi topshiriq
    /// (u noto'g'ri bo'lsa bajarish ham yozilmaydi — natija yo'qolmasin).
    /// </summary>
    public async Task<LeadTaskResult> CompleteLeadTaskAsync(
        string taskId, string? result, LeadTasks.Draft? next, LeadTasks.Actor actor, CancellationToken ct = default)
    {
        var (link, task) = await LeadTaskRowAsync(taskId, ct);
        if (link is null || task is null) return LeadTaskResult.NotFound();
        if (!task.IsOpen) return LeadTaskResult.Bad("Topshiriq allaqachon yopilgan.");
        if (LeadTasks.ValidateResult(result) is { } resultError) return LeadTaskResult.Bad(resultError);
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == link.LeadId, ct);
        if (lead is null) return LeadTaskResult.NotFound("Lid topilmadi");
        if (next is not null && LeadTasks.ValidateDraft(lead, next) is { } nextError)
            return LeadTaskResult.Bad("Keyingi topshiriq: " + nextError);

        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var text = result!.Trim();
        task.Complete(Now, text, 0);
        LeadTasks.AddEvent(db, lead.Id, LeadTasks.EventDone,
            $"Topshiriq bajarildi: {Describe(link, task, tz)} — natija: {text}", actor);

        HrTask? nextTask = null;
        HrLeadTask? nextLink = null;
        if (next is not null)
        {
            var (res, t, l) = await AddLeadTaskAsync(lead, next, actor, ct);
            if (res is not null) return res with { Error = "Keyingi topshiriq: " + res.Error };
            (nextTask, nextLink) = (t, l);
        }
        await db.SaveChangesAsync(ct);
        if (nextTask is not null) await NotifyLeadTaskAsync(nextTask, actor, ct);

        var pairs = new List<(HrLeadTask, HrTask)> { (link, task) };
        if (nextTask is not null) pairs.Add((nextLink!, nextTask));
        var dtos = await LeadTaskDtosAsync(pairs, ct);
        return new LeadTaskResult(200, null, dtos[0], dtos.Count > 1 ? dtos[1] : null);
    }

    /// <summary>Tur / matn / muddat / mas'ulni o'zgartiradi (faqat ochiq). Mas'ul almashsa — yangisiga xabar.</summary>
    public async Task<LeadTaskResult> UpdateLeadTaskAsync(
        string taskId, LeadTasks.Draft d, LeadTasks.Actor actor, CancellationToken ct = default)
    {
        var (link, task) = await LeadTaskRowAsync(taskId, ct);
        if (link is null || task is null) return LeadTaskResult.NotFound();
        if (!task.IsOpen) return LeadTaskResult.Bad("Yopilgan topshiriqni tahrirlab bo'lmaydi.");
        if (LeadTasks.ValidateFields(d) is { } error) return LeadTaskResult.Bad(error);
        var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == link.LeadId, ct);

        var newEmployee = task.EmployeeId;
        var responsible = (d.ResponsibleUserId ?? "").Trim();
        if (responsible.Length > 0)
        {
            var emp = await EmployeeOfUserAsync(responsible, ct);
            if (emp is null) return LeadTaskResult.Bad(NotEmployeeMessage);
            newEmployee = emp.Id;
        }

        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var oldDue = Describe(link, task, tz);
        var previousDue = task.DueAt;
        var (day, time) = LeadTasks.ParseDue(d.DueAt, d.AllDay)!.Value;
        link.Type = d.Type!.Trim();
        link.Text = (d.Text ?? "").Trim();
        link.AllDay = d.AllDay;
        task.UpdateDetails(Now, LeadTasks.Title(link.Type, lead?.FullName ?? "", link.Text), true,
            lead is null ? task.Description : LeadTasks.Description(lead, link.Text), HrValues.At(day, time, tz));
        var reassigned = newEmployee != task.EmployeeId;
        if (reassigned)
        {
            task.EmployeeId = newEmployee;
            task.UpdatedAt = Now;
        }
        var newDue = Describe(link, task, tz);
        // Muddat siljisa «30 daqiqa qoldi» / «muddati keldi» yangi muddat uchun QAYTA ketsin (panel tahriri bilan bir xil).
        if (task.DueAt != previousDue) await ForgetRemindersAsync(task.Id, ct);
        if (oldDue != newDue || reassigned)
            LeadTasks.AddEvent(db, link.LeadId, LeadTasks.EventCreated,
                $"Topshiriq o'zgartirildi: {oldDue} → {newDue}" + (reassigned ? " (mas'ul almashdi)" : ""), actor);
        await db.SaveChangesAsync(ct);
        if (reassigned) await NotifyLeadTaskAsync(task, actor, ct);
        return new LeadTaskResult(200, null, (await LeadTaskDtosAsync([(link, task)], ct))[0]);
    }

    /// <summary>Qo'lda bekor qilish (sabab ixtiyoriy).</summary>
    public async Task<LeadTaskResult> CancelLeadTaskAsync(
        string taskId, string? reason, LeadTasks.Actor actor, CancellationToken ct = default)
    {
        var (link, task) = await LeadTaskRowAsync(taskId, ct);
        if (link is null || task is null) return LeadTaskResult.NotFound();
        if (!task.IsOpen) return LeadTaskResult.Bad("Topshiriq allaqachon yopilgan.");
        var r = (reason ?? "").Trim();
        if (r.Length > 500) return LeadTaskResult.Bad("Sabab juda uzun — ko'pi bilan 500 belgi.");
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        task.Cancel(Now, r.Length == 0 ? null : r);
        LeadTasks.AddEvent(db, link.LeadId, LeadTasks.EventCancelled,
            $"Topshiriq bekor qilindi: {Describe(link, task, tz)}" + (r.Length > 0 ? $" — {r}" : ""), actor);
        await db.SaveChangesAsync(ct);
        return new LeadTaskResult(200, null, (await LeadTaskDtosAsync([(link, task)], ct))[0]);
    }

    /// <summary>HR tomonidagi o'zgarish turi — lid tarixiga qanday yoziladi.</summary>
    private enum LeadTaskChange { Done, Cancelled, Reopened, Returned }

    /// <summary>
    /// HR tomonida (xodim «Bajardim» / «Qayta ochish», panel «Bekor qilish» / «Qaytarish») o'zgargan LID topshirig'i lid
    /// tarixida ham ko'rinsin. SaveChanges QILMAYDI — chaqiruvchining saqlashi bilan birga yoziladi. Lidga bog'lanmagan
    /// topshiriqda hech narsa.
    /// </summary>
    private async Task RecordLeadTaskChangeAsync(HrTask task, LeadTaskChange change, string actorName, string? actorUserId, CancellationToken ct)
    {
        var link = await db.HrLeadTasks.AsNoTracking().FirstOrDefaultAsync(l => l.TaskId == task.Id, ct);
        if (link is null) return;
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var what = Describe(link, task, tz);
        var (type, text) = change switch
        {
            LeadTaskChange.Done => (LeadTasks.EventDone, $"Topshiriq bajarildi: {what}"
                + (string.IsNullOrWhiteSpace(task.EmployeeComment) ? "" : $" — natija: {task.EmployeeComment}")),
            LeadTaskChange.Cancelled => (LeadTasks.EventCancelled, $"Topshiriq bekor qilindi: {what}"
                + (string.IsNullOrWhiteSpace(task.DirectorComment) ? "" : $" — {task.DirectorComment}")),
            LeadTaskChange.Reopened => (LeadTasks.EventCreated, $"Topshiriq qayta ochildi: {what}"),
            _ => (LeadTasks.EventCreated, $"Topshiriq qayta ishlashga qaytarildi: {what}"
                + (string.IsNullOrWhiteSpace(task.DirectorComment) ? "" : $" — {task.DirectorComment}")),
        };
        LeadTasks.AddEvent(db, link.LeadId, type, text, new LeadTasks.Actor(actorUserId, actorName));
    }

    /// <summary>Panel amalini bajargan odamning ismi (lid tarixi uchun): HR qatori → akkaunt → «Rahbar».</summary>
    private async Task<string> PanelActorNameAsync(HrPanel panel, CancellationToken ct) =>
        panel.Self?.FullName
        ?? await db.Users.AsNoTracking().Where(u => u.Id == panel.Actor.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct)
        ?? "Rahbar";

    /// <summary>
    /// Xodim nofaol bo'lganda (ishdan bo'shatish, akkaunt o'chirish/arxivlash) uning OCHIQ topshiriqlari bekor qilinadi —
    /// YAGONA joy (`People`, `Sync`). Lid topshirig'i bo'lsa lid tarixiga sabab yoziladi: aks holda lid «keyingi qadam»i
    /// izsiz yo'qolardi. SaveChanges QILMAYDI. Nechta bekor qilingani qaytadi.
    /// </summary>
    private static async Task<int> CancelEmployeeOpenTasksAsync(
        Abstractions.IAppDbContext db, string employeeId, DateTimeOffset now, CancellationToken ct)
    {
        var cancelled = (await db.HrTasks
                .Where(t => t.EmployeeId == employeeId && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned))
                .ToListAsync(ct))
            .Where(t => t.Cancel(now)).ToList();
        if (cancelled.Count == 0) return 0;
        var ids = cancelled.Select(t => t.Id).ToList();
        var links = await db.HrLeadTasks.AsNoTracking().Where(l => ids.Contains(l.TaskId)).ToListAsync(ct);
        if (links.Count > 0)
        {
            var tz = await LeadTasks.ZoneAsync(db, ct);
            var byId = cancelled.ToDictionary(t => t.Id);
            foreach (var link in links)
                LeadTasks.AddEvent(db, link.LeadId, LeadTasks.EventCancelled,
                    $"Topshiriq bekor qilindi: {Describe(link, byId[link.TaskId], tz)} — mas'ul xodim faol emas. Yangi topshiriq qo'ying.",
                    new LeadTasks.Actor(null, "Tizim"));
        }
        return cancelled.Count;
    }

    // ------------------------------------------------------------------------------------------

    private const string NotEmployeeMessage =
        "Bu xodim «Topshiriqlar» tizimida yo'q (faol xodim emas) — boshqa mas'ulni tanlang.";

    private async Task<HrEmployee?> EmployeeOfUserAsync(string userId, CancellationToken ct) =>
        await db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId && e.IsActive, ct);

    /// <summary>Topshiriq + bog'lanish yaratadi (saqlamaydi). Xato bo'lsa — natija, aks holda (null, task, link).</summary>
    private async Task<(LeadTaskResult? Error, HrTask? Task, HrLeadTask? Link)> AddLeadTaskAsync(
        Lead lead, LeadTasks.Draft d, LeadTasks.Actor actor, CancellationToken ct)
    {
        if (LeadTasks.ValidateDraft(lead, d) is { } error) return (LeadTaskResult.Bad(error), null, null);

        // Mas'ul: tanlangan → lid mas'uli → yaratuvchi. Faqat FAOL HR xodimi (superadmin HR'da yo'q).
        HrEmployee? employee;
        var chosen = (d.ResponsibleUserId ?? "").Trim();
        if (chosen.Length > 0)
        {
            employee = await EmployeeOfUserAsync(chosen, ct);
            if (employee is null) return (LeadTaskResult.Bad(NotEmployeeMessage), null, null);
        }
        else
        {
            employee = (string.IsNullOrWhiteSpace(lead.AssigneeUserId) ? null : await EmployeeOfUserAsync(lead.AssigneeUserId!, ct))
                       ?? (string.IsNullOrWhiteSpace(actor.UserId) ? null : await EmployeeOfUserAsync(actor.UserId!, ct));
            if (employee is null)
                return (LeadTaskResult.Bad("Mas'ul xodimni tanlang — lid mas'uli va siz «Topshiriqlar» tizimida yo'qsiz."), null, null);
        }

        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var (day, time) = LeadTasks.ParseDue(d.DueAt, d.AllDay)!.Value;
        var now = Now;
        var type = d.Type!.Trim();
        var text = (d.Text ?? "").Trim();
        var task = new HrTask
        {
            EmployeeId = employee.Id,
            Source = HrTaskSources.Director,
            Title = LeadTasks.Title(type, lead.FullName, text),
            Description = LeadTasks.Description(lead, text),
            Kind = HrTaskKinds.Check,
            // «Natija majburiy» = HR isboti (izoh yoki fayl) — xodim «Mening topshiriqlarim»da ham natijasiz yopolmaydi.
            ProofRequired = true,
            OverduePolicy = HrOverduePolicies.KeepOpen,
            PeriodStart = HrValues.LocalDate(now, tz),
            DueAt = HrValues.At(day, time, tz),
            Status = HrTaskStatuses.Open,
            CreatedBy = actor.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var link = new HrLeadTask { TaskId = task.Id, LeadId = lead.Id, Type = type, Text = text, AllDay = d.AllDay, CreatedAt = now };
        db.HrTasks.Add(task);
        db.HrLeadTasks.Add(link);
        if (string.IsNullOrWhiteSpace(lead.AssigneeUserId) && !string.IsNullOrWhiteSpace(actor.UserId))
            lead.AssigneeUserId = actor.UserId;
        LeadTasks.AddEvent(db, lead.Id, LeadTasks.EventCreated,
            $"Topshiriq qo'yildi: {Describe(link, task, tz)} → {employee.FullName}", actor);
        return (null, task, link);
    }

    private async Task NotifyLeadTaskAsync(HrTask task, LeadTasks.Actor actor, CancellationToken ct)
    {
        var userId = await db.HrEmployees.AsNoTracking().Where(e => e.Id == task.EmployeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(userId) || userId == actor.UserId) return;   // o'ziga qo'ygan — xabar shart emas
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        await NotifyAssignedAsync(userId, task, tz, actor.Name, ct);
    }

    private async Task<(HrLeadTask? Link, HrTask? Task)> LeadTaskRowAsync(string taskId, CancellationToken ct)
    {
        var link = await db.HrLeadTasks.FirstOrDefaultAsync(l => l.TaskId == taskId, ct);
        if (link is null) return (null, null);
        return (link, await db.HrTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct));
    }

    private static string Describe(HrLeadTask link, HrTask task, TimeZoneInfo tz) =>
        $"{LeadTasks.TypeLabel(link.Type)}, {LeadTasks.DueText(HrValues.Local(task.DueAt, tz), link.AllDay)}"
        + (link.Text.Length > 0 ? $" — {link.Text}" : "");

    /// <summary>Lid sahifasi DTO'lari (lid, mas'ul, yaratuvchi nomlari — to'plamli so'rovlar).</summary>
    private async Task<List<LeadTaskDto>> LeadTaskDtosAsync(List<(HrLeadTask Link, HrTask Task)> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var tz = (await GetOrCreateSettingsAsync(ct)).Zone;
        var leadIds = rows.Select(r => r.Link.LeadId).Distinct().ToList();
        var leads = await db.Leads.AsNoTracking().Where(l => leadIds.Contains(l.Id))
            .Select(l => new { l.Id, l.FullName, l.Phone, l.FatherPhone, l.MotherPhone, l.Stage }).ToDictionaryAsync(l => l.Id, ct);
        var stageIds = leads.Values.Select(l => l.Stage).Distinct().ToList();
        var stages = await db.LeadStages.AsNoTracking().Where(s => stageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Title, ct);
        var employeeIds = rows.Select(r => r.Task.EmployeeId).Distinct().ToList();
        var employees = await db.HrEmployees.AsNoTracking().Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.UserId, e.FullName }, ct);
        var creatorIds = rows.Select(r => r.Task.CreatedBy).OfType<string>().Distinct().ToList();
        var creators = await db.Users.AsNoTracking().Where(u => creatorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var now = Now;
        var localNow = HrValues.Local(now, tz);

        return [.. rows.Select(r =>
        {
            var (link, t) = r;
            var lead = leads.GetValueOrDefault(link.LeadId);
            var phone = lead is null ? ""
                : !string.IsNullOrWhiteSpace(lead.Phone) ? lead.Phone
                : !string.IsNullOrWhiteSpace(lead.FatherPhone) ? lead.FatherPhone
                : lead.MotherPhone ?? "";
            var emp = employees.GetValueOrDefault(t.EmployeeId);
            var dueLocal = HrValues.Local(t.DueAt, tz).ToString(LeadTasks.IsoFormat, CultureInfo.InvariantCulture);
            var status = LeadTasks.StatusOf(t.Status);
            var open = status == LeadTasks.StatusOpen;
            return new LeadTaskDto(
                t.Id, link.LeadId, lead?.FullName ?? "(lid o'chirilgan)", phone,
                lead is null ? "" : stages.GetValueOrDefault(lead.Stage ?? "") ?? "",
                link.Type, LeadTasks.TypeLabel(link.Type), link.Text, dueLocal, link.AllDay,
                emp?.UserId, emp?.FullName,
                status, t.EmployeeComment, status == LeadTasks.StatusCancelled ? t.DirectorComment : null,
                HrValues.Local(t.CreatedAt, tz).ToString(LeadTasks.IsoFormat, CultureInfo.InvariantCulture),
                t.CreatedBy is { } c ? creators.GetValueOrDefault(c, "") : "Tizim",
                t.CompletedAt is { } done ? HrValues.Local(done, tz).ToString(LeadTasks.IsoFormat, CultureInfo.InvariantCulture) : null,
                status == LeadTasks.StatusDone ? emp?.FullName : null,
                open && t.IsOverdue(now),
                open ? LeadTasks.OverdueDays(dueLocal, localNow) : 0,
                open ? LeadTasks.Bucket(dueLocal, localNow) : "later",
                t.AcceptedAt is not null);
        })];
    }
}

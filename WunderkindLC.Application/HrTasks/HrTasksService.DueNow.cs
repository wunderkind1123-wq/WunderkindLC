using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.HrTasks;

/// <summary>
/// LC: «MUDDAT KELDI» xabari — LMS'da yo'q (`.claude/rules/hr-tasks.md` §10). Egasi (2026-11-10): «topshiriq deadline
/// kelsa botdan xabar borsin». LMS'ning eslatmalari (<see cref="SendRemindersAsync"/>) faqat muddatdan OLDIN
/// (120/60/30 daq) ketadi; muddat o'tgan payt jim edi.
///
/// <para>Qoidalar: muddati shu daqiqalarda tugagan (<see cref="DueNowWindowMinutes"/> oynasi — server o'chib qolsa
/// eski kechikkanlar to'lqin bo'lib ketmasin), hali ochiq topshiriq → ijrochiga BITTA xabar. Bir topshiriqqa bir marta:
/// <c>hr_task_reminders</c> ga <see cref="DueNowOffset"/> (0) qatori bilan "band qilinadi" (unikal kalit). "Kun oxiri"
/// (23:59, vaqtsiz) topshiriqlar — oldindan eslatmalar kabi faqat <c>RemindEndOfDay</c> yoqilgan bo'lsa (yarim tunda
/// xabar yubormaslik uchun). Sozlamadagi «Eslatmalar» o'chirilsa bu xabar ham yo'q.</para>
/// </summary>
public sealed partial class HrTasksService
{
    /// <summary>Band qilish qatoridagi "offset" — muddatning O'ZI (oldindan eslatmalar ≥ 5 daq).</summary>
    public const short DueNowOffset = 0;

    /// <summary>Muddati shundan oldin tugaganlar uchun xabar YUBORILMAYDI (restartdan keyingi to'lqin bo'lmasin).</summary>
    public const int DueNowWindowMinutes = 30;

    /// <summary>Bitta sikl. Qaytaradi: yetkazilgan xabarlar soni.</summary>
    public async Task<int> SendDueNowAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        now = now.ToUniversalTime();
        var settings = await GetOrCreateSettingsAsync(ct);
        if (!settings.ReminderEnabled || !telegram.IsConfigured) return 0;
        var tz = settings.Zone;
        var since = now.AddMinutes(-DueNowWindowMinutes);
        var tasks = await (from t in db.HrTasks.AsNoTracking()
                           join e in db.HrEmployees.AsNoTracking() on t.EmployeeId equals e.Id
                           where e.IsActive && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned)
                                 && t.DueAt <= now && t.DueAt > since
                           orderby t.DueAt, t.Id
                           select new { Task = t, e.UserId }).ToListAsync(ct);
        tasks = [.. tasks.Where(x => settings.RemindEndOfDay || !HrReminderRules.IsEndOfDay(x.Task.DueAt, tz))];
        if (tasks.Count == 0) return 0;

        var chats = await ChatsAsync(tasks.Select(x => x.UserId), ct);
        tasks = [.. tasks.Where(x => chats.ContainsKey(x.UserId))];
        var ids = tasks.Select(x => x.Task.Id).ToList();
        var claimed = (await db.HrTaskReminders.AsNoTracking()
                .Where(r => ids.Contains(r.TaskId) && r.OffsetMinutes == DueNowOffset)
                .Select(r => r.TaskId).ToListAsync(ct))
            .ToHashSet();

        var plan = new Dictionary<string, List<string>>();
        foreach (var x in tasks)
        {
            if (!claimed.Add(x.Task.Id)) continue;
            db.HrTaskReminders.Add(new HrTaskReminder { TaskId = x.Task.Id, OffsetMinutes = DueNowOffset, SentAt = now });
            if (!plan.TryGetValue(x.UserId, out var titles)) plan[x.UserId] = titles = [];
            titles.Add(x.Task.Title);
        }
        if (plan.Count == 0) return 0;
        await db.SaveChangesAsync(ct); // avval band qilinadi — keyin yiqilsa ham xabar takrorlanmaydi

        var roles = await RolesAsync(plan.Keys, ct);
        var sent = 0;
        foreach (var (userId, titles) in plan)
        {
            var text = HrTasksTexts.DueNow(titles);
            foreach (var chat in chats[userId])
                if (await SafeSendAsync(chat, text, MyTasksUrl(roles.GetValueOrDefault(userId)), ct)) sent++;
        }
        return sent;
    }
}

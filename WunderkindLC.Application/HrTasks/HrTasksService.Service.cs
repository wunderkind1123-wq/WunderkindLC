using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.HrTasks;

/// <summary>
/// LC: «SERVICE» KUNLIK TOPSHIRIG'I (egasi, 2026-11-10: «servis uchun faqat Munojatga yozilsin»), `.claude/rules/hr-tasks.md`
/// §14. Har kuni 09:00 dan keyin (rejalashtiruvchi har 5 daq) — «Bog'lanish kerak» navbatidagi BUGUNGI ish (yangi talablar,
/// bugungi va muddati o'tgan qayta qo'ng'iroqlar — `ContactService` «todo» guruhi) BITTA ro'yxat bo'lib
/// <see cref="HrSettings.ServiceTaskEmployeeId"/> xodimiga boradi. Kuniga bitta (unikal xodim + kun); kechagi ochiq ro'yxat
/// yangisi bilan almashadi (bekor qilinadi — ish yangi ro'yxatda davom etadi); ro'yxatdagi hamma talab yopilsa — o'zi bajariladi.
/// Navbatda ish bo'lmasa topshiriq yaratilmaydi.
/// </summary>
public sealed partial class HrTasksService
{
    public const string ServiceGiverName = "Tizim (Service)";
    public const string ServiceAutoDoneComment = "Avtomatik: ro'yxatdagi barcha o'quvchilar bilan bog'lanildi (talablar yopildi).";
    public const string ServiceReplacedComment = "Yangi kunlik «Service» ro'yxati bilan almashtirildi — qolgan ish o'sha ro'yxatda.";
    public const int ServiceMaxLines = 60;
    private const int ServiceCloseWindowDays = 14;

    /// <summary>Bitta sikl: avval yopish, 09:00 dan keyin — bugungi ro'yxat. Qaytaradi: nechta o'zgardi.</summary>
    public async Task<int> RunServiceTasksAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        now = now.ToUniversalTime();
        var settings = await GetOrCreateSettingsAsync(ct);
        var tz = settings.Zone;
        var local = HrValues.Local(now, tz);
        var today = DateOnly.FromDateTime(local);
        var changed = await AutoCloseServiceTasksAsync(today, now, ct);

        var employeeId = settings.ServiceTaskEmployeeId;
        if (string.IsNullOrEmpty(employeeId) || TimeOnly.FromDateTime(local) < HrTrialRules.SendAt) return changed;
        var employee = await db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId && e.IsActive, ct);
        if (employee is null) return changed;
        if (await db.HrServiceTasks.AsNoTracking().AnyAsync(r => r.EmployeeId == employeeId && r.Day == today, ct)) return changed;

        // Bugungi ish: yangi (sanasiz) + qayta qo'ng'iroq sanasi bugun yoki o'tgan (navbatdagi «Bugun qilish kerak»).
        var todayIso = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var requests = (await db.ContactRequests.AsNoTracking()
                .Where(r => r.Status == ContactStatuses.New || r.Status == ContactStatuses.Callback)
                .ToListAsync(ct))
            .Where(r => r.Status == ContactStatuses.New || string.IsNullOrEmpty(r.DueDate) || string.CompareOrdinal(r.DueDate, todayIso) <= 0)
            .OrderBy(r => string.IsNullOrEmpty(r.DueDate) ? "9999" : r.DueDate, StringComparer.Ordinal)
            .ThenBy(r => r.CreatedAt, StringComparer.Ordinal)
            .ToList();
        if (requests.Count == 0) return changed;

        var studentIds = requests.Select(r => r.StudentId).Distinct().ToList();
        var phones = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Phone, s.ParentPhone }).ToDictionaryAsync(s => s.Id, ct);
        var lines = requests.Take(ServiceMaxLines).Select((r, i) =>
        {
            var p = phones.GetValueOrDefault(r.StudentId);
            var phone = p is null ? "" : !string.IsNullOrWhiteSpace(p.ParentPhone) ? p.ParentPhone : p.Phone ?? "";
            var due = r.Status == ContactStatuses.Callback && !string.IsNullOrEmpty(r.DueDate) && string.CompareOrdinal(r.DueDate, todayIso) < 0
                ? $" — qayta qo'ng'iroq {r.DueDate[8..10]}.{r.DueDate[5..7]} (kechikkan)"
                : r.Status == ContactStatuses.Callback ? " — qayta qo'ng'iroq bugun" : "";
            var reason = string.IsNullOrWhiteSpace(r.ReasonLabel) ? "" : $" · {r.ReasonLabel}";
            return $"{i + 1}. {r.StudentName}{(phone.Length > 0 ? " · " + HrTrialRules.Phone(phone) : "")}{reason}{due}";
        }).ToList();
        if (requests.Count > ServiceMaxLines) lines.Add($"… yana {requests.Count - ServiceMaxLines} ta — «Service» bo'limida.");
        lines.AddRange(["", "Har biriga «Service» bo'limida «Bog'lanildi» belgilang — hammasi yopilsa topshiriq o'zi bajariladi."]);

        // Kechagi (va undan oldingi) ochiq ro'yxatlar — yangisi bilan almashadi (ish bitta ro'yxatda davom etsin).
        var stale = await (from r in db.HrServiceTasks
                           join t in db.HrTasks on r.TaskId equals t.Id
                           where r.EmployeeId == employeeId && r.Day < today
                               && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned)
                           select t).ToListAsync(ct);
        foreach (var t in stale) t.Cancel(now, ServiceReplacedComment);

        var title = $"Bog'lanish kerak — {requests.Count} ta o'quvchi ({today:dd.MM})";
        var task = new HrTask
        {
            EmployeeId = employeeId,
            Source = HrTaskSources.Director,
            Title = title.Length <= HrLimits.TitleMax ? title : title[..HrLimits.TitleMax],
            Description = Clip(string.Join("\n", lines), HrLimits.DescriptionMax),
            Kind = HrTaskKinds.Check,
            ProofRequired = false,
            OverduePolicy = HrOverduePolicies.KeepOpen,
            PeriodStart = today,
            DueAt = DueInstant(HrTrialRules.DueLocal(local), tz),
            Status = HrTaskStatuses.Open,
            CreatedBy = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var link = new HrServiceTask
        {
            TaskId = task.Id, EmployeeId = employeeId, Day = today,
            RequestIds = string.Join(",", requests.Select(r => r.Id)), CreatedAt = now,
        };
        db.HrTasks.Add(task);
        db.HrServiceTasks.Add(link);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Unikal (xodim, kun): parallel sikl allaqachon yaratgan — ikkinchisi yo'q.
            if (db is DbContext ctx)
            {
                ctx.Entry(task).State = EntityState.Detached;
                ctx.Entry(link).State = EntityState.Detached;
                foreach (var t in stale) ctx.Entry(t).State = EntityState.Unchanged;
            }
            logger.LogInformation(ex, "HR Service: {Day} ro'yxati {Employee} uchun allaqachon bor", today, employeeId);
            return changed;
        }
        await NotifyAssignedAsync(employee.UserId, task, tz, ServiceGiverName, ct);
        return changed + 1 + stale.Count;
    }

    /// <summary>Ro'yxatdagi HAMMA talab yopilgan (yoki o'chirilgan) ochiq «Service» topshirig'i — bajarildi.</summary>
    private async Task<int> AutoCloseServiceTasksAsync(DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        var since = today.AddDays(-ServiceCloseWindowDays);
        var open = await (from r in db.HrServiceTasks
                          join t in db.HrTasks on r.TaskId equals t.Id
                          where r.Day >= since && (t.Status == HrTaskStatuses.Open || t.Status == HrTaskStatuses.Returned)
                          select new { Row = r, Task = t }).ToListAsync(ct);
        if (open.Count == 0) return 0;
        var ids = open.SelectMany(o => o.Row.RequestIdList).Distinct().ToList();
        var stillOpen = (await db.ContactRequests.AsNoTracking()
                .Where(r => ids.Contains(r.Id) && (r.Status == ContactStatuses.New || r.Status == ContactStatuses.Callback))
                .Select(r => r.Id).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var closed = 0;
        foreach (var o in open)
        {
            if (o.Row.RequestIdList.Any(stillOpen.Contains)) continue;
            o.Task.Complete(now, ServiceAutoDoneComment, 0);
            closed++;
        }
        if (closed > 0) await db.SaveChangesAsync(ct);
        return closed;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

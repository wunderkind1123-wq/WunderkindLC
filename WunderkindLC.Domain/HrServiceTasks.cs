namespace WunderkindLC.Domain;

/// <summary>
/// LC (egasi, 2026-11-10): «SERVICE» KUNLIK TOPSHIRIG'I — «Bog'lanish kerak» navbatidagi ochiq talablar ro'yxati har kuni
/// (09:00) sozlamada tanlangan BITTA xodimga (<see cref="HrSettings.ServiceTaskEmployeeId"/>) oddiy <c>HrTask</c> bo'lib
/// boradi. Bu qator topshiriqni TALABLARGA bog'laydi: ro'yxatdagi hammasi yopilsa topshiriq O'ZI bajariladi.
/// Unikal: (xodim, kun) — kuniga bitta ro'yxat. `.claude/rules/hr-tasks.md` §14.
/// </summary>
public class HrServiceTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>`hr_tasks.id`.</summary>
    public string TaskId { get; set; } = string.Empty;
    /// <summary>`hr_employees.id`.</summary>
    public string EmployeeId { get; set; } = string.Empty;
    /// <summary>Ro'yxat kuni (markaz vaqti).</summary>
    public DateOnly Day { get; set; }
    /// <summary>Ro'yxatdagi talablar (`ContactRequest.Id`), vergul bilan.</summary>
    public string RequestIds { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<string> RequestIdList =>
        [.. RequestIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}

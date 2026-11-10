namespace WunderkindLC.Application.Dtos;

/// <summary>
/// Lid (sotuv) topshirig'i — lid sahifasi uchun ko'rinish. Manba — HR topshirig'i (`HrTask` + `HrLeadTask`),
/// sanalar markaz vaqtida (mahalliy ISO "yyyy-MM-ddTHH:mm:ss").
/// </summary>
/// <param name="Status"><c>open</c> | <c>done</c> | <c>cancelled</c> (HR holatidan — <c>LeadTasks.StatusOf</c>).</param>
/// <param name="Bucket"><c>overdue</c> | <c>today</c> | <c>tomorrow</c> | <c>later</c> — faqat ochiq topshiriqda ma'noli.</param>
/// <param name="Accepted">Xodim Telegram'da «✅ Qabul qildim» bosganmi (yoki panelda tasdiqlagan).</param>
public record LeadTaskDto(
    string Id, string LeadId, string LeadName, string LeadPhone, string LeadStage,
    string Type, string TypeLabel, string Text, string DueAt, bool AllDay,
    string? ResponsibleUserId, string? ResponsibleName,
    string Status, string? Result, string? CancelReason,
    string CreatedAt, string CreatedByName, string? ClosedAt, string? ClosedByName,
    bool Overdue, int OverdueDays, string Bucket, bool Accepted);

/// <summary>Yangi topshiriq / tahrir. <c>DueAt</c> — "yyyy-MM-ddTHH:mm" yoki butun kun uchun "yyyy-MM-dd".</summary>
public record LeadTaskPayload(
    string? LeadId, string? Type, string? Text, string? DueAt, bool AllDay, string? ResponsibleUserId);

/// <summary>Bajarish: natija MAJBURIY; <c>Next</c> berilsa — o'sha tranzaksiyada keyingi topshiriq.</summary>
public record CompleteLeadTaskRequest(string? Result, LeadTaskPayload? Next);

public record CancelLeadTaskRequest(string? Reason);

/// <summary>Bajarish javobi: yopilgan topshiriq + (bo'lsa) yaratilgan keyingisi.</summary>
public record CompleteLeadTaskResult(LeadTaskDto Done, LeadTaskDto? Next);

public record LeadTaskTypeDto(string Key, string Label);

/// <summary>Mas'ul tanlovi uchun xodim (AppUser id + ism) — faqat «Topshiriqlar»dagi faol xodimlar.</summary>
public record LeadTaskPersonDto(string Id, string Name);

public record LeadTaskMetaDto(List<LeadTaskTypeDto> Types, List<LeadTaskPersonDto> People, string? MeId);

namespace WunderkindLC.Domain;

/* =====================================================================================
 *  LID (SOTUV) TOPSHIRIQLARI — LC qo'shimchasi, LMS'da YO'Q. Sotuv rejasi 1-qism (`docs/sales/PLAN.md`),
 *  qoidalar `.claude/rules/crm-leads.md` «LID TOPSHIRIQLARI».
 *
 *  Egasi (2026-11-10): «Vazifalar alohida bo'lmasin — Topshiriqlar bilan BITTA bo'lsin, sotuvnikilari esa
 *  boshqa rangda ajralib tursin». Shuning uchun lid vazifasi — oddiy `HrTask` (panel, «Mening topshiriqlarim»,
 *  Telegram «✅ Qabul qildim», eslatmalar, KPI — hammasi o'sha). Bu qator faqat uni LIDGA bog'laydi va lid
 *  sahifasi uchun qo'shimcha ma'lumotni (tur, asl matn, butun kun) saqlaydi. `HrTrialTask` bilan bir xil naqsh.
 * ===================================================================================== */

public static class HrLeadTaskTypes
{
    public const string Call = "call";
    public const string Meeting = "meeting";
    public const string Message = "message";
    public const string Other = "other";
    public static readonly string[] All = [Call, Meeting, Message, Other];
}

/// <summary>HR topshirig'i ↔ lid. Bitta topshiriq — bitta lid (unikal <see cref="TaskId"/>).</summary>
public class HrLeadTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>`hr_tasks.id`.</summary>
    public string TaskId { get; set; } = string.Empty;
    /// <summary><see cref="Lead"/>.Id (FK YO'Q: lid o'chirilsa ham topshiriq tarixi qoladi).</summary>
    public string LeadId { get; set; } = string.Empty;
    /// <summary><see cref="HrLeadTaskTypes"/>.</summary>
    public string Type { get; set; } = HrLeadTaskTypes.Call;
    /// <summary>Menejer yozgan asl matn (topshiriq sarlavhasi undan quriladi).</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>Butun kunlik (vaqtsiz) — muddat kun oxiri 23:59.</summary>
    public bool AllDay { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

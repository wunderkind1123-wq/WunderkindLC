namespace WunderkindLC.Domain;

/// <summary>
/// «SINOVGA QAYTARISH» yozuvi (2026-11-13, egasi: «sinovga qaytarilgan o'quvchi guruhda qolmasin va birinchi darsga o'tsin,
/// puli hisoblanmasin, tarixda qolsin»). O'quvchi guruhdan chiqqanda yoziladi va «Birinchi darsga yozilganlar» sahifasidagi
/// «Sinovdan qaytganlar» ro'yxatini quradi. Guruhga qayta qo'shilsa, arxivlansa yoki qo'lda yopilsa — <see cref="ResolvedAt"/>.
/// Guruh nomi, sabab va kim — SNAPSHOT (guruh o'chirilsa/sabab o'zgarsa ham o'qiladi). Qoidalar — <c>TrialReturnService</c>,
/// <c>.claude/rules/membership-periods.md</c> §12.
/// </summary>
public class StudentTrialReturn
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string StudentId { get; set; } = string.Empty;
    /// <summary>Qaysi guruhdan qaytarildi (<see cref="Group"/>.Id).</summary>
    public string GroupId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string? ReasonId { get; set; }
    public string Reason { get; set; } = string.Empty;
    /// <summary>Qaytarilgan vaqt (ISO, markaz vaqti).</summary>
    public string CreatedAt { get; set; } = string.Empty;
    public string? CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    /// <summary>Joriy oy va keyingi oylardan bekor qilingan hisob (so'm) — balansga qaytgan.</summary>
    public decimal CanceledAmount { get; set; }
    /// <summary>Ro'yxatdan chiqqan vaqt (guruhga qo'shildi / arxivlandi / qo'lda). null — ochiq («Birinchi dars»da).</summary>
    public string? ResolvedAt { get; set; }
    public string ResolvedNote { get; set; } = string.Empty;
}

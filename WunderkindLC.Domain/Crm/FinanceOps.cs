namespace WunderkindLC.Domain;

// Owner backlog 2026-10-17 (docs/SPEC-backlog-2026-10-17.md F3): expense approval and the payroll register.
// Model config: WunderkindLC.Infrastructure/Data/AppDbContext.CrmFinanceOps.cs.

public partial class CenterMeta
{
    /// <summary>CHIQIM TASDIG'I chegarasi (so'm): shundan KATTA qo'lda kiritilgan chiqim (maosh to'lovi ham) darhol
    /// kassadan chiqmaydi — <see cref="ExpenseRequest"/> bo'lib tasdiq kutadi. 0 — o'chiq (egasining qarori: faqat
    /// katta summalar tasdiqlanadi).</summary>
    public decimal ExpenseApprovalThreshold { get; set; }
}

/// <summary>
/// TASDIQ KUTAYOTGAN CHIQIM. ⚠️ Bu <see cref="FinanceTransaction"/> EMAS — pul hali harakatlanmagan, shuning uchun
/// hech bir hisobot/qoldiq/maosh uni o'qimaydi (25+ o'quvchi joyga istisno kerak bo'lmasin — kassadan kassaga
/// ko'chirish naqshi). Tasdiqlanganda AYNAN shu maydonlardan oddiy tranzaksiya yoziladi.
/// </summary>
public class ExpenseRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary><c>pending</c> · <c>approved</c> · <c>rejected</c> · <c>cancelled</c> (kiritgan o'zi qaytarib oldi).</summary>
    public string Status { get; set; } = "pending";

    /* ---- chiqimning o'zi (FinanceTransaction maydonlari) ---- */
    public string Date { get; set; } = string.Empty;
    public string Category { get; set; } = "other";
    public string? TypeId { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public string? Comment { get; set; }
    public string? TeacherId { get; set; }
    public string? StaffUserId { get; set; }
    public string? Month { get; set; }
    public string? Method { get; set; }
    public string? CashboxId { get; set; }
    public string? SalaryPayKind { get; set; }

    /* ---- kim / qachon ---- */
    public string CreatedAt { get; set; } = AppClock.Iso();
    public string? CreatedById { get; set; }
    public string? CreatedBy { get; set; }
    public string? DecidedAt { get; set; }
    public string? DecidedById { get; set; }
    public string? DecidedBy { get; set; }
    /// <summary>Rad etish sababi (majburiy) yoki tasdiqdagi izoh.</summary>
    public string? DecisionNote { get; set; }
    /// <summary>Tasdiqlanganda yozilgan tranzaksiya.</summary>
    public string? TransactionId { get; set; }
}

/// <summary>
/// XODIM OYLIGI (o'qituvchi emas — administrator, kassir, operator): qat'iy oylik summa, <see cref="EffectiveFrom"/>
/// oyidan boshlab (versiyalanadi — <see cref="KpiProfileSalary"/> naqshi, o'tgan oylar o'zgarmaydi). KPI profili bor
/// xodimning oyligi KPI'dan olinadi (oklad + bonus − jarima, tasdiqlangan oy — <see cref="KpiMonthResult"/>).
/// </summary>
public class StaffSalary
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    /// <summary>Shu OYDAN boshlab ("yyyy-MM").</summary>
    public string EffectiveFrom { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public string CreatedAt { get; set; } = AppClock.Iso();
}

/// <summary>Xodim oyligiga QO'LDA bonus / jarima turlari.</summary>
public static class StaffSalaryAdjustmentKinds
{
    public const string Bonus = "bonus";
    public const string Fine = "fine";
    public static bool IsValid(string? v) => v is Bonus or Fine;
}

/// <summary>
/// XODIM OYLIGIGA QO'LDA BONUS / JARIMA (egasi, 2026-11-10: «fiks summa, pastda jarima qancha, bonus qancha — hisobotda
/// ko'rinsin»). Bir oyda bir nechta bo'lishi mumkin, har biri SABABI bilan. Hisoblangan maosh =
/// asos (qat'iy oylik yoki KPI natijasi) + bonuslar − jarimalar (<c>PayrollRegister</c>). Faqat panel xodimlari
/// (o'qituvchilarda o'z maosh hisobi va jarimalari bor). Ko'rinish — faqat Moliya (`finance.main`).
/// </summary>
public class StaffSalaryAdjustment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    /// <summary>Qaysi oy maoshiga ("yyyy-MM").</summary>
    public string Month { get; set; } = string.Empty;
    /// <summary><see cref="StaffSalaryAdjustmentKinds"/>.</summary>
    public string Kind { get; set; } = StaffSalaryAdjustmentKinds.Bonus;
    /// <summary>Har doim musbat; ishora <see cref="Kind"/> dan.</summary>
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public string CreatedAt { get; set; } = AppClock.Iso();
}

namespace WunderkindLC.Domain;

/// <summary>
/// HISOBOT TOIFASI (Pul oqimi 2.0 · P&amp;L 2.0, <c>docs/finance-reports/</c>): har kirim/chiqim «qatori» pul oqimining
/// qaysi faoliyatiga va P&amp;L ning qaysi qismiga tushishi. Qator kaliti (<see cref="Key"/>):
/// <c>type:{TransactionType.Id}</c> — markaz turi; turi yo'q eski amallar uchun <c>cat:{direction}:{category}</c>
/// (masalan <c>cat:income:tuition</c>). ⚠️ Toifa kalitida YO'NALISH bor: <c>other</c>/<c>book</c> kodlari kirimda
/// ham, chiqimda ham uchraydi.
///
/// <para>⚠️ Bu FAQAT hisobot qatlami — mavjud hisob-kitob (balans, maosh foizi, kassa) <c>Category</c> bo'yicha
/// avvalgidek ishlaydi va bu jadvalni O'QIMAYDI.</para>
/// </summary>
public class FinanceCategoryConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Unikal qator kaliti — <c>type:…</c> yoki <c>cat:{direction}:{category}</c>.</summary>
    public string Key { get; set; } = string.Empty;
    public string? TypeId { get; set; }
    public string? SystemCategory { get; set; }
    /// <summary><c>income</c> | <c>expense</c>.</summary>
    public string Direction { get; set; } = "income";
    /// <summary>Hisobotda ko'rinadigan nom (bo'sh — tur nomi / toifa yorlig'i).</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary><c>operating</c> | <c>investing</c> | <c>financing</c> | <c>excluded</c>.</summary>
    public string CashFlowActivity { get; set; } = "operating";
    /// <summary>
    /// <c>tuitionRevenue</c> | <c>otherRevenue</c> | <c>otherRevenueContra</c> | <c>expense</c> | <c>salary</c> |
    /// <c>salaryAdvance</c> | <c>revenueContra</c> | <c>nonCash</c> | <c>excluded</c>.
    /// </summary>
    public string PnlTreatment { get; set; } = "expense";
    /// <summary>P&amp;L xarajat guruhi (bo'sh — qator nomi).</summary>
    public string? PnlGroup { get; set; }
    public int SortOrder { get; set; }
    /// <summary>Qoida bilan avtomatik qo'yilganmi (qo'lda saqlangach — false, qayta tasniflash unga tegmaydi).</summary>
    public bool IsAutoClassified { get; set; } = true;
    public string AutoReason { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// REJALI XARAJAT SHABLONI (P&amp;L 2.0): oylik takrorlanadigan xarajat (ijara, kommunal, soliq). Faol shablon har oyning
/// 1-kunida TO'LIQ summada P&amp;L ga hisoblanadi — to'langan-to'lanmaganidan qat'i nazar. Shu turdagi haqiqiy chiqimlar
/// shablonning «to'langan» qismi bo'ladi (xarajatga ikkinchi marta qo'shilmaydi).
///
/// <para>⚠️ Eski <see cref="PlannedExpense"/> (bir martalik reja, «to'landi» holati bilan) bilan ARALASHTIRMANG — u
/// alohida sahifa va bu jadvalga tegmaydi.</para>
/// </summary>
public class PlannedExpenseTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    /// <summary>Markaz chiqim turi (<see cref="TransactionType"/>); bo'sh bo'lsa — <see cref="Category"/>.</summary>
    public string? TypeId { get; set; }
    /// <summary>Tizim chiqim toifasi (turi yo'q shablon uchun, masalan <c>rent</c>).</summary>
    public string Category { get; set; } = "other";
    public decimal Amount { get; set; }
    /// <summary>Faqat <c>monthly</c>.</summary>
    public string Frequency { get; set; } = "monthly";
    /// <summary>"yyyy-MM-dd".</summary>
    public string StartDate { get; set; } = string.Empty;
    /// <summary>"yyyy-MM-dd" yoki bo'sh (muddatsiz).</summary>
    public string EndDate { get; set; } = string.Empty;
    /// <summary><c>active</c> | <c>inactive</c>.</summary>
    public string Status { get; set; } = "active";
    /// <summary>Oyning eslatma kuni (1–28), ixtiyoriy.</summary>
    public int? ReminderDay { get; set; }
    public string? Note { get; set; }
    public bool IsDeleted { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Shablonning OYLIK hisobi (P&amp;L 2.0) — oy boshida yoziladi va keyin O'ZGARMAYDI: shablon summasi keyin tahrirlansa
/// o'tgan oylar qayta yozilmasin. «Tuzatish» — hisob-faktura bo'yicha haqiqiy summa (<see cref="CorrectedAmount"/>).
/// Unikal (TemplateId, Month).
/// </summary>
public class PlannedExpenseAccrual
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TemplateId { get; set; } = string.Empty;
    /// <summary>"yyyy-MM".</summary>
    public string Month { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? CorrectedAmount { get; set; }
    public string? CorrectionNote { get; set; }
    public string? CorrectedBy { get; set; }
    public string? CorrectedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

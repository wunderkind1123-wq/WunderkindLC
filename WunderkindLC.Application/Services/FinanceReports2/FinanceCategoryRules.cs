using System.Text;

namespace WunderkindLC.Application.Services.FinanceReports2;

/// <summary>
/// HISOBOT TOIFALARI — avtomatik tasniflash (Pul oqimi 2.0 · P&amp;L 2.0, <c>docs/finance-reports/DECISIONS.md</c>).
/// Sof funksiyalar, <c>FinanceCategoryRulesTests</c> bilan qoplangan.
///
/// <para>Qoidalar YUQORIDAN PASTGA, birinchi mos kelgani g'olib (spetsifikatsiya §3.2, bizning ma'lumotga moslab):
/// tur BO'LIMI (<c>vaucher</c>/<c>jarima</c>) va tizim toifasi nom bo'yicha qidiruvdan oldin turadi.</para>
/// </summary>
public static class FinanceCategoryRules
{
    // ---- Pul oqimi faoliyati ----
    public const string Operating = "operating";
    public const string Investing = "investing";
    public const string Financing = "financing";
    public const string ExcludedActivity = "excluded";
    public static readonly IReadOnlyList<string> Activities = [Operating, Investing, Financing, ExcludedActivity];

    // ---- P&L dagi o'rni ----
    public const string TuitionRevenue = "tuitionRevenue";
    public const string OtherRevenue = "otherRevenue";
    /// <summary>Boshqa daromadni qaytarish (kitob qaytarishda berilgan pul) — «Boshqa daromad» dan ayiriladi.</summary>
    public const string OtherRevenueContra = "otherRevenueContra";
    public const string Expense = "expense";
    public const string Salary = "salary";
    public const string SalaryAdvance = "salaryAdvance";
    public const string RevenueContra = "revenueContra";
    public const string NonCash = "nonCash";
    public const string Excluded = "excluded";
    public static readonly IReadOnlyList<string> Treatments =
        [TuitionRevenue, OtherRevenue, OtherRevenueContra, Expense, Salary, SalaryAdvance, RevenueContra, NonCash, Excluded];

    /// <summary>Qaysi yo'nalishda qaysi P&amp;L o'rni ma'noli (sozlamada noto'g'ri juftlik 400).</summary>
    public static bool TreatmentFits(string direction, string treatment) => direction == "income"
        ? treatment is TuitionRevenue or OtherRevenue or NonCash or Excluded
        : treatment is OtherRevenueContra or Expense or Salary or SalaryAdvance or RevenueContra or NonCash or Excluded;

    public readonly record struct Input(string Name, string Direction, string? Kind, string Category);
    public readonly record struct Result(string Activity, string Treatment, string Reason);

    private static readonly string[] TuitionWords = ["oylik to'lov", "o'quvchi to'lovi", "abonement"];
    private static readonly string[] NonCashWords = ["vaucher", "voucher", "chegirma", "bonus", "jarima", "coin", "ваучер", "скидка", "штраф"];
    private static readonly string[] ErrorWords = ["adashib", "xato", "noto'g'ri kiritil", "balansdan chiqarish", "korrektirovka", "корректировка"];
    private static readonly string[] RefundWords = ["qaytarish", "vozvrat", "возврат"];
    private static readonly string[] AdvanceWords = ["avans", "аванс"];
    private static readonly string[] SalaryWords = ["oylik", "ish haqi", "maosh", "зарплата"];
    private static readonly string[] FinancingWords = ["kredit", "qarz olindi", "qarz qaytar", "ta'sischi", "dividend", "investor", "ustav", "кредит"];
    private static readonly string[] InvestingWords =
        ["jihoz", "mebel", "kompyuter", "noutbuk", "konditsioner", "remont", "ta'mir", "qurilish", "avtomobil", "bino sotib", "asosiy vosita"];

    /// <summary>
    /// Kichik harf, apostrof variantlari (<c>' ’ ʻ ʼ `</c>) bitta <c>'</c> ga, ortiqcha bo'shliq bittaga.
    /// Kirill harflari o'zgarmaydi (kalit so'zlar ro'yxatida kirill variantlari alohida).
    /// </summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        var space = false;
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            var c = ch is '’' or 'ʻ' or 'ʼ' or '`' or '‘' or 'ʹ' ? '\'' : ch;
            if (char.IsWhiteSpace(c))
            {
                if (!space) sb.Append(' ');
                space = true;
                continue;
            }
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string? Hit(string name, IEnumerable<string> words) => words.FirstOrDefault(name.Contains);

    public static Result Classify(Input x)
    {
        var name = Normalize(x.Name);
        var income = x.Direction == "income";
        var cat = x.Category ?? "";

        // 1. O'quvchi o'qish to'lovi.
        if (income && cat == "tuition") return new(Operating, TuitionRevenue, "Qoida #1: toifa «tuition»");
        if (income && Hit(name, TuitionWords) is { } t1) return new(Operating, TuitionRevenue, $"Qoida #1: '{t1}'");

        // 2. Pul harakati emas (vaucher/bonus/jarima/chegirma). ⚠️ Pul oqimi faoliyati — OPERATSION: bu CRM'da har amal
        //    kassa qoldig'ini o'zgartiradi, «excluded» qilinsa pul oqimi kassa qoldig'idan farq qilardi (DECISIONS D6).
        if (x.Kind is "vaucher" or "jarima") return new(Operating, NonCash, $"Qoida #2: tur bo'limi «{x.Kind}»");
        if (cat == "penalty") return new(Operating, NonCash, "Qoida #2: toifa «penalty» (jarima)");
        if (Hit(name, NonCashWords) is { } t2) return new(Operating, NonCash, $"Qoida #2: '{t2}'");

        // 3. Xato kiritilganni tuzatish — P&L ga kirmaydi.
        if (Hit(name, ErrorWords) is { } t3) return new(Operating, Excluded, $"Qoida #3: '{t3}'");

        // 4. Vozvrat (o'qish to'lovini qaytarish) va kitob qaytarish.
        if (!income && cat == "refund") return new(Operating, RevenueContra, "Qoida #4: toifa «refund»");
        if (!income && cat == "book") return new(Operating, OtherRevenueContra, "Qoida #4: kitob qaytarish (toifa «book»)");
        // ⚠️ Moliyalashtirish so'zlari nomdagi «qaytarish» dan USTUN: «Kredit qaytarish» — vozvrat emas.
        if (Hit(name, FinancingWords) is { } t7a) return new(Financing, Excluded, $"Qoida #7: '{t7a}'");
        if (!income && Hit(name, RefundWords) is { } t4) return new(Operating, RevenueContra, $"Qoida #4: '{t4}'");

        // 5. Avans (maosh hisobiga) — P&L ga kirmaydi, pul oqimida bor.
        if (!income && Hit(name, AdvanceWords) is { } t5) return new(Operating, SalaryAdvance, $"Qoida #5: '{t5}'");

        // 6. Ish haqi.
        if (!income && cat == "salary") return new(Operating, Salary, "Qoida #6: toifa «salary»");
        if (!income && Hit(name, SalaryWords) is { } t6) return new(Operating, Salary, $"Qoida #6: '{t6}'");

        // 7. Moliyalashtirish.
        if (Hit(name, FinancingWords) is { } t7) return new(Financing, Excluded, $"Qoida #7: '{t7}'");

        // 8. Investitsiya (asosiy vositalar) — P&L da ham xarajat (amortizatsiya yo'q).
        if (!income && Hit(name, InvestingWords) is { } t8) return new(Investing, Expense, $"Qoida #8: '{t8}'");
        if (!income && cat is "supplies" or "repair") return new(Investing, Expense, $"Qoida #8: toifa «{cat}»");

        // 9–10. Qolganlari.
        return income
            ? new(Operating, OtherRevenue, "Qoida #9: boshqa kirim")
            : new(Operating, Expense, "Qoida #10: boshqa chiqim");
    }

    // ---- Qator kalitlari ----

    public static string TypeKey(string typeId) => "type:" + typeId;
    public static string CategoryKey(string direction, string category) => $"cat:{direction}:{category}";

    /// <summary>Amalning qator kaliti: turi bo'lsa — tur, aks holda yo'nalish + toifa.</summary>
    public static string KeyOf(string? typeId, string direction, string category) =>
        !string.IsNullOrEmpty(typeId) ? TypeKey(typeId) : CategoryKey(direction, category);

    /// <summary>Tizim toifasining o'zbekcha yorlig'i (klientdagi <c>financeCategoryLabel</c> bilan bir xil).</summary>
    public static string CategoryLabel(string category, string direction) => (category, direction) switch
    {
        ("tuition", _) => "O'quvchi to'lovi",
        ("refund", _) => "Vozvrat (to'lovni qaytarish)",
        ("book", "expense") => "Kitob qaytarish",
        ("book", _) => "Kitob/qo'llanma sotuvi",
        ("mock", _) => "Mock imtihon to'lovi",
        ("donation", _) => "Homiylik",
        ("rent_in", _) => "Ijaradan kirim",
        ("salary", _) => "Oylik maosh",
        ("utilities", _) => "Kommunal",
        ("internet", _) => "Internet",
        ("stationery", _) => "Kantselyariya",
        ("supplies", _) => "Jihoz/materiallar",
        ("rent", _) => "Ijara",
        ("repair", _) => "Ta'mirlash",
        ("penalty", _) => "Jarima",
        ("other", "expense") => "Boshqa chiqim",
        ("other", _) => "Boshqa kirim",
        _ => category,
    };
}

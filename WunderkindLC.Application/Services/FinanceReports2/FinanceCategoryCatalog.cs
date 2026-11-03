using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.FinanceReports2;

/// <summary>
/// Hisobot toifalari katalogi: har qator kaliti uchun sozlama (<see cref="FinanceCategoryConfig"/>). Yangi tur yoki
/// turi yo'q yangi toifa paydo bo'lsa — <see cref="EnsureAsync"/> uni <see cref="FinanceCategoryRules.Classify"/> bilan
/// avtomatik qo'shadi (idempotent; qo'lda saqlangan yozuvga TEGMAYDI).
/// </summary>
public static class FinanceCategoryCatalog
{
    /// <summary>Hisobotlar ishlatadigan qator sozlamasi.</summary>
    public sealed record Entry(
        string Key, string Name, string Direction, string Activity, string Treatment, string Group, int SortOrder,
        string? TypeId, string? Category, string? Kind = null)
    {
        /// <summary>O'quvchi chegirmasi sifatida sanaladigan «pul harakati emas» chiqim (vaucher, «chegirma» turi) — o'qituvchi
        /// JARIMASI emas (u ham <c>nonCash</c>, lekin o'quvchi kutuviga aloqasi yo'q).</summary>
        public bool IsStudentDiscount => Treatment == FinanceCategoryRules.NonCash && Direction == "expense"
            && Kind != "jarima" && Category != "penalty";
    }

    public sealed class Catalog(Dictionary<string, Entry> entries, Dictionary<string, (string Name, string Kind, string Category)> types)
    {
        public IReadOnlyDictionary<string, Entry> Entries => entries;

        /// <summary>Amal → sozlama. Katalogda bo'lmasa (poyga: EnsureAsync dan keyin yaratilgan tur) — qoida bilan joyida.</summary>
        public Entry Resolve(string? typeId, string direction, string category)
        {
            var key = FinanceCategoryRules.KeyOf(typeId, direction, category);
            if (entries.TryGetValue(key, out var e)) return e;
            var t = typeId is not null && types.TryGetValue(typeId, out var tt) ? tt : default;
            var name = t.Name ?? FinanceCategoryRules.CategoryLabel(category, direction);
            var r = FinanceCategoryRules.Classify(new(name, direction, t.Kind, category));
            e = new Entry(key, name, direction, r.Activity, r.Treatment, name, 1000, typeId, category, t.Kind);
            entries[key] = e;
            return e;
        }
    }

    public static async Task<Catalog> LoadAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var types = (await db.TransactionTypes.AsNoTracking()
                .Select(t => new { t.Id, t.Name, t.Kind, t.BaseCategory }).ToListAsync(ct))
            .ToDictionary(t => t.Id, t => (t.Name, t.Kind, t.BaseCategory));
        var configs = await db.FinanceCategoryConfigs.AsNoTracking().ToListAsync(ct);
        var dict = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var c in configs)
        {
            var baseName = c.TypeId is not null && types.TryGetValue(c.TypeId, out var t)
                ? t.Name
                : FinanceCategoryRules.CategoryLabel(c.SystemCategory ?? "", c.Direction);
            var name = string.IsNullOrWhiteSpace(c.DisplayName) ? baseName : c.DisplayName.Trim();
            var group = string.IsNullOrWhiteSpace(c.PnlGroup) ? name : c.PnlGroup.Trim();
            dict[c.Key] = new Entry(c.Key, name, c.Direction, c.CashFlowActivity, c.PnlTreatment, group, c.SortOrder,
                c.TypeId, c.SystemCategory ?? (c.TypeId is not null && types.TryGetValue(c.TypeId, out var t2) ? t2.BaseCategory : null),
                c.TypeId is not null && types.TryGetValue(c.TypeId, out var t3) ? t3.Kind : null);
        }
        return new Catalog(dict, types);
    }

    /// <summary>Katalogda bo'lishi kerak bo'lgan qatorlar: BARCHA markaz turlari + turi yo'q amallarning (yo'nalish, toifa)
    /// juftliklari (bekor qilinganlar global filtr bilan tushadi).</summary>
    private static async Task<List<(string Key, string? TypeId, string? Category, string Direction, string Name, string? Kind, string BaseCategory)>>
        CandidatesAsync(IAppDbContext db, CancellationToken ct)
    {
        var list = new List<(string, string?, string?, string, string, string?, string)>();
        foreach (var t in await db.TransactionTypes.AsNoTracking().ToListAsync(ct))
            list.Add((FinanceCategoryRules.TypeKey(t.Id), t.Id, null, t.Direction, t.Name, t.Kind, t.BaseCategory));
        var typeless = await db.FinanceTransactions.AsNoTracking()
            .Where(t => t.TypeId == null)
            .Select(t => new { t.Direction, t.Category }).Distinct().ToListAsync(ct);
        foreach (var c in typeless)
        {
            var dir = c.Direction == "expense" ? "expense" : "income";
            var cat = string.IsNullOrWhiteSpace(c.Category) ? "other" : c.Category;
            list.Add((FinanceCategoryRules.CategoryKey(dir, cat), null, cat, dir, FinanceCategoryRules.CategoryLabel(cat, dir), null, cat));
        }
        return list;
    }

    /// <summary>Yetishmagan sozlamalarni qoida bilan yaratadi. Qaytadi: nechta yaratildi.</summary>
    public static async Task<int> EnsureAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var existing = (await db.FinanceCategoryConfigs.AsNoTracking().Select(c => c.Key).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var created = 0;
        var order = existing.Count;
        foreach (var c in await CandidatesAsync(db, ct))
        {
            if (!existing.Add(c.Key)) continue;
            var r = FinanceCategoryRules.Classify(new(c.Name, c.Direction, c.Kind, c.BaseCategory));
            db.FinanceCategoryConfigs.Add(new FinanceCategoryConfig
            {
                Key = c.Key, TypeId = c.TypeId, SystemCategory = c.Category, Direction = c.Direction,
                CashFlowActivity = r.Activity, PnlTreatment = r.Treatment, SortOrder = (++order) * 10,
                IsAutoClassified = true, AutoReason = r.Reason, UpdatedAt = AppClock.Now.ToString("o"),
            });
            created++;
        }
        if (created > 0)
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // Parallel so'rov AYNI kalitni yaratgan (unikal indeks) — qatorlarimizni ajratamiz, keyingi chaqiruv to'ldiradi.
                foreach (var e in db.FinanceCategoryConfigs.Local.Where(x => db.FinanceCategoryConfigs.Entry(x).State == EntityState.Added).ToList())
                    db.FinanceCategoryConfigs.Entry(e).State = EntityState.Detached;
                return 0;
            }
        }
        return created;
    }

    /// <summary>«Qayta avtomatik tasniflash» — FAQAT <c>IsAutoClassified</c> yozuvlarga (qo'lda saqlanganlar tegilmaydi).
    /// Saqlamaydi. Qaytadi: o'zgargan yozuvlar soni.</summary>
    public static async Task<int> ReclassifyAutoAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var types = (await db.TransactionTypes.AsNoTracking().ToListAsync(ct)).ToDictionary(t => t.Id);
        var changed = 0;
        foreach (var c in await db.FinanceCategoryConfigs.Where(c => c.IsAutoClassified).ToListAsync(ct))
        {
            var t = c.TypeId is not null && types.TryGetValue(c.TypeId, out var tt) ? tt : null;
            var name = t?.Name ?? FinanceCategoryRules.CategoryLabel(c.SystemCategory ?? "", c.Direction);
            var r = FinanceCategoryRules.Classify(new(name, c.Direction, t?.Kind, t?.BaseCategory ?? c.SystemCategory ?? ""));
            if (c.CashFlowActivity == r.Activity && c.PnlTreatment == r.Treatment && c.AutoReason == r.Reason) continue;
            c.CashFlowActivity = r.Activity;
            c.PnlTreatment = r.Treatment;
            c.AutoReason = r.Reason;
            c.UpdatedAt = AppClock.Now.ToString("o");
            changed++;
        }
        return changed;
    }
}

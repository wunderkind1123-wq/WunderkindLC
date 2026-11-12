using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Services.Accounts;

namespace WunderkindLC.Application.Services;

/// <summary>
/// CRM'DAN KIRITILADIGAN TIZIM KALITLARI (egasining talabi, 2026-11-04): Instagram/Meta kalitlarini serverdagi
/// <c>.env</c> ni tahrirlamasdan, Marketing → Instagram → Sozlamalar sahifasidan kiritish.
///
/// <para><b>Xavfsizlik</b> — <see cref="AppSecrets"/> dagi "kalit bazada ochiq turmasin" qoidasi SAQLANADI:
/// qiymat bazaga FAQAT <see cref="CredentialVault"/> bilan shifrlangan holda yoziladi (shifr kaliti
/// <c>CREDENTIAL_VAULT_KEY</c> faqat <c>.env</c> da), zaxiraga kirmaydi (<c>BackupService.ExcludedTables</c>),
/// MCP roli o'qiy olmaydi (<c>McpReadOnlyGrants.Forbidden</c>), API/audit/log'ga qiymati tushmaydi.</para>
///
/// <para><b>Ustunlik:</b> <c>.env</c> dagi qiymat HAR DOIM ustun (<see cref="AppSecrets.SourceOf"/>) — mavjud
/// o'rnatmalar o'zgarmaydi. Seyf kaliti yo'q bo'lsa kiritish O'CHIQ (fail-closed, ochiq saqlash varianti YO'Q).</para>
/// </summary>
public static class StoredSecrets
{
    /// <summary>CRM'dan kiritiladigan kalit tavsifi.</summary>
    /// <param name="EnvName">`.env` nomi — bazadagi kalit ham shu.</param>
    /// <param name="Label">Sahifadagi nom.</param>
    /// <param name="IsAppSecret">Meta App Secret (32 belgili hex) — aks holda verify token (o'zimiz o'ylab topamiz).</param>
    /// <param name="Hint">Qayerdan olinadi.</param>
    /// <param name="Section">Qaysi sahifada ko'rsatiladi: <c>instagram</c> (Marketing → Instagram → Sozlamalar) yoki
    /// <c>gemini</c> (Sozlamalar → AI Tahlil).</param>
    public sealed record KeyInfo(string EnvName, string Label, bool IsAppSecret, string Hint, string Section = SectionInstagram);

    public const string SectionInstagram = "instagram";
    public const string SectionGemini = "gemini";

    /// <summary>Ruxsat etilgan kalitlar — FAQAT shular. Telegram kabi qolganlari avvalgidek faqat `.env` da.
    /// Gemini — 2026-11-13 dan (egasi: «Gemini API kalitini sozlamalardan kiritsam bo'ladigan qilib ber»).</summary>
    public static readonly IReadOnlyList<KeyInfo> Keys =
    [
        new(AppSecrets.EnvKeys.InstagramAppSecret, "Instagram App Secret", true,
            "Meta → ilova → Instagram → API setup with Instagram login → «Instagram app secret» (Show)"),
        new(AppSecrets.EnvKeys.InstagramVerifyToken, "Instagram Verify token", false,
            "O'zingiz o'ylab topasiz; Meta → Configure webhooks → «Verify token» ga AYNAN shuni yozasiz"),
        new(AppSecrets.EnvKeys.MetaAppSecret, "Reklama lidlari: Meta App Secret", true,
            "Faqat reklama boshqa Meta ilovasida bo'lsa. Bo'sh — Instagram kaliti ishlatiladi"),
        new(AppSecrets.EnvKeys.MetaVerifyToken, "Reklama lidlari: Verify token", false,
            "Faqat reklama boshqa Meta ilovasida bo'lsa. Bo'sh — Instagram tokeni ishlatiladi"),
        new(AppSecrets.EnvKeys.GeminiApiKey, "Gemini API kaliti", false,
            "Google AI Studio → Get API key (aistudio.google.com/apikey) → «Create API key»", SectionGemini),
    ];

    public static bool IsAllowed(string? envName) => envName is not null && Keys.Any(k => k.EnvName == envName);

    public static KeyInfo? Find(string? envName) => Keys.FirstOrDefault(k => k.EnvName == envName);

    private static readonly Regex Hex32 = new("^[0-9a-fA-F]{32}$", RegexOptions.Compiled);
    private static readonly Regex TokenChars = new(@"^[A-Za-z0-9_\-.~]{8,128}$", RegexOptions.Compiled);
    /// <summary>Gemini kaliti: eski «AIza…» (39 belgi) VA yangi AI Studio kalitlari («AQ.Ab8…», nuqtali) — shuning uchun
    /// faqat «bo'sh joysiz, ko'rinadigan ASCII, 20–500 belgi» (2026-11-13: nuqtali yangi kalit rad etilgan edi).</summary>
    private static readonly Regex GeminiKey = new(@"^[!-~]{20,500}$", RegexOptions.Compiled);

    /// <summary>
    /// Kiritilgan qiymatni tekshiradi (bo'sh qiymat — "o'chirish", xato emas). Xato bo'lsa o'zbekcha sabab.
    /// <para>App Secret — AYNAN 32 belgili hex (Meta shunday beradi): noto'g'ri nusxa olingan (bo'sh joy, qisqa) yoki
    /// «App ID» kiritilgan holat shu yerda ushlanadi — aks holda webhook imzosi jimgina mos kelmay qolardi.</para>
    /// </summary>
    public static string? Validate(string envName, string? value)
    {
        var info = Find(envName);
        if (info is null) return "Bu kalitni CRM'dan kiritib bo'lmaydi.";
        var v = (value ?? "").Trim();
        if (v.Length == 0) return null;
        if (envName == AppSecrets.EnvKeys.GeminiApiKey)
            return GeminiKey.IsMatch(v)
                ? null
                : "Gemini API kaliti noto'g'ri ko'rinishda: odatda «AIza…» yoki «AQ.…» ko'rinishida, bo'sh joy va qator ko'chishisiz. "
                  + "Google AI Studio'dan to'liq ko'chirib oling.";
        if (info.IsAppSecret)
            return Hex32.IsMatch(v)
                ? null
                : "App Secret 32 ta belgidan iborat bo'lishi kerak (0–9 va a–f). Meta'da «Show» bosib, to'liq ko'chirib oling — "
                  + "«App ID» (faqat raqamlar) bu yerga kirmaydi.";
        return TokenChars.IsMatch(v)
            ? null
            : "Verify token 8–128 belgi bo'lsin: lotin harflari, raqamlar va _ - . ~ (bo'sh joysiz).";
    }

    /// <summary>
    /// Ishga tushganda: bazadagi kalitlarni ochib xotiraga qo'yadi. Seyf kaliti yo'q / boshqa kalit bilan
    /// shifrlangan / buzuq qator — o'tkazib yuboriladi (qiymat ochilmaydi, xato tashlanmaydi).
    /// </summary>
    /// <returns>Yuklangan va ochilmagan qatorlar soni.</returns>
    public static async Task<(int Loaded, int Unreadable)> LoadAsync(IAppDbContext db, CredentialVault vault, CancellationToken ct = default)
    {
        var rows = await db.StoredSecrets.AsNoTracking().ToListAsync(ct);
        int loaded = 0, bad = 0;
        foreach (var r in rows)
        {
            if (!IsAllowed(r.Key)) continue;
            var plain = vault.Unprotect(r.Cipher);
            if (string.IsNullOrEmpty(plain)) { bad++; AppSecrets.SetStored(r.Key, null); continue; }
            AppSecrets.SetStored(r.Key, plain);
            loaded++;
        }
        return (loaded, bad);
    }

    /// <summary>Saqlash MUVAFFAQIYATLI bo'lgach xotiradagi qiymatni yangilaydi (bo'sh — olib tashlaydi).</summary>
    public static void Apply(string envName, string? value) => AppSecrets.SetStored(envName, value);
}

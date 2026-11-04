namespace WunderkindLC.Domain;

/// <summary>
/// CRM'dan kiritilgan TIZIM KALITI — faqat <b>shifrlangan</b> holda (<c>CredentialVault</c>, AES-256-GCM; kaliti
/// FAQAT <c>.env</c> da — <c>CREDENTIAL_VAULT_KEY</c>). Egasining talabi (2026-11-04): Instagram/Meta kalitlarini
/// serverga kirmasdan, Marketing → Sozlamalar sahifasidan kiritish.
///
/// <para><b>Qoidalar</b> (<c>.claude/rules/marketing-instagram.md</c> «KALITLARNI CRM'DAN KIRITISH»):
/// <list type="bullet">
///   <item>Faqat <c>StoredSecrets.Keys</c> ro'yxatidagi kalitlar (Instagram/Meta) — Telegram/Gemini kabi qolganlari
///     avvalgidek FAQAT <c>.env</c> da.</item>
///   <item><c>.env</c> dagi qiymat USTUN — bu yerdagisi faqat <c>.env</c> bo'sh bo'lsa o'qiladi.</item>
///   <item>Qiymat HECH QACHON qaytarilmaydi (API, audit, log) — faqat "sozlangan / qayerdan".</item>
///   <item>Zaxiraga KIRMAYDI va MCP roli o'qiy olmaydi (<c>StudentCredential</c> bilan bir xil).</item>
/// </list></para>
/// </summary>
public class StoredSecret
{
    /// <summary>`.env` dagi nomi (masalan <c>INSTAGRAM_APP_SECRET</c>) — kalit.</summary>
    public string Key { get; set; } = "";

    /// <summary>Shifrlangan qiymat (<c>CredentialVault.Protect</c>).</summary>
    public byte[] Cipher { get; set; } = [];

    /// <summary>Oxirgi o'zgarish vaqti (mahalliy, ISO).</summary>
    public string UpdatedAt { get; set; } = "";

    /// <summary>Kim kiritdi (F.I.Sh — ko'rsatish uchun).</summary>
    public string UpdatedBy { get; set; } = "";
}

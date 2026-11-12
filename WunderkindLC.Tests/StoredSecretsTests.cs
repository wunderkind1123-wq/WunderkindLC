using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Mcp;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Accounts;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Instagram/Meta kalitlarini CRM'dan kiritish (`StoredSecrets`, 2026-11-04). Qulflanadi: format tekshiruvi, faqat
/// ruxsat etilgan kalitlar, bazada SHIFRLANGAN (ochiq matn yo'q), boshqa seyf kaliti bilan ochilmaydi, zaxiraga va
/// MCP'ga kirmaydi.
/// <para>⚠️ <c>AppSecrets</c> statik — har test o'zi qo'ygan qiymatni <c>finally</c> da olib tashlaydi.</para>
/// </summary>
public class StoredSecretsTests
{
    private const string Hex = "0123456789abcdef0123456789ABCDEF";
    private static readonly CredentialVault VaultA = new(Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
    private static readonly CredentialVault VaultB = new(Convert.ToBase64String(Enumerable.Range(100, 32).Select(i => (byte)i).ToArray()));

    [Theory]
    [InlineData(AppSecrets.EnvKeys.InstagramAppSecret, Hex, true)]
    [InlineData(AppSecrets.EnvKeys.InstagramAppSecret, "", true)]                  // bo'sh = o'chirish
    [InlineData(AppSecrets.EnvKeys.InstagramAppSecret, "1234567890123456", false)] // App ID kiritilgan
    [InlineData(AppSecrets.EnvKeys.InstagramAppSecret, Hex + "0", false)]
    [InlineData(AppSecrets.EnvKeys.InstagramAppSecret, "0123456789abcdef0123456789abcdeg", false)]
    [InlineData(AppSecrets.EnvKeys.MetaAppSecret, Hex, true)]
    [InlineData(AppSecrets.EnvKeys.InstagramVerifyToken, "wk_ig_2026_x7", true)]
    [InlineData(AppSecrets.EnvKeys.InstagramVerifyToken, "short", false)]
    [InlineData(AppSecrets.EnvKeys.InstagramVerifyToken, "has space token", false)]
    [InlineData(AppSecrets.EnvKeys.MetaVerifyToken, "abc.def-ghi~123", true)]
    [InlineData(AppSecrets.EnvKeys.GeminiApiKey, "AIzaSyD-abcdefghijklmnopqrstuvwxyz12345", true)]
    [InlineData(AppSecrets.EnvKeys.GeminiApiKey, "AQ.Ab8RN6KxYzAbCdEfGhIjKlMnOpQrStUvWxYz012345", true)]   // yangi AI Studio kaliti (nuqtali)
    [InlineData(AppSecrets.EnvKeys.GeminiApiKey, "AIza short", false)]
    [InlineData(AppSecrets.EnvKeys.GeminiApiKey, "AIzaSy'; DROP TABLE x; --aaaaaaaaaaaaaaa", false)]
    public void Validate__format(string key, string value, bool ok)
    {
        var err = StoredSecrets.Validate(key, value);
        Assert.Equal(ok, err is null);
    }

    [Theory]
    [InlineData(AppSecrets.EnvKeys.TelegramBotToken)]
    [InlineData(AppSecrets.EnvKeys.CredentialVaultKey)]
    [InlineData("SOMETHING_ELSE")]
    public void Faqat_Instagram_Meta_kalitlari_CRMdan_kiritiladi(string key)
    {
        Assert.False(StoredSecrets.IsAllowed(key));
        Assert.NotNull(StoredSecrets.Validate(key, "anything-long-enough"));
        // Xotiraga ham tushmaydi.
        StoredSecrets.Apply(key, "leak");
        Assert.NotEqual("crm", AppSecrets.SourceOf(key));
    }

    [Fact]
    public async Task Bazada_shifrlangan__yuklanganda_ochiladi__ochiq_matn_yoq()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        try
        {
            db.StoredSecrets.Add(new StoredSecret { Key = AppSecrets.EnvKeys.InstagramAppSecret, Cipher = VaultA.Protect(Hex) });
            await db.SaveChangesAsync();

            var raw = (await db.StoredSecrets.AsNoTracking().SingleAsync()).Cipher;
            Assert.DoesNotContain("0123456789abcdef", System.Text.Encoding.UTF8.GetString(raw));

            var (loaded, bad) = await StoredSecrets.LoadAsync(db, VaultA);
            Assert.Equal((1, 0), (loaded, bad));
            Assert.Equal(Hex, AppSecrets.InstagramAppSecret);
            Assert.Equal("crm", AppSecrets.SourceOf(AppSecrets.EnvKeys.InstagramAppSecret));
            // Meta kaliti bo'sh — Instagram kalitiga qaytadi (avvalgi qoida o'zgarmadi).
            Assert.Equal(Hex, AppSecrets.MetaAppSecret);
        }
        finally
        {
            StoredSecrets.Apply(AppSecrets.EnvKeys.InstagramAppSecret, null);
        }
        Assert.Equal("", AppSecrets.InstagramAppSecret);
    }

    [Fact]
    public async Task Boshqa_seyf_kaliti_bilan_ochilmaydi__xato_tashlamaydi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        try
        {
            db.StoredSecrets.Add(new StoredSecret { Key = AppSecrets.EnvKeys.InstagramVerifyToken, Cipher = VaultA.Protect("wk_token_123") });
            await db.SaveChangesAsync();

            var (loaded, bad) = await StoredSecrets.LoadAsync(db, VaultB);
            Assert.Equal((0, 1), (loaded, bad));
            Assert.Equal("", AppSecrets.InstagramVerifyToken);

            // Seyf o'chiq — ham hech narsa ochilmaydi.
            Assert.Equal((0, 1), await StoredSecrets.LoadAsync(db, new CredentialVault(null)));
        }
        finally
        {
            StoredSecrets.Apply(AppSecrets.EnvKeys.InstagramVerifyToken, null);
        }
    }

    [Fact]
    public void Zaxiraga_va_MCPga_kirmaydi()
    {
        Assert.Contains("storedSecrets", BackupService.ExcludedTables);
        Assert.Contains(typeof(StoredSecret), McpReadOnlyGrants.Forbidden);
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WunderkindLC.Application.Services;

/// <summary>
/// Telegram <b>Mini App</b> (<c>window.Telegram.WebApp.initData</c>) imzosini tekshiradi.
///
/// <para>Mini App ichidagi sahifa oddiy statik HTML — u yerda "login" yo'q. Telegram sahifaga
/// imzolangan <c>initData</c> satrini beradi; server uni BOT TOKENI bilan qayta hisoblab
/// haqiqiyligini tasdiqlaydi. Rasmiy algoritm:</para>
/// <code>
/// secret_key = HMAC_SHA256(key: "WebAppData", data: bot_token)
/// hash       = HMAC_SHA256(key: secret_key,  data: data_check_string)
/// </code>
/// <para><c>data_check_string</c> — <c>hash</c>dan tashqari barcha maydonlar <c>key=value</c>
/// ko'rinishida, kalit bo'yicha alifbo tartibida, <c>\n</c> bilan ulangan holda.</para>
///
/// <para>⚠️ <c>signature</c> (Telegram'ning Ed25519 imzosi, 2024 dan har klientda) — data_check_string'ga KIRADI:
/// rasmiy qoidada HMAC uchun FAQAT <c>hash</c> chiqariladi (<c>signature</c> faqat uchinchi tomon Ed25519 tekshiruvida
/// chiqariladi). 2026-11-10 gacha u chiqarib tashlanardi — prod'da <c>POST telegram/link</c> bir haftada 383 marta 400
/// qaytardi, BIRORTA panel akkaunti (xodim/admin/superadmin) Telegram'ga ulanmadi va HR topshiriqlari botga bormadi.
/// Endi ikkala variant tekshiriladi (avval rasmiysi) — ikkalasi ham bot TOKENI bilan, ya'ni soxtalashtirib bo'lmaydi.</para>
///
/// <para>Qo'shimcha himoya: <c>auth_date</c> eskirgan bo'lsa (default 24 soat) rad etiladi —
/// bir marta o'g'irlangan satr abadiy ishlamasin.</para>
/// </summary>
public static class TelegramInitData
{
    /// <param name="ChatId">Telegram foydalanuvchi (= shaxsiy chat) id'si.</param>
    public record User(long ChatId, string Username, string FirstName, string LastName);

    /// <summary>Imzoni tekshiradi. Yaroqli bo'lsa foydalanuvchi ma'lumoti, aks holda <c>null</c>.</summary>
    /// <param name="initData">Telegram bergan xom so'rov satri (URL-encoded).</param>
    /// <param name="botToken">Mini App QAYSI botga tegishli bo'lsa — o'sha botning tokeni.</param>
    /// <param name="maxAge">Ruxsat etilgan eng katta "yosh" (default 24 soat).</param>
    public static User? Validate(string? initData, string botToken, TimeSpan? maxAge = null) =>
        Validate(initData, botToken, out _, maxAge);

    /// <summary>
    /// <see cref="Validate(string?, string, TimeSpan?)"/> + rad etish SABABI (<paramref name="reason"/>): <c>empty</c> ·
    /// <c>malformed</c> · <c>hash</c> · <c>expired</c> · <c>no-user</c>; muvaffaqiyatda bo'sh. Logga yozish uchun —
    /// ma'lumotning O'ZI (initData, token) hech qachon yozilmaydi.
    /// </summary>
    public static User? Validate(string? initData, string botToken, out string reason, TimeSpan? maxAge = null)
    {
        reason = "empty";
        if (string.IsNullOrWhiteSpace(initData) || string.IsNullOrWhiteSpace(botToken)) return null;
        reason = "malformed";

        try
        {
            // Xom satrni o'zimiz ajratamiz (tayyor query-parser emas): har juftlik alohida kerak va
            // dekodlash `encodeURIComponent`ning teskarisi bo'lishi shart — `Uri.UnescapeDataString`
            // (`+` belgisini bo'sh joyga AYLANTIRMAYDI, aks holda imzo mos kelmay qolardi).
            var pairs = new List<(string Key, string Value)>();
            string? hash = null;
            string? signature = null;
            foreach (var part in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var key = part[..eq];
                var value = Uri.UnescapeDataString(part[(eq + 1)..]);
                if (key == "hash") { hash = value; continue; }
                if (key == "signature") { signature = value; continue; }
                pairs.Add((key, value));
            }
            if (string.IsNullOrEmpty(hash) || pairs.Count == 0) return null;

            var secretKey = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
            var given = Encoding.ASCII.GetBytes(hash.ToLowerInvariant());
            bool Matches(IEnumerable<(string Key, string Value)> fields)
            {
                var dataCheckString = string.Join('\n',
                    fields.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));
                var computed = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString));
                // Vaqt bo'yicha oshkor bo'lmaydigan taqqoslash.
                return CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(Convert.ToHexString(computed).ToLowerInvariant()), given);
            }

            // Rasmiy: `signature` bilan; zaxira: `signature`siz (eski qoida bo'yicha imzolangan satr).
            var ok = signature is not null
                ? Matches(pairs.Append(("signature", signature))) || Matches(pairs)
                : Matches(pairs);
            reason = "hash";
            if (!ok) return null;

            // auth_date — eskirmaganini tekshiramiz.
            var authRaw = pairs.FirstOrDefault(p => p.Key == "auth_date").Value;
            if (long.TryParse(authRaw, out var authUnix))
            {
                var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(authUnix);
                if (age > (maxAge ?? TimeSpan.FromHours(24)) || age < TimeSpan.FromMinutes(-5))
                {
                    reason = "expired";
                    return null;
                }
            }

            reason = "no-user";
            var userJson = pairs.FirstOrDefault(p => p.Key == "user").Value;
            if (string.IsNullOrWhiteSpace(userJson)) return null;

            using var doc = JsonDocument.Parse(userJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idEl)) return null;

            string Str(string prop) =>
                root.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

            reason = "";
            return new User(idEl.GetInt64(), Str("username"), Str("first_name"), Str("last_name"));
        }
        catch
        {
            reason = "malformed";
            return null;
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Application.Services.Accounts;
using WunderkindLC.Domain;

namespace WunderkindLC.Server.Controllers;

/// <summary>
/// INSTAGRAM/META KALITLARINI CRM'DAN KIRITISH (egasining talabi, 2026-11-04) — <see cref="StoredSecrets"/>.
///
/// <para><b>Ruxsat:</b> o'qish (holat) — sinf darajasidagi <c>marketing</c>; YOZISH — <b>FAQAT superadmin</b>, HARD
/// rol (kassalar naqshi: <c>AdminPerm</c> admin'ni ham o'tkazadi). Kalit bilan begona Meta ilovasini ulab, mijozlar
/// yozishmalarini boshqa joyga yo'naltirish mumkin bo'lardi — buni faqat tizim egasi qiladi.</para>
///
/// <para>⚠️ Qiymat HECH QACHON qaytarilmaydi va auditga yozilmaydi — faqat "kiritildi/almashtirildi/o'chirildi".</para>
/// </summary>
public partial class InstagramController
{
    /// <summary>Kalitlar holati: qaysi biri sozlangan va QAYERDAN (`.env` / CRM), tahrirlash mumkinmi.</summary>
    [HttpGet("secrets")]
    public async Task<ActionResult<IgSecretsDto>> Secrets(CancellationToken ct)
    {
        var vault = HttpContext.RequestServices.GetRequiredService<CredentialVault>();
        var rows = await db.StoredSecrets.AsNoTracking()
            .Select(s => new { s.Key, s.UpdatedAt, s.UpdatedBy })
            .ToListAsync(ct);

        var items = StoredSecrets.Keys.Select(k =>
        {
            var row = rows.FirstOrDefault(r => r.Key == k.EnvName);
            return new IgSecretItemDto(
                k.EnvName, k.Label, k.Hint, k.IsAppSecret,
                Source: AppSecrets.SourceOf(k.EnvName),
                StoredInCrm: row is not null,
                UpdatedAt: row?.UpdatedAt ?? "",
                UpdatedBy: row?.UpdatedBy ?? "");
        }).ToList();

        return new IgSecretsDto(
            Items: items,
            VaultConfigured: vault.Configured,
            CanEdit: User.IsInRole(Roles.SuperAdmin),
            VaultEnvKey: AppSecrets.EnvKeys.CredentialVaultKey);
    }

    /// <summary>
    /// Kalitni kiritish / almashtirish (<c>value</c> bo'sh — CRM'dagisini o'chirish).
    /// <para>`.env` da shu kalit bo'lsa — 400: u USTUN, CRM'ga yozilgan qiymat baribir ishlamasdi (admin
    /// "saqlandi" deb o'ylab, boshqa kalit ishlayotganini bilmay qolardi).</para>
    /// </summary>
    [HttpPut("secrets/{key}")]
    public async Task<ActionResult<IgSecretsDto>> SetSecret(string key, [FromBody] IgSecretPayload body, CancellationToken ct)
    {
        if (!User.IsInRole(Roles.SuperAdmin))
            return StatusCode(403, new { message = "Tizim kalitlarini faqat tizim egasi (superadmin) kirita oladi." });

        var info = StoredSecrets.Find(key);
        if (info is null) return BadRequest(new { message = "Bu kalitni CRM'dan kiritib bo'lmaydi." });

        var vault = HttpContext.RequestServices.GetRequiredService<CredentialVault>();
        if (!vault.Configured)
            return BadRequest(new
            {
                message = $"Kalitlarni shifrlash o'chiq: serverdagi .env da {AppSecrets.EnvKeys.CredentialVaultKey} yo'q. "
                    + $"Hozircha {key} ni .env ga yozing.",
            });

        if (AppSecrets.SourceOf(key) == "env")
            return BadRequest(new
            {
                message = $"{key} serverdagi .env faylida allaqachon bor va u ustun — CRM'dagi qiymat ishlamaydi. "
                    + "Almashtirish uchun .env dagini o'zgartiring yoki olib tashlang.",
            });

        var value = (body.Value ?? "").Trim();
        if (StoredSecrets.Validate(key, value) is { } error) return BadRequest(new { message = error });

        var row = await db.StoredSecrets.FirstOrDefaultAsync(s => s.Key == key, ct);
        string verb;
        if (value.Length == 0)
        {
            if (row is null) return await Secrets(ct);
            db.StoredSecrets.Remove(row);
            verb = "o'chirildi";
        }
        else
        {
            verb = row is null ? "kiritildi" : "almashtirildi";
            row ??= db.StoredSecrets.Add(new StoredSecret { Key = key }).Entity;
            row.Cipher = vault.Protect(value);
            row.UpdatedAt = AppClock.Iso();
            row.UpdatedBy = Actor;
        }

        // ⚠️ Qiymat auditga YOZILMAYDI — faqat qaysi kalit va nima bo'lgani.
        audit.Record(AuditEntity, "secrets", value.Length == 0 ? "delete" : "update",
            $"{info.Label} ({key}) CRM orqali {verb}");
        await db.SaveChangesAsync(ct);

        // Xotiradagi qiymat — faqat saqlash muvaffaqiyatli bo'lgach (yiqilsa eski kalit ishlayveradi).
        StoredSecrets.Apply(key, value);
        logger.LogInformation("Tizim kaliti {Key} CRM orqali {Verb} ({Actor})", key, verb, Actor);
        return await Secrets(ct);
    }
}

/// <summary>Bitta kalit holati. ⚠️ QIYMAT YO'Q.</summary>
/// <param name="Source"><c>"env"</c> · <c>"crm"</c> · <c>""</c> (sozlanmagan).</param>
/// <param name="StoredInCrm">CRM'da qator bor (`.env` ustun bo'lsa ham) — o'chirish tugmasi uchun.</param>
public record IgSecretItemDto(
    string Key, string Label, string Hint, bool IsAppSecret,
    string Source, bool StoredInCrm, string UpdatedAt, string UpdatedBy);

public record IgSecretsDto(List<IgSecretItemDto> Items, bool VaultConfigured, bool CanEdit, string VaultEnvKey);

/// <summary><paramref name="Value"/> bo'sh — CRM'dagi qiymatni o'chirish.</summary>
public record IgSecretPayload(string? Value);

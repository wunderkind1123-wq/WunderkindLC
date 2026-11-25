using System.Text;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Abstractions;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services;

/// <summary>
/// CALL CENTER «IMPORT» (2026-11-25, egasi: «amoCRM'dagidek — bitta odam uchun kontakt yuklash; shu odamga ko'rinadi,
/// boshqalarga yo'q, faqat direktor, superadmin va menejer ko'radi»).
///
/// <para>Fayl (.xlsx yoki .csv) — ustunlar: <b>Ism · Telefon · Izoh</b> (izoh ixtiyoriy; birinchi qator sarlavha bo'lsa
/// o'tkaziladi). Har qator — tanlangan operatorga biriktirilgan SHAXSIY lid (<see cref="Lead.IsPrivate"/>), Call Center
/// navbatida, birinchi bosqichda, manba «Import».</para>
///
/// <para>O'tkaziladi (sababi bilan qaytadi, jim tashlanmaydi): telefon xato/bo'sh; fayl ichida takror; bazada shu raqamli
/// LID bor (ochiq/aylantirilgan/arxiv, boshqa operatorniki ham — global filtrsiz); shu raqamli O'QUVCHI bor (o'zi yoki
/// ota-onasi telefoni). Egasining qarori: bitta odamga ikki operator qo'ng'iroq qilmasin.</para>
///
/// <para>⚠️ Telegram KARTASI yuborilmaydi (500 kontakt — 500 xabar bo'lardi) va avto-xabar yo'q. Har lidga «created»
/// hodisasi, butun importga BITTA audit qatori.</para>
/// </summary>
public static class LeadImport
{
    public const string SourceName = "Import";
    /// <summary>Bir faylda ko'pi bilan nechta qator.</summary>
    public const int MaxRows = 2000;
    public const int MaxNameLength = 100;
    public const int MaxNoteLength = 1000;
    public const string PlaceholderName = "Noma'lum (import)";

    /// <summary>Fayldan o'qilgan bitta qator (Row — fayldagi 1 dan boshlanadigan raqam).</summary>
    public readonly record struct InputRow(int Row, string Name, string Phone, string Note);

    /// <summary>Yaratiladigan kontakt (telefon normallashtirilgan).</summary>
    public readonly record struct PlannedLead(int Row, string Name, string Phone, string Note);

    public sealed record Plan(List<PlannedLead> Create, List<LeadImportSkipDto> Skipped);

    /// <summary>
    /// SOF reja: qaysi qator yaratiladi, qaysi biri NEGA o'tkaziladi. <paramref name="existingKeys"/> — bazadagi lid va
    /// o'quvchi telefonlarining kalitlari (<see cref="PhoneUtil.Key"/>).
    /// </summary>
    public static Plan BuildPlan(IEnumerable<InputRow> rows, IReadOnlySet<string> leadKeys, IReadOnlySet<string> studentKeys)
    {
        var create = new List<PlannedLead>();
        var skipped = new List<LeadImportSkipDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var name = Clip(r.Name, MaxNameLength);
            var note = Clip(r.Note, MaxNoteLength);
            if (name.Length == 0 && r.Phone.Trim().Length == 0) continue; // bo'sh qator — sanalmaydi

            var (valid, normalized, error) = PhoneUtil.Validate(r.Phone);
            if (!valid)
            {
                skipped.Add(new LeadImportSkipDto(r.Row, name, r.Phone.Trim(), error ?? "Telefon raqami noto'g'ri"));
                continue;
            }
            var key = PhoneUtil.Key(normalized);
            if (!seen.Add(key))
            {
                skipped.Add(new LeadImportSkipDto(r.Row, name, normalized, "Faylda takrorlangan raqam"));
                continue;
            }
            if (leadKeys.Contains(key))
            {
                skipped.Add(new LeadImportSkipDto(r.Row, name, normalized, "Bu raqamli lid bazada bor"));
                continue;
            }
            if (studentKeys.Contains(key))
            {
                skipped.Add(new LeadImportSkipDto(r.Row, name, normalized, "Bu raqam o'quvchida (yoki ota-onasida) bor"));
                continue;
            }
            create.Add(new PlannedLead(r.Row, name.Length == 0 ? PlaceholderName : name, normalized, note));
        }
        return new Plan(create, skipped);
    }

    /// <summary>
    /// Faylni qatorlarga o'qiydi: .xlsx (<see cref="ExcelImport"/>) yoki .csv (vergul yoki nuqtali vergul, qo'shtirnoq
    /// bilan). Birinchi qator SARLAVHA bo'lsa (telefon ustunida raqam yo'q) — o'tkaziladi.
    /// </summary>
    public static (List<InputRow> Rows, string? Error) ReadFile(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        List<string[]> raw;
        try
        {
            raw = ext switch
            {
                ".xlsx" => ExcelImport.ReadRows(stream, 3),
                ".csv" or ".txt" => ReadCsv(stream),
                _ => throw new InvalidDataException("Faqat .xlsx yoki .csv fayl yuklang"),
            };
        }
        catch (InvalidDataException e) { return ([], e.Message); }
        catch (Exception) { return ([], "Faylni o'qib bo'lmadi — .xlsx yoki .csv ekanini tekshiring"); }

        var rows = new List<InputRow>();
        for (var i = 0; i < raw.Count; i++)
        {
            var c = raw[i];
            string Col(int n) => n < c.Length ? (c[n] ?? "").Trim() : "";
            if (i == 0 && PhoneUtil.Key(Col(1)).Length < 7 && !string.IsNullOrWhiteSpace(Col(0) + Col(1))) continue; // sarlavha
            rows.Add(new InputRow(i + 1, Col(0), Col(1), Col(2)));
        }
        var nonEmpty = rows.Count(r => r.Name.Length > 0 || r.Phone.Length > 0);
        if (nonEmpty == 0) return ([], "Faylda kontakt topilmadi (ustunlar: Ism, Telefon, Izoh)");
        if (nonEmpty > MaxRows) return ([], $"Bir faylda ko'pi bilan {MaxRows} ta kontakt — faylni bo'ling");
        return (rows, null);
    }

    /// <summary>
    /// Bazaga yozadi (SaveChanges — chaqiruvchida). Takror tekshiruvi GLOBAL FILTRSIZ: boshqa operatorning shaxsiy
    /// lidi ham "bor" sanaladi.
    /// </summary>
    public static async Task<Plan> ApplyAsync(
        IAppDbContext db, AuditService audit, IReadOnlyList<InputRow> rows,
        string assigneeId, string assigneeName, string actorName, string? actorId, string fileName,
        CancellationToken ct = default)
    {
        var leadKeys = (await db.Leads.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.PhoneKey != "").Select(l => l.PhoneKey).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var studentPhones = await db.Students.AsNoTracking()
            .Select(s => new { s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone }).ToListAsync(ct);
        var studentKeys = studentPhones
            .SelectMany(s => new[] { s.Phone, s.ParentPhone, s.FatherPhone, s.MotherPhone })
            .Select(PhoneUtil.Key).Where(k => k.Length >= 7)
            .ToHashSet(StringComparer.Ordinal);

        var plan = BuildPlan(rows, leadKeys, studentKeys);
        if (plan.Create.Count == 0) return plan;

        var stage = await LeadIntake.FirstStageIdAsync(db, ct);
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        foreach (var p in plan.Create)
        {
            var lead = new Lead
            {
                FullName = p.Name,
                Phone = p.Phone,
                Note = p.Note.Length == 0 ? null : p.Note,
                Stage = stage,
                Source = SourceName,
                CreatedAt = now,
                AssigneeUserId = assigneeId,
                InCallCenter = true,
                IsPrivate = true,
            };
            db.Leads.Add(lead);
            db.LeadEvents.Add(new LeadEvent
            {
                LeadId = lead.Id, Type = "created",
                Text = $"Import qilindi ({fileName}) — mas'ul: {assigneeName}",
                ActorName = actorName, ActorUserId = actorId, CreatedAt = now, ToStage = stage,
            });
        }
        audit.Record("Lead", "import", "create",
            $"Call Center importi: {plan.Create.Count} ta shaxsiy kontakt — mas'ul: {assigneeName} (fayl: {fileName})"
                + (plan.Skipped.Count > 0 ? $"; {plan.Skipped.Count} ta qator o'tkazildi" : ""));
        return plan;
    }

    private static string Clip(string? s, int max)
    {
        var t = (s ?? "").Trim();
        return t.Length <= max ? t : t[..max];
    }

    /// <summary>Oddiy CSV o'qigich: ajratkich — birinchi qatorda ko'proq uchragani (`;` yoki `,`), qo'shtirnoq qo'llanadi.</summary>
    private static List<string[]> ReadCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = lines.FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        var sep = first.Count(ch => ch == ';') > first.Count(ch => ch == ',') ? ';' : ',';
        var result = new List<string[]>();
        foreach (var line in lines)
        {
            if (result.Count > MaxRows + 1) break;
            var cells = new List<string>();
            var sb = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (ch == '"') quoted = false;
                    else sb.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == sep) { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            cells.Add(sb.ToString());
            result.Add(cells.ToArray());
        }
        return result;
    }
}

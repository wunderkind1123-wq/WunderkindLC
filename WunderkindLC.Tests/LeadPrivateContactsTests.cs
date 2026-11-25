using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CALL CENTER «IMPORT» va SHAXSIY KONTAKTLAR (2026-11-25): import rejasi (<see cref="LeadImport.BuildPlan"/>), bazaning
/// GLOBAL filtri (<c>AppDbContext.LeadViewerId</c>) va kim hammasini ko'rishi (<see cref="LeadVisibility"/>).
/// </summary>
public class LeadPrivateContactsTests
{
    // ============================ Import rejasi ============================

    [Fact]
    public void Reja__bazada_bor_raqam_faylda_takror_va_xato_telefon_OTKAZILADI_sababi_bilan()
    {
        var rows = new[]
        {
            new LeadImport.InputRow(2, "Ali", "+998 90 111 22 33", "qo'ng'iroq qilsin"),
            new LeadImport.InputRow(3, "Vali", "90 111 22 33", ""),          // faylda takror (kalit bir xil)
            new LeadImport.InputRow(4, "Sobir", "12", ""),                    // xato telefon
            new LeadImport.InputRow(5, "Lidda bor", "901112244", ""),         // bazada lid
            new LeadImport.InputRow(6, "O'quvchida bor", "901112255", ""),    // o'quvchi/ota-ona
            new LeadImport.InputRow(7, "", "", ""),                           // bo'sh qator — sanalmaydi
            new LeadImport.InputRow(8, "", "901112266", ""),                  // ismsiz — placeholder
        };
        var plan = LeadImport.BuildPlan(rows,
            new HashSet<string> { "901112244" }, new HashSet<string> { "901112255" });

        Assert.Equal([2, 8], plan.Create.Select(c => c.Row));
        Assert.Equal("+998-90-111-22-33", plan.Create[0].Phone);
        Assert.Equal("qo'ng'iroq qilsin", plan.Create[0].Note);
        Assert.Equal(LeadImport.PlaceholderName, plan.Create[1].Name);
        Assert.Equal([3, 4, 5, 6], plan.Skipped.Select(s => s.Row));
        Assert.Contains("takror", plan.Skipped[0].Reason);
        Assert.Contains("lid", plan.Skipped[2].Reason);
        Assert.Contains("o'quvchi", plan.Skipped[3].Reason);
    }

    [Fact]
    public void Fayl__CSV_sarlavha_otkaziladi_nuqtali_vergul_va_qoshtirnoq()
    {
        var csv = "Ism;Telefon;Izoh\n\"Karimov; Ali\";901112233;\"u \"\"ertaga\"\" dedi\"\nVali;901112244;\n";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var (rows, error) = LeadImport.ReadFile(ms, "kontaktlar.csv");

        Assert.Null(error);
        Assert.Equal(2, rows.Count(r => r.Phone.Length > 0));
        Assert.Equal("Karimov; Ali", rows[0].Name);
        Assert.Equal("u \"ertaga\" dedi", rows[0].Note);
        Assert.Equal(2, rows[0].Row);
    }

    [Fact]
    public void Fayl__notogri_turi_rad_etiladi()
    {
        using var ms = new MemoryStream([1, 2, 3]);
        Assert.NotNull(LeadImport.ReadFile(ms, "a.pdf").Error);
    }

    // ============================ Global filtr ============================

    [Fact]
    public async Task Filtr__operator_FAQAT_ozining_shaxsiy_kontaktini_korar_oddiy_lidlar_hammaga()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Leads.AddRange(
            new Lead { FullName = "A shaxsiy", Phone = "+998-90-000-00-01", IsPrivate = true, AssigneeUserId = "opA" },
            new Lead { FullName = "B shaxsiy", Phone = "+998-90-000-00-02", IsPrivate = true, AssigneeUserId = "opB" },
            new Lead { FullName = "Oddiy", Phone = "+998-90-000-00-03", AssigneeUserId = "opB" });
        await ctx.SaveChangesAsync();

        async Task<string[]> Seen(string? viewer)
        {
            ctx.LeadViewerId = viewer;
            ctx.ChangeTracker.Clear();
            return (await ctx.Leads.Select(l => l.FullName).ToListAsync()).Order().ToArray();
        }

        Assert.Equal(["A shaxsiy", "Oddiy"], await Seen("opA"));
        Assert.Equal(["B shaxsiy", "Oddiy"], await Seen("opB"));
        Assert.Equal(["Oddiy"], await Seen(LeadVisibility.NobodyId));
        Assert.Equal(["A shaxsiy", "B shaxsiy", "Oddiy"], await Seen(null)); // direktor/superadmin/menejer, fon xizmati

        // Begona shaxsiy lidni id bilan ham ochib bo'lmaydi (lid sahifasi → 404).
        ctx.LeadViewerId = "opA";
        ctx.ChangeTracker.Clear();
        var bId = await ctx.Leads.IgnoreQueryFilters().Where(l => l.FullName == "B shaxsiy").Select(l => l.Id).SingleAsync();
        Assert.Null(await ctx.Leads.FindAsync(bId));
    }

    [Fact]
    public async Task Takror_tekshiruvi__begona_operatorning_shaxsiy_kontakti_HAM_topiladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Leads.Add(new Lead { FullName = "B shaxsiy", Phone = "+998-90-000-00-02", IsPrivate = true, AssigneeUserId = "opB" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        ctx.LeadViewerId = "opA";

        // Aks holda opA oqimida (Instagram «Lidga aylantirish» va h.k.) shu odamga ikkinchi lid ochilardi.
        Assert.NotNull(await LeadIntake.FindByPhoneAsync(ctx, "90 000 00 02"));
    }

    // ============================ Kim hammasini ko'radi ============================

    private static ClaimsPrincipal User(string role, params string[] perms)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "u1"), new(ClaimTypes.Role, role) };
        claims.AddRange(perms.Select(p => new Claim(LeadVisibility.PermClaimType, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public void Korinish__superadmin_direktor_va_ANIQ_ruxsatli_menejer_cheklanmaydi_qolganlar_ozi()
    {
        Assert.Null(LeadVisibility.ViewerIdFor(User(Roles.SuperAdmin)));
        Assert.Null(LeadVisibility.ViewerIdFor(User(Roles.Admin)));
        Assert.Null(LeadVisibility.ViewerIdFor(User(Roles.Staff, "leads.callcenter", LeadVisibility.PermissionKey)));
        // Yalang `leads` (bo'lim) YETMAYDI — kalit qattiq, meros yo'q.
        Assert.Equal("u1", LeadVisibility.ViewerIdFor(User(Roles.Staff, "leads")));
        Assert.Equal("u1", LeadVisibility.ViewerIdFor(User(Roles.Staff, "leads.callcenter")));
        // Anonim (ommaviy forma, webhook) — cheklovsiz: takror tekshiruvi hamma lidni ko'rishi shart.
        Assert.Null(LeadVisibility.ViewerIdFor(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}

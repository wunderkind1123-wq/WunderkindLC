using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «MAOSHGA TUSHGAN PUL» bazasi (2026-11-04, egasining qarori: "ustozlar doim tushirgan pulidan oylik oladi") —
/// <see cref="CenterMeta.SalaryCashBaseFrom"/> dan boshlab foiz = shu oyda (to'lov SANASI bo'yicha) tushgan pul − vozvrat
/// + shu oy hisoblaridagi chegirma (markaz ko'taradi). Kech to'langan qarz — pul TUSHGAN oyga. Kuchga kirishdan oldingi
/// oyga teglangan to'lov — kirmaydi (u oy maoshi eski bazada to'liq hisoblangan).
/// </summary>
public class SalaryCashBaseTests
{
    private const string Sep = "2026-09";
    private const string Oct = "2026-10";
    private const string Nov = "2026-11";
    private const string Dec = "2026-12";

    /// <summary>40% foizli o'qituvchi + guruh (500 000) + o'quvchi; sentabrdan hisoblangan, oktabrdan tushgan pul bazasi.</summary>
    private static (Teacher T, Group G, Student S) Setup(AppDbContext ctx)
    {
        ctx.CenterMeta.Add(new CenterMeta { SalaryChargedBaseFrom = Sep, SalaryCashBaseFrom = Oct });
        var t = new Teacher { FullName = "Foizli Ustoz", SalaryMode = "percent", SalaryPercent = 40 };
        ctx.Teachers.Add(t);
        var g = new Group
        {
            Name = "B guruh", MonthlyFee = 500_000m, TeacherId = t.Id,
            TeacherSalaryMode = "percent", TeacherSalaryPercent = 40, Days = new List<int> { 0, 2, 4 },
        };
        ctx.Classes.Add(g);
        var s = new Student { FullName = "Ali Valiyev", EnrollmentDate = "2026-01-01" };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = "2026-01-01", ActivatedAt = "2026-01-01", RecordedAt = "2026-01-01",
        });
        return (t, g, s);
    }

    private static void Charge(AppDbContext ctx, Student s, Group g, string month, decimal amount, decimal discount = 0m) =>
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = month, Amount = amount, Discount = discount, Date = $"{month}-01",
        });

    private static void Pay(AppDbContext ctx, Student s, Group? g, string date, string forMonth, decimal amount) =>
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = date, Direction = "income", Category = "tuition", Amount = amount,
            StudentId = s.Id, GroupId = g?.Id, Month = forMonth, Method = "cash",
        });

    private static async Task<Application.Dtos.MonthSalaryDto> MonthAsync(AppDbContext ctx, Teacher t, string month) =>
        (await SalaryLedger.BuildAsync(ctx, t, month, month)).Months.Single(m => m.Month == month);

    [Fact]
    public async Task Egasining_misoli__oylik_500_chegirma_100_tolandi_400__foiz_40__maosh_200()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        Charge(ctx, s, g, Oct, 500_000m, discount: 100_000m);
        Pay(ctx, s, g, "2026-10-05", Oct, 400_000m);
        await ctx.SaveChangesAsync();

        var m = await MonthAsync(ctx, t, Oct);

        Assert.True(m.CashBase);
        Assert.Equal(400_000m, m.SalaryCash);
        Assert.Equal(100_000m, m.SalaryDiscount);
        Assert.Equal(500_000m, m.SalaryBase);
        Assert.Equal(200_000m, m.Expected);
        Assert.Equal(m.Expected, m.CollectedExpected);   // «hozir chiqadigan» = maoshning o'zi
    }

    [Fact]
    public async Task Tolanmagan_oyda_maosh_YOQ__hisoblangan_bazadan_farqi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        Charge(ctx, s, g, Sep, 500_000m);   // sentabr — hisoblangan baza: pul kelmasa ham maosh bor
        Charge(ctx, s, g, Oct, 500_000m);   // oktabr — tushgan pul bazasi: pul kelmagan
        await ctx.SaveChangesAsync();

        Assert.Equal(200_000m, (await MonthAsync(ctx, t, Sep)).Expected);
        var oct = await MonthAsync(ctx, t, Oct);
        Assert.Equal(0m, oct.Expected);
        Assert.Equal(200_000m, oct.PotentialExpected);   // "hammasi to'lasa" — to'liq oylik × foiz
    }

    [Fact]
    public async Task Kech_tolangan_qarz_PUL_TUSHGAN_oyga__oktabr_maoshi_ozgarmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        Charge(ctx, s, g, Oct, 500_000m);
        Charge(ctx, s, g, Nov, 500_000m);
        Charge(ctx, s, g, Dec, 500_000m);
        Pay(ctx, s, g, "2026-12-03", Oct, 500_000m);   // oktabr qarzi dekabrda
        await ctx.SaveChangesAsync();

        Assert.Equal(0m, (await MonthAsync(ctx, t, Oct)).Expected);
        Assert.Equal(0m, (await MonthAsync(ctx, t, Nov)).Expected);
        var dec = await MonthAsync(ctx, t, Dec);
        Assert.Equal(500_000m, dec.SalaryCash);
        Assert.Equal(200_000m, dec.Expected);
    }

    [Fact]
    public async Task Sentabr_qarzi_oktabrda_tolansa_KIRMAYDI__sentabr_hisoblangan_bazada_tolangan()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        Charge(ctx, s, g, Sep, 500_000m);
        Charge(ctx, s, g, Oct, 500_000m);
        Pay(ctx, s, g, "2026-10-04", Sep, 500_000m);   // sentabr uchun — oktabrda
        Pay(ctx, s, g, "2026-10-06", Oct, 500_000m);
        await ctx.SaveChangesAsync();

        Assert.Equal(200_000m, (await MonthAsync(ctx, t, Sep)).Expected);   // hisoblangan — o'zgarmagan
        var oct = await MonthAsync(ctx, t, Oct);
        Assert.Equal(500_000m, oct.SalaryCash);   // faqat oktabr puli — ikki marta emas
        Assert.Equal(200_000m, oct.Expected);
    }

    [Fact]
    public async Task Sentabrda_tolangan_oktabr_AVANSI_oktabrga_kiradi__yoqolmaydi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        Charge(ctx, s, g, Oct, 500_000m);
        Pay(ctx, s, g, "2026-09-25", Oct, 500_000m);
        await ctx.SaveChangesAsync();

        Assert.Equal(200_000m, (await MonthAsync(ctx, t, Oct)).Expected);
    }

    [Fact]
    public async Task Noyabr_avansi_oktabrda_tolansa_OKTABRGA__pul_tushgan_oy()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        Charge(ctx, s, g, Oct, 500_000m);
        Charge(ctx, s, g, Nov, 500_000m);
        Pay(ctx, s, g, "2026-10-05", Oct, 500_000m);
        Pay(ctx, s, g, "2026-10-20", Nov, 500_000m);
        await ctx.SaveChangesAsync();

        Assert.Equal(400_000m, (await MonthAsync(ctx, t, Oct)).Expected);
        Assert.Equal(0m, (await MonthAsync(ctx, t, Nov)).Expected);
    }

    [Fact]
    public async Task Vozvrat_oz_oyida_ayriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        Charge(ctx, s, g, Oct, 500_000m);
        Pay(ctx, s, g, "2026-10-05", Oct, 500_000m);
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = "2026-10-25", Direction = "expense", Category = "refund", Amount = 100_000m,
            StudentId = s.Id, GroupId = g.Id, Month = Oct, Method = "cash",
        });
        await ctx.SaveChangesAsync();

        var oct = await MonthAsync(ctx, t, Oct);
        Assert.Equal(400_000m, oct.SalaryCash);
        Assert.Equal(160_000m, oct.Expected);
    }

    [Fact]
    public async Task Chegirmani_ustoz_qoplaydi_belgisi_ikki_marta_ayirmaydi__faqat_bitta_ushlanma()
    {
        // B varianti: chegirma bazaga qo'shiladi (markaz ko'taradi), «ustoz qoplaydi» belgisi bo'lsa — DiscountCover ushlanmasi.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        Charge(ctx, s, g, Oct, 500_000m, discount: 100_000m);
        Pay(ctx, s, g, "2026-10-05", Oct, 400_000m);
        await ctx.SaveChangesAsync();

        var m = await MonthAsync(ctx, t, Oct);

        // Belgisiz: 200 000 (chegirma ustozdan ushlanmaydi). Belgi bo'lsa ushlanma alohida qatorda (DiscountCoverTests).
        Assert.Equal(0m, m.DiscountCover);
        Assert.Equal(200_000m, m.Expected);
    }

    [Fact]
    public async Task Teglanmagan_tolov_guruh_narxi_nisbatida_bolinadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        var other = new Group { Name = "Boshqa", MonthlyFee = 500_000m, Days = new List<int> { 1, 3 } };
        ctx.Classes.Add(other);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = other.Id, Status = "active", IsActive = true,
            JoinedAt = "2026-01-01", ActivatedAt = "2026-01-01", RecordedAt = "2026-01-01",
        });
        Pay(ctx, s, null, "2026-10-05", Oct, 600_000m);   // guruhsiz — 50/50
        await ctx.SaveChangesAsync();

        Assert.Equal(300_000m, (await MonthAsync(ctx, t, Oct)).SalaryCash);
    }

    [Fact]
    public async Task Ekrandagi_ikki_bosqich__tushgan_pul_foizi_160_plus_chegirma_foizi_40()
    {
        // Egasi (2026-11-04): «tushgan puldan foiz ajratilib, chegirma bo'lsa qo'shilishi kerak» — markaz 100 000 chegirma
        // bergan bo'lsa ustozga 40 000. Ikki qism yig'indisi = maosh (ushlanmalarsiz).
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        Charge(ctx, s, g, Oct, 500_000m, discount: 100_000m);
        Pay(ctx, s, g, "2026-10-05", Oct, 300_000m);
        Pay(ctx, s, g, "2026-10-15", Oct, 100_000m);
        Pay(ctx, s, g, "2026-10-04", Sep, 999_000m);   // sentabr uchun — kirmaydi
        await ctx.SaveChangesAsync();

        var m = await MonthAsync(ctx, t, Oct);

        Assert.Equal(400_000m, m.SalaryCash);
        Assert.Equal(160_000m, m.CashSalary);
        Assert.Equal(100_000m, m.SalaryDiscount);
        Assert.Equal(40_000m, m.DiscountSalary);
        Assert.Equal(m.CashSalary + m.DiscountSalary, m.Expected);
    }

    [Fact]
    public async Task Tushgan_pul_tarkibi__shu_oy_uchun_otgan_oy_qarzi_va_avans()
    {
        // Egasi so'radi (2026-11-04): «Tushum bo'ldi» (shu oy UCHUN) bilan «Tushgan pul» (shu oy KUNLARIDA) farqi ko'rinsin.
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        Pay(ctx, s, g, "2026-11-01", Nov, 400_000m);
        Pay(ctx, s, g, "2026-11-02", Oct, 200_000m);   // oktabr qarzi
        Pay(ctx, s, g, "2026-11-03", Dec, 300_000m);   // dekabr avansi
        await ctx.SaveChangesAsync();

        var m = await MonthAsync(ctx, t, Nov);

        Assert.Equal(900_000m, m.SalaryCash);
        Assert.Equal(200_000m, m.CashLate);
        Assert.Equal(300_000m, m.CashAdvance);
        Assert.Equal(400_000m, m.SalaryCash - m.CashLate - m.CashAdvance);
        Assert.Equal(360_000m, m.CashSalary);
    }

    [Fact]
    public async Task Avans_hisoblangan_maoshdan_ayriladi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        var (t, g, s) = Setup(ctx);
        Charge(ctx, s, g, Oct, 500_000m, discount: 100_000m);
        Pay(ctx, s, g, "2026-10-05", Oct, 400_000m);
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Date = "2026-10-20", Direction = "expense", Category = "salary", Amount = 50_000m,
            TeacherId = t.Id, Month = Oct, Method = "cash", Note = "Avans",
        });
        await ctx.SaveChangesAsync();

        var m = await MonthAsync(ctx, t, Oct);

        Assert.Equal(200_000m, m.Expected);
        Assert.Equal(50_000m, m.Paid);
        Assert.Equal(150_000m, m.Remaining);
    }

    [Theory]
    [InlineData("2026-10-05", "2026-10", "2026-10", "2026-10")]
    [InlineData("2026-12-03", "2026-10", "2026-10", "2026-12")]   // kech to'lov — tushgan oyga
    [InlineData("2026-10-04", "2026-09", "2026-10", null)]        // eski oy qarzi — kirmaydi
    [InlineData("2026-09-25", "2026-10", "2026-10", "2026-10")]   // avans — kuchga kirgan oyga
    [InlineData("2026-10-05", null, "2026-10", "2026-10")]        // tegsiz — sana oyi
    [InlineData("2026-10-05", "2026-10", "", null)]               // o'chiq
    public void CashMonthOf_qoidasi(string date, string? forMonth, string cashFrom, string? expected) =>
        Assert.Equal(expected, SalaryLedger.CashMonthOf(date, forMonth, cashFrom));
}

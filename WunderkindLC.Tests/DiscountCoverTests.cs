using WunderkindLC.Application.Services;
using WunderkindLC.Application.Dtos;
using WunderkindLC.Domain;
using WunderkindLC.Infrastructure.Data;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «CHEGIRMANI USTOZ QOPLAYDI» (2026-11-03, <see cref="DiscountCover"/>): standartda chegirmani markaz ko'taradi (maosh to'liq
/// tarifdan), belgi qo'yilsa ustoz maoshidan chegirmaning MARKAZ ULUSHI ushlanadi — 
/// maoshida ham — chegirmaning admin kiritgan foizi ushlanadi. Misol: chegirma 137 500, 60% → 82 500.
/// </summary>
public class DiscountCoverTests
{
    private const string Month = "2026-11";

    [Fact]
    public void Qoida_chegirma_x_admin_foizi()
    {
        Assert.Equal(82_500m, DiscountCover.Withhold(137_500m, 60m));
        Assert.Equal(41_250m, DiscountCover.Withhold(137_500m, 30m));
        Assert.Equal(137_500m, DiscountCover.Withhold(137_500m, 150m));   // 100% dan oshmaydi
        Assert.Equal(0m, DiscountCover.Withhold(137_500m, 0m));
        Assert.Equal("2026-11", DiscountCover.CoverFrom("2026-09", "2026-11"));   // o'tgan oylar o'zgarmaydi
        Assert.Equal("2026-12", DiscountCover.CoverFrom("2026-12", "2026-11"));
        Assert.False(DiscountCover.Covers(true, "2026-11", "2026-10"));
        Assert.True(DiscountCover.Covers(true, "2026-11", "2026-11"));
        Assert.False(DiscountCover.Covers(false, "2026-11", "2026-12"));
    }

    private static async Task<(Teacher T, Group G, Student S)> SetupAsync(
        AppDbContext ctx, bool covers, string teacherMode = "percent", decimal teacherSalary = 0m, string coversFrom = Month, int coverPct = 60)
    {
        ctx.CenterMeta.Add(new CenterMeta { SalaryChargedBaseFrom = "2026-09" });
        var t = new Teacher { FullName = "Muxlisa", SalaryMode = teacherMode, SalaryPercent = 40, Salary = teacherSalary };
        ctx.Teachers.Add(t);
        var g = new Group { Name = "Beginner", MonthlyFee = 275_000m, TeacherId = t.Id, Days = [0, 2, 4] };
        ctx.Classes.Add(g);
        var s = new Student { FullName = "Ali Valiyev", EnrollmentDate = "2026-01-01" };
        ctx.Students.Add(s);
        ctx.StudentGroups.Add(new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, Status = "active", IsActive = true,
            JoinedAt = "2026-01-01", ActivatedAt = "2026-01-01", RecordedAt = "2026-01-01",
        });
        ctx.StudentDiscounts.Add(new StudentDiscount
        {
            StudentId = s.Id, StudentName = s.FullName, GroupId = g.Id, GroupName = g.Name, Pct = 50, StartMonth = "2026-09",
            Reason = "Ustoz iltimosi", Status = StudentDiscount.StatusActive, CreatedAt = "2026-09-01",
            TeacherCovers = covers, TeacherCoversFrom = covers ? coversFrom : "", TeacherCoverPct = covers ? coverPct : 0,
        });
        ctx.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = Month, Amount = 275_000m, Discount = 137_500m, Date = $"{Month}-01",
        });
        await ctx.SaveChangesAsync();
        return (t, g, s);
    }

    [Fact]
    public async Task Standart_markaz_koradi__maosh_TOLIQ_tarifdan()
    {
        using var db = TestDb.Sqlite();
        var (t, _, _) = await SetupAsync(db.Context, covers: false);
        var m = (await SalaryLedger.BuildAsync(db.Context, t, Month, Month)).Months.Single();
        Assert.Equal(110_000m, m.Expected);
        Assert.Equal(0m, m.DiscountCover);
        Assert.Null(m.DiscountCoverItems);
    }

    [Fact]
    public async Task Ustoz_qoplaydi__admin_60_foiz__82500_ushlanadi()
    {
        using var db = TestDb.Sqlite();
        var (t, _, _) = await SetupAsync(db.Context, covers: true);
        var m = (await SalaryLedger.BuildAsync(db.Context, t, Month, Month)).Months.Single();
        Assert.Equal(110_000m, m.BaseExpected);           // baza baribir to'liq tarif
        Assert.Equal(82_500m, m.DiscountCover);           // 137 500 × 60%
        Assert.Equal(27_500m, m.Expected);
        var item = Assert.Single(m.DiscountCoverItems!);
        Assert.Equal(("Ali Valiyev", 137_500m, 60m, 82_500m), (item.StudentName, item.Discount, item.RatePct, item.Amount));
    }

    [Fact]
    public async Task Ustoz_qoplaydi__qatiy_maoshda_ham_AYNAN_admin_foizi()
    {
        using var db = TestDb.Sqlite();
        var (t, _, _) = await SetupAsync(db.Context, covers: true, teacherMode: "fixed", teacherSalary: 3_000_000m);
        var m = (await SalaryLedger.BuildAsync(db.Context, t, Month, Month)).Months.Single();
        Assert.Equal(82_500m, m.DiscountCover);
        Assert.Equal(m.BaseExpected - 82_500m, m.Expected);
    }

    [Fact]
    public async Task Admin_30_foiz_kiritsa__41250()
    {
        using var db = TestDb.Sqlite();
        var (t, _, _) = await SetupAsync(db.Context, covers: true, coverPct: 30);
        var m = (await SalaryLedger.BuildAsync(db.Context, t, Month, Month)).Months.Single();
        Assert.Equal(41_250m, m.DiscountCover);
        Assert.Equal(68_750m, m.Expected);
    }

    [Fact]
    public async Task Belgi_qoyilgan_oydan_OLDINGI_oy_ozgarmaydi()
    {
        using var db = TestDb.Sqlite();
        var (t, _, _) = await SetupAsync(db.Context, covers: true, coversFrom: "2026-12");
        var m = (await SalaryLedger.BuildAsync(db.Context, t, Month, Month)).Months.Single();
        Assert.Equal(0m, m.DiscountCover);
        Assert.Equal(110_000m, m.Expected);
    }

    [Fact]
    public void Tarix__PctFor_va_WithChange()
    {
        Assert.Equal("2026-11:60", DiscountCover.WithChange("", "2026-11", 60));
        Assert.Equal("2026-11:60;2026-12:30", DiscountCover.WithChange("2026-11:60", "2026-12", 30));
        Assert.Equal("2026-11:60", DiscountCover.WithChange("2026-11:60", "2026-12", 60));          // o'zgarmasa yozuv yo'q
        Assert.Equal("2026-11:60;2026-12:0", DiscountCover.WithChange("2026-11:60;2026-12:30", "2026-12", 0)); // shu oy qayta yoziladi
        Assert.Equal("2026-11:45", DiscountCover.WithChange("2026-11:60", "2026-11", 45));          // bir oyda tuzatish
        Assert.Equal("", DiscountCover.WithChange("", "2026-11", 0));
        Assert.Equal(0, DiscountCover.PctFor("2026-11:60;2026-12:30", "2026-10"));
        Assert.Equal(60, DiscountCover.PctFor("2026-11:60;2026-12:30", "2026-11"));
        Assert.Equal(30, DiscountCover.PctFor("2026-11:60;2026-12:30", "2027-03"));
        Assert.Equal(60, DiscountCover.PctFor("buzuq;2026-11:60;x:y", "2026-11"));                  // buzuq yozuv tashlanadi
    }

    /// <summary>Noyabr + dekabr hisoblari; registr qatoriga <paramref name="mutate"/> qo'llanadi.</summary>
    private static async Task<(MonthSalaryDto Nov, MonthSalaryDto Dec)> TwoMonthsAsync(Action<StudentDiscount> mutate)
    {
        using var db = TestDb.Sqlite();
        var (t, g, s) = await SetupAsync(db.Context, covers: true);
        db.Context.MonthlyCharges.Add(new MonthlyCharge
        {
            StudentId = s.Id, GroupId = g.Id, Month = "2026-12", Amount = 275_000m, Discount = 137_500m, Date = "2026-12-01",
        });
        mutate(db.Context.StudentDiscounts.Single());
        await db.Context.SaveChangesAsync();
        var months = (await SalaryLedger.BuildAsync(db.Context, t, "2026-11", "2026-12")).Months;
        return (months.Single(m => m.Month == "2026-11"), months.Single(m => m.Month == "2026-12"));
    }

    [Fact]
    public async Task Foiz_dekabrda_ozgarsa__NOYABR_maoshi_ozgarmaydi()
    {
        var (nov, dec) = await TwoMonthsAsync(d => { d.TeacherCoverPct = 30; d.TeacherCoverHistory = "2026-11:60;2026-12:30"; });
        Assert.Equal(82_500m, nov.DiscountCover);   // 137 500 × 60%
        Assert.Equal(41_250m, dec.DiscountCover);   // 137 500 × 30%
    }

    [Fact]
    public async Task Belgi_dekabrda_olinsa__NOYABR_ushlanmasi_qoladi()
    {
        var (nov, dec) = await TwoMonthsAsync(d =>
        {
            d.TeacherCovers = false; d.TeacherCoversFrom = ""; d.TeacherCoverPct = 0; d.TeacherCoverHistory = "2026-11:60;2026-12:0";
        });
        Assert.Equal(82_500m, nov.DiscountCover);
        Assert.Equal(0m, dec.DiscountCover);
    }

    [Fact]
    public async Task Chegirma_dekabrda_BEKOR_qilinsa__NOYABR_ushlanmasi_qoladi()
    {
        var (nov, _) = await TwoMonthsAsync(d =>
        {
            d.TeacherCoverHistory = "2026-11:60";
            d.Status = StudentDiscount.StatusCancelled; d.EndedAt = "2026-12-05T10:00:00";
        });
        Assert.Equal(82_500m, nov.DiscountCover);
    }

    [Fact]
    public async Task Bekor_qilingandan_KEYINGI_oyda_ushlanma_yoq()
    {
        var (nov, dec) = await TwoMonthsAsync(d =>
        {
            d.TeacherCoverHistory = "2026-11:60";
            d.Status = StudentDiscount.StatusCancelled; d.EndedAt = "2026-11-20T10:00:00";
        });
        Assert.Equal(82_500m, nov.DiscountCover);   // bekor qilingan oyning o'zi — o'sha oyda amalda bo'lgan
        Assert.Equal(0m, dec.DiscountCover);
    }

    [Fact]
    public async Task Kelmagan_darslar_chegirmasi_markazda_qoladi()
    {
        using var db = TestDb.Sqlite();
        var (t, g, s) = await SetupAsync(db.Context, covers: true);
        // Shu oy hisobidagi chegirmaning 37 500 qismi — kelmagan darslar chegirmasi (registr emas).
        db.Context.LessonDiscounts.Add(new LessonDiscount { StudentId = s.Id, GroupId = g.Id, Month = Month, Amount = 37_500m, CreatedAt = "2026-11-02" });
        await db.Context.SaveChangesAsync();
        var m = (await SalaryLedger.BuildAsync(db.Context, t, Month, Month)).Months.Single();
        Assert.Equal(60_000m, m.DiscountCover);           // (137 500 − 37 500) × 60%
    }
}

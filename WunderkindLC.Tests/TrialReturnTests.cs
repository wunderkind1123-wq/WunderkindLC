using Microsoft.EntityFrameworkCore;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// «SINOVGA QAYTARISH» (2026-11-13, egasi): o'quvchi guruhdan chiqadi, «Birinchi darsga yozilganlar»ga o'tadi, JORIY oydan
/// puli hisoblanmaydi (o'tgan oylar tegilmaydi), ketish hisoblanmaydi, tarixda qoladi. `membership-periods.md` §12.
/// </summary>
public class TrialReturnTests
{
    private sealed class NoHttpContext : Microsoft.AspNetCore.Http.IHttpContextAccessor
    {
        public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get => null; set { } }
    }

    [Theory]
    [InlineData("2026-09-05", "", "2026-11-13", "2026-10-31")]   // o'tgan oylarda o'qigan — davr o'tgan oy oxirida yopiladi
    [InlineData("2026-11-02", "", "2026-11-13", null)]           // joriy oyda aktivlashgan (xato kiritilgan) — davr YO'Q
    [InlineData("2026-09-05", "2026-10-10", "2026-11-13", "2026-10-10")] // muzlatilgan — muzlatish sanasida
    [InlineData("2026-09-05", "2026-11-05", "2026-11-13", "2026-10-31")] // joriy oyda muzlatilgan — o'tgan oy oxiriga qirqiladi
    [InlineData("", "", "2026-11-13", null)]                     // aktivlashmagan
    public void PeriodEnd__joriy_oy_hech_qachon_davrga_kirmaydi(string act, string frozen, string today, string? expected) =>
        Assert.Equal(expected, TrialReturnService.PeriodEnd(act, frozen, today));

    [Fact]
    public async Task Qaytarish__guruhdan_chiqadi_JORIY_oy_hisobi_bekor_OTGAN_oy_qoladi_yozuv_va_tarix()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        var today = AppClock.Today;
        var cur = today.ToString("yyyy-MM");
        var prev = today.AddMonths(-1).ToString("yyyy-MM");
        var s = new Student { FullName = "Ali", Balance = -400_000m, ClassName = "Beginner" };
        var g = new Group { Name = "Beginner", MonthlyFee = 200_000m };
        db.Students.Add(s);
        db.Classes.Add(g);
        var sg = new StudentGroup
        {
            StudentId = s.Id, GroupId = g.Id, IsActive = true, Status = "active",
            JoinedAt = prev + "-01", ActivatedAt = prev + "-01",
        };
        db.StudentGroups.Add(sg);
        db.MonthlyCharges.AddRange(
            new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = prev, Amount = 200_000m },
            new MonthlyCharge { StudentId = s.Id, GroupId = g.Id, Month = cur, Amount = 200_000m });
        await db.SaveChangesAsync();

        var audit = new AuditService(db, new NoHttpContext());
        var r = await TrialReturnService.ReturnAsync(db, audit, s, g, sg, null, "Xato kiritilgan", "u1", "Admin");
        await db.SaveChangesAsync();

        Assert.Equal(200_000m, r.Canceled);
        Assert.Equal(-200_000m, s.Balance);                                    // joriy oy qaytdi, o'tgan oy qarzi qoldi
        Assert.Equal([prev], await db.MonthlyCharges.Select(c => c.Month).ToListAsync());
        Assert.False(sg.IsActive);
        Assert.True(sg.ReturnedToTrial);
        Assert.Equal("trial", sg.Status);
        Assert.Equal(today.ToString("yyyy-MM-dd"), sg.LeftAt);
        Assert.Single(sg.PastPeriods, p => p.EndsWith(today.AddDays(-today.Day).ToString("yyyy-MM-dd"), StringComparison.Ordinal));
        var rec = await db.StudentTrialReturns.SingleAsync();
        Assert.Equal(("Beginner", "Xato kiritilgan", 200_000m), (rec.GroupName, rec.Reason, rec.CanceledAmount));
        Assert.Contains(await db.AuditLogs.Select(a => a.Summary).ToListAsync(), x => x.StartsWith("Sinovga qaytarildi"));
    }

    [Fact]
    public void Sinovga_qaytarish__KETISH_hisoblanmaydi()
    {
        var returned = new StudentGroup
        {
            StudentId = "s", GroupId = "g", JoinedAt = "2026-09-01", ActivatedAt = "", IsActive = false,
            LeftAt = "2026-11-13", Status = "trial", ReturnedToTrial = true, PastPeriods = ["2026-09-01|2026-10-31"],
        };
        var left = new StudentGroup
        {
            StudentId = "s2", GroupId = "g", JoinedAt = "2026-09-01", ActivatedAt = "2026-09-01", IsActive = false,
            LeftAt = "2026-11-13", Status = "active",
        };
        var events = DashboardSummary.LeftEvents([returned, left], new Dictionary<string, string>(), _ => true).ToList();
        Assert.Equal(["s2"], events.Select(e => e.StudentId));
    }

    [Fact]
    public void Royxatda_turadi__keyin_guruhga_qoshilsa_yoki_arxivlansa_chiqadi()
    {
        var rec = new StudentTrialReturn { CreatedAt = "2026-11-13T10:00:00" };
        // eski (parallel) guruhi — qaytarishdan oldin qo'shilgan: ochiq qoladi
        Assert.True(TrialReturnService.IsOpen(rec, false, [(true, "2026-09-01", "2026-09-01")]));
        // keyin yangi guruhga qo'shildi
        Assert.False(TrialReturnService.IsOpen(rec, false, [(true, "2026-11-14", "2026-11-14")]));
        // arxivlandi
        Assert.False(TrialReturnService.IsOpen(rec, true, []));
        rec.ResolvedAt = "2026-11-15T09:00:00";
        Assert.False(TrialReturnService.IsOpen(rec, false, []));
    }
}

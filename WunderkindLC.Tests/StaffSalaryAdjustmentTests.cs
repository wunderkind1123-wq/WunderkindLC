using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Xodim oyligiga QO'LDA bonus / jarima (egasi, 2026-11-10: «fiks summa + jarima qancha + bonus qancha, hisobotda
/// ko'rinsin»). Hisoblangan = asos (qat'iy oylik yoki KPI) + bonuslar − jarimalar, kamida 0. `billing.md` «Maosh reestri».
/// </summary>
public class StaffSalaryAdjustmentTests
{
    [Fact]
    public async Task Reestr__fiks_oylik_bonus_va_jarima_bilan__qoldiq_qayta_hisoblanadi()
    {
        using var db = TestDb.Sqlite();
        var ctx = db.Context;
        ctx.Users.AddRange(
            new AppUser { Id = "s1", FullName = "Administrator", Role = Roles.Staff, Email = "s1" },
            new AppUser { Id = "s2", FullName = "Operator", Role = Roles.Staff, Email = "s2" },
            new AppUser { Id = "a1", FullName = "Direktor", Role = Roles.Admin, Email = "a1" });
        ctx.StaffSalaries.Add(new StaffSalary { UserId = "s1", EffectiveFrom = "2026-09", Amount = 7_000_000m });
        ctx.StaffSalaryAdjustments.AddRange(
            new StaffSalaryAdjustment { UserId = "s1", Month = "2026-10", Kind = "bonus", Amount = 500_000m, Reason = "Reja bajarildi" },
            new StaffSalaryAdjustment { UserId = "s1", Month = "2026-10", Kind = "bonus", Amount = 300_000m, Reason = "Qo'shimcha ish" },
            new StaffSalaryAdjustment { UserId = "s1", Month = "2026-10", Kind = "fine", Amount = 200_000m, Reason = "Kechikish" },
            new StaffSalaryAdjustment { UserId = "s1", Month = "2026-09", Kind = "fine", Amount = 999_000m, Reason = "boshqa oy" },
            // maoshi yozilmagan admin — faqat bonusi bor: ro'yxatga chiqadi
            new StaffSalaryAdjustment { UserId = "a1", Month = "2026-10", Kind = "bonus", Amount = 1_000_000m, Reason = "Yillik mukofot" },
            // jarima asosdan katta — hisoblangan manfiy emas
            new StaffSalaryAdjustment { UserId = "s2", Month = "2026-10", Kind = "fine", Amount = 50_000m, Reason = "Xato hisobot" });
        ctx.FinanceTransactions.Add(new FinanceTransaction
        {
            Direction = "expense", Category = "salary", StaffUserId = "s1", Month = "2026-10", Amount = 7_000_000m,
            SalaryPayKind = "final", Date = "2026-11-03",
        });
        await ctx.SaveChangesAsync();

        var r = await PayrollRegister.BuildAsync(ctx, "2026-10");

        var admin = r.Staff.Single(x => x.Id == "s1");
        Assert.Equal((7_000_000m, 800_000m, 200_000m, 7_600_000m), (admin.Base, admin.Bonus, admin.Fine, admin.Accrued));
        Assert.Equal((600_000m, "partial"), (admin.Remaining, admin.Status));   // 7 mln berilgan — bonus qoldi
        var director = r.Staff.Single(x => x.Id == "a1");
        Assert.Equal((0m, 1_000_000m, 1_000_000m), (director.Base, director.Bonus, director.Accrued));
        Assert.Equal(0m, r.Staff.Single(x => x.Id == "s2").Accrued);
        Assert.Equal((1_800_000m, 250_000m), (r.Bonus, r.Fine));
    }

    [Theory]
    [InlineData("bonus", 100, "Sabab", "2026-10", null)]
    [InlineData("penalty", 100, "Sabab", "2026-10", "Turini")]
    [InlineData("fine", 0, "Sabab", "2026-10", "0 dan katta")]
    [InlineData("fine", 100, "  ", "2026-10", "Sababini")]
    [InlineData("fine", 100, "Sabab", "oktabr", "Oy noto'g'ri")]
    public void Kiritish_qoidalari(string kind, decimal amount, string reason, string month, string? error)
    {
        var res = PayrollRegister.ValidateAdjustment(kind, amount, reason, month);
        if (error is null) Assert.Null(res);
        else Assert.Contains(error, res);
    }

    [Fact]
    public void Hisoblangan_hech_qachon_manfiy_emas() =>
        Assert.Equal((7_600_000m, 0m), (PayrollRegister.Total(7_000_000m, 800_000m, 200_000m), PayrollRegister.Total(100m, 0m, 500m)));
}

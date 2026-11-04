using System.Security.Claims;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// Egasi (2026-11-04): «lid CRM'dan hech qachon o'chib ketmasligi kerak — faqat superadmin yoki direktor (admin roli)».
/// HARD ROL: ruxsat kaliti (`leads`, `leads.list:delete`) yetmaydi.
/// </summary>
public class LeadDeleteRightsTests
{
    private static ClaimsPrincipal User(string role, params string[] perms)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        claims.AddRange(perms.Select(p => new Claim("perm", p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Theory]
    [InlineData(Roles.SuperAdmin, true)]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Staff, false)]
    [InlineData(Roles.Teacher, false)]
    public void Faqat_superadmin_va_admin(string role, bool expected) =>
        Assert.Equal(expected, LeadDeleteRights.CanDelete(User(role)));

    [Fact]
    public void Xodimga_toliq_leads_ruxsati_ham_yetmaydi() =>
        Assert.False(LeadDeleteRights.CanDelete(User(Roles.Staff, "leads", "leads.list:delete")));

    [Fact]
    public void Server_ikkala_ochirish_yolida_tekshiradi()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var root = dir!.FullName;
        var leads = File.ReadAllText(Path.Combine(root, "WunderkindLC.Server", "Controllers", "LeadsController.cs"));
        var archive = File.ReadAllText(Path.Combine(root, "WunderkindLC.Server", "Controllers", "ArchiveController.cs"));
        Assert.Contains("LeadDeleteRights.CanDelete(User)", leads);
        Assert.Contains("LeadDeleteRights.CanDelete(User)", archive);
    }
}

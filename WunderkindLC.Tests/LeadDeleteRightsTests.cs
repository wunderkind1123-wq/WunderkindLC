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
    [InlineData(Roles.Admin, true)] // 2026-11-10 (kechqurun): direktor ham o'chiradi — faqat arxivlangan yozuvni
    [InlineData(Roles.Staff, false)]
    [InlineData(Roles.Teacher, false)]
    public void Faqat_superadmin_va_direktor(string role, bool expected) =>
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

        // 2026-11-10 (egasi): o'chirish FAQAT ARXIVLANGANDAN KEYIN — lid ham, o'quvchi ham (ikkala o'quvchi yo'li).
        Assert.Contains("if (!LeadLoss.IsLost(lead)) return BadRequest(new { message = LeadDeleteRights.LeadNotArchived });", leads);
        var students = File.ReadAllText(Path.Combine(root, "WunderkindLC.Server", "Controllers", "StudentsController.cs"));
        Assert.Contains("if (!LeadDeleteRights.CanDelete(User)) return StatusCode(403", students);
        Assert.Contains("if (!student.IsArchived) return BadRequest(new { message = LeadDeleteRights.StudentNotArchived });", students);
        var purge = File.ReadAllText(Path.Combine(root, "WunderkindLC.Server", "Controllers", "StudentPurgeController.cs"));
        Assert.Contains("LeadDeleteRights.StudentNotArchived", purge);
        Assert.Contains("or \"student\" && !LeadDeleteRights.CanDelete(User)", archive);
    }
}

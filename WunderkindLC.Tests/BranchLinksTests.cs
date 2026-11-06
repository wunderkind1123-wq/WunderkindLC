using WunderkindLC.Application.Services;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>Tepadagi filial almashtirgich ro'yxati (`Branches:Links`) — joriy filial hostdan, faqat https.</summary>
public class BranchLinksTests
{
    private const string Raw = "Yaypan|https://lc.wunderkindedu.uz; Furqat|https://furqat.wunderkindedu.uz/";

    [Fact]
    public void Joriy_filial_hostdan_aniqlanadi()
    {
        var links = BranchLinks.Parse(Raw, "furqat.wunderkindedu.uz");
        Assert.Equal(
            [new BranchLinks.Link("Yaypan", "https://lc.wunderkindedu.uz", false),
             new BranchLinks.Link("Furqat", "https://furqat.wunderkindedu.uz", true)],
            links);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Yaypan")]
    [InlineData("Yaypan|lc.wunderkindedu.uz")]
    [InlineData("X|javascript:alert(1)")]
    [InlineData("X|http://lc.wunderkindedu.uz")]
    [InlineData("|https://lc.wunderkindedu.uz")]
    public void Buzuq_yoki_https_bolmagan_qiymat_tashlanadi(string? raw) =>
        Assert.Empty(BranchLinks.Parse(raw, "lc.wunderkindedu.uz"));

    /// <summary>Test project does not reference the Server project, so the gate is checked on the source text
    /// (same approach as <see cref="WikiControllerGateTests"/>).</summary>
    [Fact]
    public void Ochiq_royxat_faqat_bitta_GET_va_admin_royxati_superadminda_qoladi()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WunderkindLC.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var controllers = Path.Combine(dir!.FullName, "WunderkindLC.Server", "Controllers");

        var open = File.ReadAllText(Path.Combine(controllers, "PublicBranchLinksController.cs"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(open, @"\[Http\w+"));
        Assert.Contains("[HttpGet]", open);

        var admin = File.ReadAllText(Path.Combine(controllers, "BranchLinksController.cs"));
        Assert.Contains("[Authorize(Roles = \"superadmin\")]", admin);
        Assert.DoesNotContain("AllowAnonymous", admin);
    }

    [Fact]
    public void Takror_manzil_bir_marta() =>
        Assert.Single(BranchLinks.Parse("A|https://a.uz;B|https://a.uz", "a.uz"));
}

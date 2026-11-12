using WunderkindLC.Application.Dtos;
using WunderkindLC.Application.Services;
using WunderkindLC.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WunderkindLC.Tests;

/// <summary>
/// CALL CENTER (2026-11-12, egasi): avtomatik kelgan lid (forma, daraja testi, landing, Instagram, Meta) Call Center
/// navbatiga tushadi; «Buyurtmalar» — faqat markazga kelganlar (2026-11-12 gacha bo'lgan lidlar ham). `crm-leads.md` «CALL CENTER».
/// </summary>
public class LeadCallCenterTests
{
    [Fact]
    public async Task Forma_orqali_kelgan_lid_CALL_CENTERga_tushadi()
    {
        using var t = TestDb.Sqlite();
        var db = t.Context;
        db.LeadForms.Add(new LeadForm
        {
            Title = "Instagram", Slug = "ig", Source = "Instagram", IsActive = true, CreatedAt = "2026-11-12T10:00:00",
        });
        db.LeadStages.Add(new LeadStage { Title = "Yangi", Order = 0 });
        await db.SaveChangesAsync();

        var (_, error) = await LeadFormService.SubmitAsync(db, "ig",
            new LeadFormSubmitRequest("Aliyev Ali", "+998901234567", null, 0, null, null, null));

        Assert.Null(error);
        var lead = await db.Leads.SingleAsync();
        Assert.True(lead.InCallCenter);
        Assert.Null(lead.ArrivedAt);
    }

    [Fact]
    public void Bosh_sahifa_Buyurtmalar_sanogi__Call_Center_lidlarini_SANAMAYDI()
    {
        var leads = new[]
        {
            new DashboardSummary.LeadRow("a", Converted: false),                     // eski / markazga kelgan
            new DashboardSummary.LeadRow("b", Converted: false, InCallCenter: true), // Call Center
            new DashboardSummary.LeadRow("c", Converted: true),
        };
        Assert.Equal(1, DashboardSummary.OpenLeads(leads, [], "2026-11-12"));
    }

    [Fact]
    public void Eski_lid_standart_holatda_BUYURTMALARDA()
    {
        // Migratsiya ustuni `false` — 2026-11-12 gacha bo'lgan lidlar Buyurtmalarda qoladi (backfill YO'Q).
        Assert.False(new Lead().InCallCenter);
    }
}

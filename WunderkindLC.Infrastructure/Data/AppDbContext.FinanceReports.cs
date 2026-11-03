using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Infrastructure.Data;

/// <summary>Pul oqimi 2.0 · P&amp;L 2.0 (<c>docs/finance-reports/</c>): hisobot toifalari va rejali xarajat shablonlari.
/// Mavjud jadvallarga TEGILMAYDI — faqat yangi jadvallar.</summary>
public partial class AppDbContext
{
    public DbSet<FinanceCategoryConfig> FinanceCategoryConfigs => Set<FinanceCategoryConfig>();
    public DbSet<PlannedExpenseTemplate> PlannedExpenseTemplates => Set<PlannedExpenseTemplate>();
    public DbSet<PlannedExpenseAccrual> PlannedExpenseAccruals => Set<PlannedExpenseAccrual>();

    partial void ConfigureFinanceReports(ModelBuilder b)
    {
        b.Entity<FinanceCategoryConfig>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(128);
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.TypeId).HasMaxLength(64);
            e.Property(x => x.SystemCategory).HasMaxLength(32);
            e.Property(x => x.Direction).HasMaxLength(8);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.CashFlowActivity).HasMaxLength(16);
            e.Property(x => x.PnlTreatment).HasMaxLength(24);
            e.Property(x => x.PnlGroup).HasMaxLength(120);
            e.Property(x => x.AutoReason).HasMaxLength(200);
        });

        b.Entity<PlannedExpenseTemplate>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.TypeId).HasMaxLength(64);
            e.Property(x => x.Category).HasMaxLength(32);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Frequency).HasMaxLength(16);
            e.Property(x => x.StartDate).HasMaxLength(10);
            e.Property(x => x.EndDate).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasIndex(x => new { x.IsDeleted, x.Status });
        });

        b.Entity<PlannedExpenseAccrual>(e =>
        {
            e.Property(x => x.TemplateId).HasMaxLength(64);
            e.Property(x => x.Month).HasMaxLength(7);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.CorrectedAmount).HasPrecision(18, 2);
            e.Property(x => x.CorrectionNote).HasMaxLength(500);
            e.HasIndex(x => new { x.TemplateId, x.Month }).IsUnique();
            e.HasIndex(x => x.Month);
        });
    }
}

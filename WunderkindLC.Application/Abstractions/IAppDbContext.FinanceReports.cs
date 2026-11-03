using Microsoft.EntityFrameworkCore;
using WunderkindLC.Domain;

namespace WunderkindLC.Application.Abstractions;

/// <summary>Pul oqimi 2.0 · P&amp;L 2.0 — hisobot toifalari va rejali xarajat shablonlari.</summary>
public partial interface IAppDbContext
{
    DbSet<FinanceCategoryConfig> FinanceCategoryConfigs { get; }
    DbSet<PlannedExpenseTemplate> PlannedExpenseTemplates { get; }
    DbSet<PlannedExpenseAccrual> PlannedExpenseAccruals { get; }
}

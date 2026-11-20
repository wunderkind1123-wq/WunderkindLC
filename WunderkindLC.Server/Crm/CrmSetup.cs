namespace WunderkindLC.Server.Crm;

/// <summary>
/// CRM upgrade wiring (docs/SPEC-crm-upgrade.md §0.2). <c>Program.cs</c> calls the five
/// <c>AddCrm&lt;Name&gt;</c> methods once in the DI section and <see cref="RunStartupAsync"/> once after
/// migrations and the existing seeds. Each phase edits ONLY its own <c>Crm&lt;Name&gt;Setup.cs</c>.
/// </summary>
public static class CrmSetup
{
    public static async Task RunStartupAsync(IServiceProvider services)
    {
        await CrmMessagingSetup.RunStartupAsync(services);
        await CrmAttendanceSetup.RunStartupAsync(services);
        await CrmRatingSetup.RunStartupAsync(services);
        await CrmWorkspaceSetup.RunStartupAsync(services);
        await CrmAccountsSetup.RunStartupAsync(services);
        await DropFutureAdvanceChargesAsync(services);
    }

    /// <summary>Bir martalik: avans ochgan KELGUSI oy hisoblari bekor qilinadi (`billing.md` «AVANS HISOBI»).</summary>
    private static async Task DropFutureAdvanceChargesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WunderkindLC.Application.Abstractions.IAppDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Billing");
        try
        {
            var n = await WunderkindLC.Application.Services.TuitionService.DropFutureAdvanceChargesOnceAsync(db);
            if (n is not null) log.LogInformation("[billing] kelgusi oy avans hisoblari bekor qilindi: {Count} ta", n);
        }
        catch (Exception ex) { log.LogWarning(ex, "[billing] kelgusi oy avans hisoblarini tozalab bo'lmadi"); }
    }
}

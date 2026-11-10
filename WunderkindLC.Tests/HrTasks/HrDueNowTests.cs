using WunderkindLC.Application.HrTasks;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.HrTasks;

/// <summary>
/// LC: «MUDDAT KELDI» xabari (`.claude/rules/hr-tasks.md` §10) — egasi: «topshiriq deadline kelsa botdan xabar borsin».
/// </summary>
public class HrDueNowTests
{
    // Seshanba 2031-03-11 15:00 Toshkent
    private static readonly DateTimeOffset Now = HrTasksKit.At(2031, 3, 11, 15, 0);

    [Fact]
    public async Task Muddati_hozir_tugagan_ochiq_topshiriq__ijrochiga_BIR_marta_xabar()
    {
        using var kit = new HrTasksKit();
        var p = await kit.PersonAsync(telegram: true);
        await kit.TaskAsync(p.Employee!.Id, Now.AddMinutes(-5), title: "Qo'ng'iroq: Vali");

        Assert.Equal(1, await kit.Service(Now).SendDueNowAsync(Now));
        var text = Assert.Single(kit.Sender.To(p.Chat!.Value));
        Assert.Contains("muddati keldi", text);
        Assert.Contains("Qo'ng'iroq: Vali", text);

        // keyingi sikl — takrorlanmaydi
        Assert.Equal(0, await kit.Service(Now.AddMinutes(5)).SendDueNowAsync(Now.AddMinutes(5)));
        Assert.Single(kit.Sender.To(p.Chat!.Value));
    }

    [Fact]
    public async Task Eski_kechikkan_bajarilgan_va_hali_muddati_kelmagan__xabarsiz()
    {
        using var kit = new HrTasksKit();
        var p = await kit.PersonAsync(telegram: true);
        await kit.TaskAsync(p.Employee!.Id, Now.AddHours(-2), title: "eski");                       // oynadan tashqari
        await kit.TaskAsync(p.Employee!.Id, Now.AddMinutes(-5), status: HrTaskStatuses.Done,
            completedAt: Now.AddMinutes(-10), comment: "bo'ldi", title: "bajarilgan");
        await kit.TaskAsync(p.Employee!.Id, Now.AddMinutes(20), title: "kelgusi");

        Assert.Equal(0, await kit.Service(Now).SendDueNowAsync(Now));
        Assert.Empty(kit.Sender.To(p.Chat!.Value));
    }

    [Fact]
    public async Task Kun_oxiri_topshirigi_va_ochirilgan_eslatmalar__xabarsiz()
    {
        using var kit = new HrTasksKit();
        var p = await kit.PersonAsync(telegram: true);
        var endOfDay = HrTasksKit.At(2031, 3, 11, 23, 59);
        await kit.TaskAsync(p.Employee!.Id, endOfDay, title: "kun oxiri");
        // yarim tunda xabar yo'q (RemindEndOfDay standart o'chiq)
        Assert.Equal(0, await kit.Service(endOfDay.AddMinutes(1)).SendDueNowAsync(endOfDay.AddMinutes(1)));

        await kit.TaskAsync(p.Employee!.Id, Now.AddMinutes(-1), title: "oddiy");
        await using (var ctx = kit.NewContext())
        {
            var settings = await kit.Service(Now, ctx).GetOrCreateSettingsAsync();
            settings.ReminderEnabled = false;
            await ctx.SaveChangesAsync();
        }
        Assert.Equal(0, await kit.Service(Now).SendDueNowAsync(Now));
        Assert.Empty(kit.Sender.To(p.Chat!.Value));
    }
}

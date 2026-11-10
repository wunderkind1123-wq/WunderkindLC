using Microsoft.Extensions.Logging.Abstractions;
using WunderkindLC.Application.HrTasks;
using WunderkindLC.Domain;
using Xunit;

namespace WunderkindLC.Tests.HrTasks;

/// <summary>
/// LC (egasi, 2026-11-10): «16:00 da bajarilmagan topshiriqlar ro'yxati borsin» (`hr-tasks.md` §11) va
/// «faqat yarim soat oldin eslatilsin» (§10).
/// </summary>
public class HrAfternoonListTests
{
    // Seshanba 2031-03-11 16:00 Toshkent
    private static readonly DateTimeOffset At16 = HrTasksKit.At(2031, 3, 11, 16, 0);

    [Fact]
    public async Task Kunduzgi_royxat__faqat_bajarilmaganlar_kechikkani_bilan_bosh_royxat_yuborilmaydi()
    {
        using var kit = new HrTasksKit();
        var busy = await kit.PersonAsync(telegram: true);
        var free = await kit.PersonAsync(telegram: true);
        await kit.TaskAsync(busy.Employee!.Id, HrTasksKit.At(2031, 3, 10, 12, 0), title: "Kechagi hisobot");
        await kit.TaskAsync(busy.Employee!.Id, HrTasksKit.At(2031, 3, 11, 18, 0), title: "Qo'ng'iroqlar");
        await kit.TaskAsync(busy.Employee!.Id, HrTasksKit.At(2031, 3, 11, 12, 0), status: HrTaskStatuses.Done,
            completedAt: At16.AddHours(-3), comment: "bo'ldi", title: "Bajarilgan ish");
        await kit.TaskAsync(free.Employee!.Id, HrTasksKit.At(2031, 3, 11, 12, 0), status: HrTaskStatuses.Done,
            completedAt: At16.AddHours(-3), comment: "bo'ldi", title: "Hammasi tayyor");

        Assert.Equal(1, await kit.Service(At16).SendAfternoonListsAsync(new DateOnly(2031, 3, 11), At16));

        var text = Assert.Single(kit.Sender.To(busy.Chat!.Value));
        Assert.Contains("Hali bajarilmagan topshiriqlar — 2 ta", text);
        Assert.Contains("Kechagi hisobot", text);
        Assert.Contains("MUDDATI O‘TGAN", text);
        Assert.Contains("Qo'ng'iroqlar", text);
        Assert.DoesNotContain("Bajarilgan ish", text);
        Assert.Empty(kit.Sender.To(free.Chat!.Value));
    }

    [Fact]
    public async Task Kunduzgi_royxat_ochirilsa__xabarsiz()
    {
        using var kit = new HrTasksKit();
        var p = await kit.PersonAsync(telegram: true);
        await kit.TaskAsync(p.Employee!.Id, HrTasksKit.At(2031, 3, 11, 18, 0), title: "Qo'ng'iroqlar");
        await using (var ctx = kit.NewContext())
        {
            var settings = await kit.Service(At16, ctx).GetOrCreateSettingsAsync();
            settings.AfternoonListEnabled = false;
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(0, await kit.Service(At16).SendAfternoonListsAsync(new DateOnly(2031, 3, 11), At16));
        Assert.Empty(kit.Sender.To(p.Chat!.Value));
    }

    [Fact]
    public async Task Rejalashtiruvchi__16_00_dan_keyin_bir_marta_kechqurun_kechikib_yubormaydi()
    {
        using var kit = new HrTasksKit();
        var p = await kit.PersonAsync(telegram: true);
        await kit.TaskAsync(p.Employee!.Id, HrTasksKit.At(2031, 3, 11, 18, 0), title: "Qo'ng'iroqlar");
        var scheduler = new HrTasksScheduler(kit.Scopes(), NullLogger<HrTasksScheduler>.Instance);

        Assert.DoesNotContain("afternoon_sent", (await scheduler.TickAsync(At16.AddMinutes(-5))).Keys);
        Assert.Equal(1, (await scheduler.TickAsync(At16.AddMinutes(5)))["afternoon_sent"]);
        Assert.DoesNotContain("afternoon_sent", (await scheduler.TickAsync(At16.AddMinutes(10))).Keys); // kuniga bir marta

        // ertasi kuni server faqat 20:00 da yondi — 4 soat kechikkan «hali bajarilmagan» xabari yuborilmaydi
        Assert.DoesNotContain("afternoon_sent", (await scheduler.TickAsync(HrTasksKit.At(2031, 3, 12, 20, 0))).Keys);
    }

    [Fact]
    public async Task Sukut_boyicha_eslatma_FAQAT_yarim_soat_oldin()
    {
        Assert.Equal([30], new HrSettings().ReminderOffsets.Select(x => (int)x));
        Assert.Equal(new TimeOnly(16, 0), new HrSettings().AfternoonTime);

        using var kit = new HrTasksKit();
        var p = await kit.PersonAsync(telegram: true);
        var due = HrTasksKit.At(2031, 3, 11, 15, 0);
        await kit.TaskAsync(p.Employee!.Id, due, title: "Uchrashuv");

        Assert.Equal(0, await kit.Service(due.AddMinutes(-110)).SendRemindersAsync(due.AddMinutes(-110))); // 2 soat oldin — yo'q
        Assert.Equal(0, await kit.Service(due.AddMinutes(-55)).SendRemindersAsync(due.AddMinutes(-55)));   // 1 soat oldin — yo'q
        Assert.Equal(1, await kit.Service(due.AddMinutes(-25)).SendRemindersAsync(due.AddMinutes(-25)));   // yarim soat oldin
        Assert.Single(kit.Sender.To(p.Chat!.Value));
    }
}

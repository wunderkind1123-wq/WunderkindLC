using WunderkindLC.Domain;

namespace WunderkindLC.Application.Services.Messaging;

/// <summary>
/// WHEN a scheduled auto-message rule runs (SPEC P2.3, pure, <c>PaymentReminderScheduleTests</c>).
/// <list type="bullet">
///   <item><c>monthly_day_time</c> — <c>payment_debt</c>, <c>rating_top</c>, <c>rating_bottom</c>: each rule has its own
///     day of month (1..31, clamped to the month end) and time;</item>
///   <item><c>daily_time</c> — <c>birthday</c>: every day after the rule's time;</item>
///   <item><c>custom</c> — <c>custom_schedule</c> (its own daily/monthly window, <c>CustomReminderService</c>);</item>
///   <item><c>none</c> — event-driven triggers.</item>
/// </list>
/// </summary>
public static class AutoMessageSchedule
{
    public const string MonthlyDayTime = "monthly_day_time";
    public const string DailyTime = "daily_time";
    public const string Custom = "custom";
    public const string None = "none";

    /// <summary>Default time when a rule's <c>ScheduleTime</c> cannot be parsed (data SQL sets 09:00).</summary>
    public static readonly TimeSpan DefaultTime = new(9, 0, 0);

    public static string ModeOf(string? trigger) => trigger switch
    {
        AutoMessageTriggers.PaymentDebt or AutoMessageTriggers.RatingTop or AutoMessageTriggers.RatingBottom => MonthlyDayTime,
        AutoMessageTriggers.Birthday => DailyTime,
        AutoMessageTriggers.CustomSchedule => Custom,
        _ => None,
    };

    /// <summary>Parses <c>HH:mm</c> (falls back to <see cref="DefaultTime"/>).</summary>
    public static TimeSpan TimeOf(AutoMessageRule rule) =>
        TryParseTime(rule.ScheduleTime, out var t) ? t : DefaultTime;

    /// <summary>Strict <c>HH:mm</c> (00:00..23:59) — used by validation.</summary>
    public static bool TryParseTime(string? value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Trim().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h is < 0 or > 23 || m is < 0 or > 59) return false;
        time = new TimeSpan(h, m, 0);
        return true;
    }

    /// <summary>Is the rule due at <paramref name="now"/> (centre time)? For <c>monthly_day_time</c>: today is the
    /// rule's day (31 → last day of a short month) and the time has passed; <c>daily_time</c>: the time has passed.
    /// Other modes are never "due" here.</summary>
    public static bool IsDue(AutoMessageRule rule, DateTime now)
    {
        var mode = ModeOf(rule.Trigger);
        var today = DateOnly.FromDateTime(now);
        return mode switch
        {
            MonthlyDayTime => IsScheduledDay(rule, today) && now.TimeOfDay >= TimeOf(rule),
            DailyTime => now.TimeOfDay >= TimeOf(rule),
            _ => false,
        };
    }

    /// <summary>Ko'pi bilan shuncha kun (oyning har kuni).</summary>
    public const int MaxDays = 31;

    /// <summary>Bir nechta kun qabul qiladigan hodisalar (hozircha faqat qarzdorlik eslatmasi).</summary>
    public static bool SupportsMultipleDays(string? trigger) => trigger == AutoMessageTriggers.PaymentDebt;

    /// <summary>
    /// Qoidaning oy kunlari (o'sib boruvchi, takrorsiz, 1..31): <see cref="AutoMessageRule.ScheduleDays"/> bo'sh bo'lmasa
    /// va hodisa bir nechta kunni qabul qilsa — o'sha ro'yxat, aks holda eski bitta kun (<c>ScheduleDayOfMonth</c>).
    /// </summary>
    public static IReadOnlyList<int> DaysOf(AutoMessageRule rule)
    {
        if (SupportsMultipleDays(rule.Trigger) && ParseDays(rule.ScheduleDays) is { Count: > 0 } days) return days;
        return new[] { Math.Clamp(rule.ScheduleDayOfMonth, 1, 31) };
    }

    /// <summary>"1, 5,10" → [1,5,10]; noto'g'ri bo'lak va 1..31 dan tashqarisi tashlanadi.</summary>
    public static List<int> ParseDays(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => int.TryParse(p, out var d) ? d : 0)
            .Where(d => d is >= 1 and <= 31)
            .Distinct().OrderBy(d => d).ToList();

    /// <summary>Saqlash shakli: [10,1,5] → "1,5,10".</summary>
    public static string FormatDays(IEnumerable<int> days) =>
        string.Join(",", days.Where(d => d is >= 1 and <= 31).Distinct().OrderBy(d => d));

    /// <summary>Bugun qoidaning kunlaridan birimi. 29–31 qisqa oyda OYNING OXIRGI KUNIGA tushadi (o'tkazib yuborilmaydi;
    /// bir nechtasi bir kunga tushsa — bitta yuborish, ish belgisi kun bo'yicha).</summary>
    public static bool IsScheduledDay(AutoMessageRule rule, DateOnly today)
    {
        var dim = DateTime.DaysInMonth(today.Year, today.Month);
        return DaysOf(rule).Any(d => Math.Min(d, dim) == today.Day);
    }

    /// <summary>Foydalanuvchiga: "Har oyning 1, 5, 10-kunlari, 09:00 (oyiga 3 marta)".</summary>
    public static string DescribeMonthly(AutoMessageRule rule)
    {
        var days = DaysOf(rule);
        return days.Count == 1
            ? $"Har oyning {days[0]}-kuni, {rule.ScheduleTime}"
            : $"Har oyning {string.Join(", ", days)}-kunlari, {rule.ScheduleTime} (oyiga {days.Count} marta)";
    }
}

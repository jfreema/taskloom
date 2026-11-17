using Taskloom.Domain;

namespace Taskloom.Application;

/// <summary>
/// The dates of repeat rules (<see cref="RepeatRule"/>). Pure calculation: "today" is the caller's, which the API takes
/// from the browser so it is the person's own date, not the server's.
/// </summary>
public static class Recurrence
{
    public const int MaxEvery = 365;

    /// <summary>A late completion skips missed dates one by one up to this many, then starts again from today.</summary>
    private const int MaxSkips = 10_000;

    private const WeekdaySet AllDays = (WeekdaySet)0b111_1111;

    public static WeekdaySet Flag(DayOfWeek day) => (WeekdaySet)(1 << (int)day);

    public static bool Has(WeekdaySet days, DayOfWeek day) => (days & Flag(day)) != 0;

    /// <summary>
    /// The rule with only the parts that fit its unit and basis. Parts a rule on schedule needs but doesn't have come
    /// from <paramref name="anchor"/> (the task's due date, else today): a weekly rule with no days falls on the anchor's
    /// weekday, a monthly one on the anchor's day of the month (or, given only which week, the anchor's weekday).
    /// </summary>
    public static RepeatRule Normalize(RepeatRule rule, DateOnly anchor)
    {
        var bare = rule with
        {
            Every = Math.Clamp(rule.Every, 1, MaxEvery),
            Weekdays = WeekdaySet.None, MonthDay = null, MonthWeek = null, MonthWeekday = null,
        };
        if (rule.Basis == RepeatBasis.Completion) return bare;
        return rule.Unit switch
        {
            RepeatUnit.Week => bare with { Weekdays = (rule.Weekdays & AllDays) == WeekdaySet.None ? Flag(anchor.DayOfWeek) : rule.Weekdays & AllDays },
            RepeatUnit.Month when rule.MonthWeek is { } week => bare with { MonthWeek = week, MonthWeekday = rule.MonthWeekday ?? anchor.DayOfWeek },
            RepeatUnit.Month => bare with { MonthDay = rule.MonthDay ?? anchor.Day },
            _ => bare,
        };
    }

    /// <summary>
    /// The first date on or after <paramref name="today"/> that a rule on schedule falls on: the due date a task without
    /// one gets when the rule is set. Null for a rule counted from completion, or one that ended before then.
    /// </summary>
    public static DateOnly? First(RepeatRule rule, DateOnly today)
    {
        if (rule.Basis == RepeatBasis.Completion) return null;
        try
        {
            // A series starts on the first date that fits; the interval counts from there.
            return Within(rule, On(rule with { Every = 1 }, today, inclusive: true));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// The due date of the task that completing a repeating task creates, or null once the rule has ended.
    /// On schedule: the rule's next date after the due date (after today when there is none), skipping dates already
    /// past, so a task completed late doesn't come back overdue. From completion: today plus the interval.
    /// </summary>
    public static DateOnly? Next(RepeatRule rule, DateOnly? due, DateOnly today)
    {
        try
        {
            DateOnly next;
            if (rule.Basis == RepeatBasis.Completion)
            {
                next = Add(today, rule.Unit, Math.Clamp(rule.Every, 1, MaxEvery));
            }
            else
            {
                // Without a due date there is nothing to count the interval from: the series starts again at the
                // first date after today that fits.
                next = due is { } d ? On(rule, d, inclusive: false) : On(rule with { Every = 1 }, today, inclusive: false);
                for (var skipped = 0; next < today; skipped++)
                    next = skipped < MaxSkips ? On(rule, next, inclusive: false) : On(rule, today, inclusive: true);
            }
            return Within(rule, next);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Past the year 9999.
            return null;
        }
    }

    private static DateOnly? Within(RepeatRule rule, DateOnly date) => rule.Until is { } until && date > until ? null : date;

    private static DateOnly Add(DateOnly date, RepeatUnit unit, int every) => unit switch
    {
        RepeatUnit.Day => date.AddDays(every),
        RepeatUnit.Week => date.AddDays(7 * every),
        RepeatUnit.Month => date.AddMonths(every),
        RepeatUnit.Year => date.AddYears(every),
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    /// <summary>
    /// The first date the rule falls on after <paramref name="from"/> (or on it, when <paramref name="inclusive"/>),
    /// counting intervals from <paramref name="from"/>'s own day, week (Monday to Sunday), month or year.
    /// </summary>
    private static DateOnly On(RepeatRule rule, DateOnly from, bool inclusive)
    {
        var every = Math.Clamp(rule.Every, 1, MaxEvery);
        bool Fits(DateOnly d) => inclusive ? d >= from : d > from;
        switch (rule.Unit)
        {
            case RepeatUnit.Day:
                return inclusive ? from : from.AddDays(every);
            case RepeatUnit.Year:
                return inclusive ? from : from.AddYears(every);
            case RepeatUnit.Week:
            {
                var days = rule.Weekdays & AllDays;
                if (days == WeekdaySet.None) days = Flag(from.DayOfWeek);
                var monday = from.AddDays(-(((int)from.DayOfWeek + 6) % 7));
                for (var i = 0; i < 7; i++)
                {
                    var d = monday.AddDays(i);
                    if (Fits(d) && Has(days, d.DayOfWeek)) return d;
                }
                var later = monday.AddDays(7 * every);
                for (var i = 0; ; i++)
                {
                    var d = later.AddDays(i);
                    if (Has(days, d.DayOfWeek)) return d;
                }
            }
            case RepeatUnit.Month:
            {
                var month = new DateOnly(from.Year, from.Month, 1);
                var here = InMonth(rule, month, from);
                return Fits(here) ? here : InMonth(rule, month.AddMonths(every), from);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(rule));
        }
    }

    /// <summary>The day a monthly rule falls on in the month starting <paramref name="first"/>.</summary>
    private static DateOnly InMonth(RepeatRule rule, DateOnly first, DateOnly anchor)
    {
        var length = DateTime.DaysInMonth(first.Year, first.Month);
        if (rule.MonthWeek is { } week)
        {
            var weekday = rule.MonthWeekday ?? anchor.DayOfWeek;
            if (week < 0)
            {
                var last = first.AddDays(length - 1);
                return last.AddDays(-(((int)last.DayOfWeek - (int)weekday + 7) % 7));
            }
            var offset = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(offset + 7 * (Math.Clamp(week, 1, 4) - 1));
        }
        var day = rule.MonthDay ?? anchor.Day;
        return first.AddDays((day < 0 ? length : Math.Clamp(day, 1, length)) - 1);
    }
}

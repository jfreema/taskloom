using Taskloom.Application;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>The dates of repeat rules (Taskloom.Application Recurrence), without the API.</summary>
public class RecurrenceTests
{
    private static DateOnly D(string s) => DateOnly.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    private static RepeatRule Rule(RepeatUnit unit, int every = 1, WeekdaySet days = WeekdaySet.None, int? monthDay = null,
        int? monthWeek = null, DayOfWeek? monthWeekday = null, RepeatBasis basis = RepeatBasis.Schedule, string? until = null) =>
        new()
        {
            Unit = unit, Every = every, Weekdays = days, MonthDay = monthDay, MonthWeek = monthWeek, MonthWeekday = monthWeekday,
            Basis = basis, Until = until is null ? null : D(until),
        };

    private static string? Next(RepeatRule rule, string? due, string today) =>
        Recurrence.Next(rule, due is null ? null : D(due), D(today))?.ToString("yyyy-MM-dd");

    private static string? First(RepeatRule rule, string today) => Recurrence.First(rule, D(today))?.ToString("yyyy-MM-dd");

    [Fact]
    public void Daily_rules_count_from_the_due_date_and_skip_days_already_past()
    {
        // 2026-11-16 is a Monday.
        Assert.Equal("2026-11-17", Next(Rule(RepeatUnit.Day), "2026-11-16", "2026-11-16"));
        Assert.Equal("2026-11-19", Next(Rule(RepeatUnit.Day, every: 3), "2026-11-16", "2026-11-16"));
        // Completed early: still the day after the due date.
        Assert.Equal("2026-11-21", Next(Rule(RepeatUnit.Day), "2026-11-20", "2026-11-16"));
        // Completed late: the missed days are skipped, and today's task is still to do.
        Assert.Equal("2026-11-16", Next(Rule(RepeatUnit.Day), "2026-11-10", "2026-11-16"));
        Assert.Equal("2026-11-18", Next(Rule(RepeatUnit.Day, every: 4), "2026-11-10", "2026-11-16"));
        // Very late: never overdue, however far back the due date is.
        Assert.Equal("2026-11-16", Next(Rule(RepeatUnit.Day), "1900-01-01", "2026-11-16"));
    }

    [Fact]
    public void Weekly_rules_fall_on_their_weekdays_counting_weeks_from_the_due_dates_week()
    {
        var monWed = Rule(RepeatUnit.Week, days: WeekdaySet.Monday | WeekdaySet.Wednesday);
        Assert.Equal("2026-11-18", Next(monWed, "2026-11-16", "2026-11-16"));
        Assert.Equal("2026-11-23", Next(monWed, "2026-11-18", "2026-11-18"));
        // Every 2 weeks on Friday, completed early: two weeks after the due Friday.
        Assert.Equal("2026-12-04", Next(Rule(RepeatUnit.Week, every: 2, days: WeekdaySet.Friday), "2026-11-20", "2026-11-16"));
        // Every 2 weeks on Monday and Friday: Friday of the same week, then Monday two weeks on.
        var fortnightly = Rule(RepeatUnit.Week, every: 2, days: WeekdaySet.Monday | WeekdaySet.Friday);
        Assert.Equal("2026-11-20", Next(fortnightly, "2026-11-16", "2026-11-16"));
        Assert.Equal("2026-11-30", Next(fortnightly, "2026-11-20", "2026-11-20"));
        // Without a due date the series starts at the first fitting day after today, whatever the interval.
        Assert.Equal("2026-11-23", Next(Rule(RepeatUnit.Week, days: WeekdaySet.Monday), null, "2026-11-18"));
        Assert.Equal("2026-11-23", Next(Rule(RepeatUnit.Week, every: 2, days: WeekdaySet.Monday), null, "2026-11-18"));
    }

    [Fact]
    public void Monthly_rules_keep_their_day_through_short_months_and_find_nth_and_last_weekdays()
    {
        var on31st = Rule(RepeatUnit.Month, monthDay: 31);
        Assert.Equal("2027-02-28", Next(on31st, "2027-01-31", "2027-01-31"));
        // The rule remembers the 31st, so a short month doesn't drag later months back.
        Assert.Equal("2027-03-31", Next(on31st, "2027-02-28", "2027-02-28"));
        Assert.Equal("2026-12-31", Next(Rule(RepeatUnit.Month, monthDay: -1), "2026-11-30", "2026-11-30"));
        Assert.Equal("2027-03-01", Next(Rule(RepeatUnit.Month, every: 3, monthDay: 1), "2026-12-01", "2026-12-01"));
        // The 3rd Monday: Nov 16, then Dec 21. The last Friday: Nov 27, then Dec 25.
        Assert.Equal("2026-12-21", Next(Rule(RepeatUnit.Month, monthWeek: 3, monthWeekday: DayOfWeek.Monday), "2026-11-16", "2026-11-16"));
        Assert.Equal("2026-12-25", Next(Rule(RepeatUnit.Month, monthWeek: -1, monthWeekday: DayOfWeek.Friday), "2026-11-27", "2026-11-27"));
        // A due date moved off the pattern: the rule's day in the same month, if still ahead.
        Assert.Equal("2026-11-20", Next(Rule(RepeatUnit.Month, monthDay: 20), "2026-11-05", "2026-11-05"));
    }

    [Fact]
    public void Yearly_rules_and_rules_counted_from_completion()
    {
        Assert.Equal("2027-11-16", Next(Rule(RepeatUnit.Year), "2026-11-16", "2026-11-16"));
        Assert.Equal("2029-02-28", Next(Rule(RepeatUnit.Year), "2028-02-29", "2028-02-29"));
        // From completion: today plus the interval, whatever the due date was.
        Assert.Equal("2026-11-23", Next(Rule(RepeatUnit.Week, basis: RepeatBasis.Completion), "2026-11-01", "2026-11-16"));
        Assert.Equal("2027-01-16", Next(Rule(RepeatUnit.Month, every: 2, basis: RepeatBasis.Completion), null, "2026-11-16"));
        Assert.Equal("2026-11-19", Next(Rule(RepeatUnit.Day, every: 3, basis: RepeatBasis.Completion), "2026-12-25", "2026-11-16"));
    }

    [Fact]
    public void Rules_end_after_their_until_date_and_past_the_last_representable_year()
    {
        Assert.Null(Next(Rule(RepeatUnit.Day, until: "2026-11-16"), "2026-11-16", "2026-11-16"));
        Assert.Equal("2026-11-17", Next(Rule(RepeatUnit.Day, until: "2026-11-17"), "2026-11-16", "2026-11-16"));
        Assert.Null(Next(Rule(RepeatUnit.Year), "9999-06-01", "9999-06-01"));
        Assert.Null(Next(Rule(RepeatUnit.Day), "9999-12-31", "9999-12-31"));
    }

    [Fact]
    public void First_is_the_first_fitting_date_from_today()
    {
        Assert.Equal("2026-11-16", First(Rule(RepeatUnit.Day, every: 3), "2026-11-16"));
        Assert.Equal("2026-11-18", First(Rule(RepeatUnit.Week, days: WeekdaySet.Wednesday), "2026-11-16"));
        Assert.Equal("2026-11-23", First(Rule(RepeatUnit.Week, every: 2, days: WeekdaySet.Monday), "2026-11-18"));
        Assert.Equal("2026-11-16", First(Rule(RepeatUnit.Month, monthDay: 16), "2026-11-16"));
        Assert.Equal("2026-12-01", First(Rule(RepeatUnit.Month, every: 3, monthDay: 1), "2026-11-16"));
        Assert.Equal("2026-11-30", First(Rule(RepeatUnit.Month, monthWeek: -1, monthWeekday: DayOfWeek.Monday), "2026-11-16"));
        Assert.Null(First(Rule(RepeatUnit.Week, basis: RepeatBasis.Completion), "2026-11-16"));
        Assert.Null(First(Rule(RepeatUnit.Month, monthDay: 1, until: "2026-11-30"), "2026-11-16"));
    }

    [Fact]
    public void Normalize_keeps_only_the_parts_that_fit_and_fills_the_missing_ones_from_the_anchor()
    {
        var wednesday = D("2026-11-18");
        Assert.Equal(WeekdaySet.Wednesday, Recurrence.Normalize(Rule(RepeatUnit.Week), wednesday).Weekdays);
        Assert.Equal(18, Recurrence.Normalize(Rule(RepeatUnit.Month), wednesday).MonthDay);
        var nth = Recurrence.Normalize(Rule(RepeatUnit.Month, monthWeek: 3), wednesday);
        Assert.Equal((null, 3, DayOfWeek.Wednesday), (nth.MonthDay, nth.MonthWeek, nth.MonthWeekday));
        var afterDone = Recurrence.Normalize(Rule(RepeatUnit.Week, days: WeekdaySet.Friday, basis: RepeatBasis.Completion), wednesday);
        Assert.Equal(WeekdaySet.None, afterDone.Weekdays);
        var daily = Recurrence.Normalize(Rule(RepeatUnit.Day, days: WeekdaySet.Friday, monthDay: 3), wednesday);
        Assert.Equal(Rule(RepeatUnit.Day), daily);
    }
}

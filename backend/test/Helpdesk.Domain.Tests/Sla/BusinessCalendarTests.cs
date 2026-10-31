using System;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Helpdesk.Sla;

public class BusinessCalendarTests
{
    // 2026-11-02 is a Monday. Opening hours 08:00-17:00 UTC, Monday to Friday.
    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    private static BusinessCalendar Calendar(Action<BusinessHoursOptions>? configure = null)
    {
        var options = new BusinessHoursOptions { TimeZone = "UTC" };
        configure?.Invoke(options);
        return new BusinessCalendar(Options.Create(options));
    }

    [Fact]
    public void Time_inside_one_working_day_is_added_directly()
    {
        Calendar().AddBusinessHours(Utc(2, 9), 2).ShouldBe(Utc(2, 11));
    }

    [Fact]
    public void Time_left_over_after_closing_continues_the_next_morning()
    {
        // 15:00 + 4h: two hours left today, the other two start at 08:00 on Tuesday
        Calendar().AddBusinessHours(Utc(2, 15), 4).ShouldBe(Utc(3, 10));
    }

    [Fact]
    public void A_full_day_ends_exactly_at_closing_time()
    {
        Calendar().AddBusinessHours(Utc(2, 9), 8).ShouldBe(Utc(2, 17));
    }

    [Fact]
    public void The_weekend_is_skipped()
    {
        // Friday 16:00 + 3h: one hour on Friday, two on Monday morning
        Calendar().AddBusinessHours(Utc(6, 16), 3).ShouldBe(Utc(9, 10));
    }

    [Fact]
    public void Starting_on_a_weekend_starts_the_clock_on_monday_morning()
    {
        Calendar().AddBusinessHours(Utc(7, 10), 1).ShouldBe(Utc(9, 9));
    }

    [Fact]
    public void Starting_before_opening_waits_for_opening()
    {
        Calendar().AddBusinessHours(Utc(2, 7), 1).ShouldBe(Utc(2, 9));
    }

    [Fact]
    public void Starting_after_closing_waits_for_the_next_morning()
    {
        Calendar().AddBusinessHours(Utc(2, 18), 1).ShouldBe(Utc(3, 9));
    }

    [Fact]
    public void Holidays_are_skipped()
    {
        var calendar = Calendar(options => options.Holidays = new[] { "2026-11-03" });

        // Monday 16:00 + 2h: one hour on Monday, Tuesday is a holiday, the rest is Wednesday 09:00
        calendar.AddBusinessHours(Utc(2, 16), 2).ShouldBe(Utc(4, 9));
    }

    [Fact]
    public void Zero_hours_still_moves_a_closed_moment_to_the_next_opening()
    {
        Calendar().AddBusinessHours(Utc(7, 12), 0).ShouldBe(Utc(9, 8));
    }

    [Fact]
    public void Custom_working_days_are_respected()
    {
        var calendar = Calendar(options => options.WorkDays = new[] { DayOfWeek.Saturday });

        // Monday 09:00 + 1h: the only working day is Saturday
        calendar.AddBusinessHours(Utc(2, 9), 1).ShouldBe(Utc(7, 9));
    }

    [Fact]
    public void Rules_are_applied_in_the_configured_time_zone()
    {
        var calendar = Calendar(options => options.TimeZone = "Asia/Ho_Chi_Minh");

        // 02:00 UTC is 09:00 in Ho Chi Minh City (UTC+7); two working hours later it is 11:00 local = 04:00 UTC
        calendar.AddBusinessHours(Utc(2, 2), 2).ShouldBe(Utc(2, 4));
    }

    [Fact]
    public void An_unknown_time_zone_falls_back_to_utc()
    {
        Calendar(options => options.TimeZone = "Nowhere/Land").AddBusinessHours(Utc(2, 9), 1).ShouldBe(Utc(2, 10));
    }

    [Fact]
    public void Negative_hours_are_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Calendar().AddBusinessHours(Utc(2, 9), -1));
    }

    [Fact]
    public void Opening_hours_must_end_after_they_start()
    {
        Should.Throw<ArgumentException>(() => Calendar(options =>
        {
            options.Start = TimeSpan.FromHours(17);
            options.End = TimeSpan.FromHours(8);
        }));
    }

    [Fact]
    public void At_least_one_working_day_is_required()
    {
        Should.Throw<ArgumentException>(() => Calendar(options => options.WorkDays = Array.Empty<DayOfWeek>()));
    }
}

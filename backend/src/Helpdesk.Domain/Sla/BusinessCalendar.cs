using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Helpdesk.Sla;

/// <summary>
/// Adds working time to a moment: the clock only runs inside the daily opening hours, on working days,
/// skipping holidays. All inputs and outputs are UTC; the rules are applied in the configured time zone.
/// </summary>
public class BusinessCalendar : ISingletonDependency
{
    private readonly TimeSpan _start;
    private readonly TimeSpan _end;
    private readonly HashSet<DayOfWeek> _workDays;
    private readonly HashSet<DateTime> _holidays;
    private readonly TimeZoneInfo _zone;

    public BusinessCalendar(IOptions<BusinessHoursOptions> options)
    {
        var value = options.Value;
        if (value.End <= value.Start)
        {
            throw new ArgumentException("Business hours must end after they start.", nameof(options));
        }

        if (value.WorkDays.Length == 0)
        {
            throw new ArgumentException("At least one working day is required.", nameof(options));
        }

        _start = value.Start;
        _end = value.End;
        _workDays = value.WorkDays.ToHashSet();
        _holidays = value.Holidays
            .Select(day => DateTime.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToHashSet();
        _zone = FindZone(value.TimeZone);
    }

    /// <summary>Returns the UTC moment at which <paramref name="hours"/> of working time have passed.</summary>
    public DateTime AddBusinessHours(DateTime utcStart, double hours)
    {
        if (hours < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hours), "Hours cannot be negative.");
        }

        var cursor = NextOpening(ToLocal(utcStart));
        var remaining = TimeSpan.FromHours(hours);

        while (true)
        {
            var leftToday = _end - cursor.TimeOfDay;
            if (remaining <= leftToday)
            {
                cursor += remaining;
                break;
            }

            remaining -= leftToday;
            cursor = NextOpening(cursor.Date.AddDays(1));
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(cursor, DateTimeKind.Unspecified), _zone);
    }

    /// <summary>How many working hours lie between two UTC moments (0 when the range is empty or entirely off hours).</summary>
    public double BusinessHoursBetween(DateTime fromUtc, DateTime toUtc)
    {
        if (toUtc <= fromUtc)
        {
            return 0;
        }

        var from = ToLocal(fromUtc);
        var to = ToLocal(toUtc);
        var hours = 0.0;

        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            if (!IsWorkingDay(day))
            {
                continue;
            }

            var start = from > day + _start ? from : day + _start;
            var end = to < day + _end ? to : day + _end;
            if (end > start)
            {
                hours += (end - start).TotalHours;
            }
        }

        return hours;
    }

    private DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    /// <summary>The first moment at or after <paramref name="local"/> that falls inside opening hours.</summary>
    private DateTime NextOpening(DateTime local)
    {
        var day = local.Date;
        var time = local.TimeOfDay;

        while (true)
        {
            if (IsWorkingDay(day))
            {
                if (time < _start)
                {
                    return day + _start;
                }

                if (time < _end)
                {
                    return day + time;
                }
            }

            day = day.AddDays(1);
            time = TimeSpan.Zero;
        }
    }

    private bool IsWorkingDay(DateTime day) => _workDays.Contains(day.DayOfWeek) && !_holidays.Contains(day);

    private static TimeZoneInfo FindZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

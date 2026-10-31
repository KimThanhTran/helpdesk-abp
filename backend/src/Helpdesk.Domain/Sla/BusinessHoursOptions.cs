using System;

namespace Helpdesk.Sla;

/// <summary>Bound from the "Helpdesk:BusinessHours" configuration section.</summary>
public class BusinessHoursOptions
{
    public const string SectionName = "Helpdesk:BusinessHours";

    /// <summary>IANA or Windows time zone id. Falls back to UTC when it is not found.</summary>
    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";

    public TimeSpan Start { get; set; } = TimeSpan.FromHours(8);

    public TimeSpan End { get; set; } = TimeSpan.FromHours(17);

    /// <summary>Days of the week that are working days.</summary>
    public DayOfWeek[] WorkDays { get; set; } =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
    };

    /// <summary>Non-working dates in the local time zone, as yyyy-MM-dd.</summary>
    public string[] Holidays { get; set; } = Array.Empty<string>();
}

using System;
using System.Collections.Generic;
using Helpdesk.Tickets;

namespace Helpdesk.Reports;

public class GetReportInput
{
    /// <summary>Tickets created on or after this moment (UTC). Defaults to 30 days ago.</summary>
    public DateTime? From { get; set; }

    /// <summary>Tickets created before this moment (UTC). Defaults to now.</summary>
    public DateTime? To { get; set; }
}

public class CountByDto<TKey>
{
    public TKey Key { get; set; } = default!;

    public int Count { get; set; }
}

public class TicketSummaryDto
{
    public DateTime From { get; set; }

    public DateTime To { get; set; }

    public int Total { get; set; }

    public List<CountByDto<TicketStatus>> ByStatus { get; set; } = new();

    public List<CountByDto<TicketPriority>> ByPriority { get; set; } = new();

    public List<CountByDto<string>> ByCategory { get; set; } = new();

    /// <summary>Still being worked on and past the resolution target right now.</summary>
    public int OverdueNow { get; set; }

    /// <summary>Tickets that have never been answered and are past the first-response target.</summary>
    public int WaitingForFirstResponseOverdue { get; set; }

    /// <summary>Average hours from creation to resolution, over tickets resolved in the range. Calendar hours.</summary>
    public double? AverageResolutionHours { get; set; }
}

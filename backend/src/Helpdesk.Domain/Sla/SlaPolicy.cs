using System;
using Helpdesk.Tickets;

namespace Helpdesk.Sla;

/// <summary>Targets in business hours. A ticket is overdue when a target passes without the event happening.</summary>
public static class SlaPolicy
{
    public readonly record struct Targets(double FirstResponseHours, double ResolutionHours);

    public static Targets For(TicketPriority priority) => priority switch
    {
        TicketPriority.Urgent => new Targets(1, 8),
        TicketPriority.High => new Targets(4, 16),
        TicketPriority.Normal => new Targets(8, 40),
        TicketPriority.Low => new Targets(16, 80),
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority."),
    };
}

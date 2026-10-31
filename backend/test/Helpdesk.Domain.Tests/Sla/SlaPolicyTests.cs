using System;
using Helpdesk.Tickets;
using Shouldly;
using Xunit;

namespace Helpdesk.Sla;

public class SlaPolicyTests
{
    [Fact]
    public void More_urgent_tickets_get_shorter_targets()
    {
        var urgent = SlaPolicy.For(TicketPriority.Urgent);
        var high = SlaPolicy.For(TicketPriority.High);
        var normal = SlaPolicy.For(TicketPriority.Normal);
        var low = SlaPolicy.For(TicketPriority.Low);

        urgent.FirstResponseHours.ShouldBeLessThan(high.FirstResponseHours);
        high.FirstResponseHours.ShouldBeLessThan(normal.FirstResponseHours);
        normal.FirstResponseHours.ShouldBeLessThan(low.FirstResponseHours);

        urgent.ResolutionHours.ShouldBeLessThan(high.ResolutionHours);
        high.ResolutionHours.ShouldBeLessThan(normal.ResolutionHours);
        normal.ResolutionHours.ShouldBeLessThan(low.ResolutionHours);
    }

    [Theory]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.Normal)]
    [InlineData(TicketPriority.High)]
    [InlineData(TicketPriority.Urgent)]
    public void A_ticket_is_answered_before_it_has_to_be_resolved(TicketPriority priority)
    {
        var targets = SlaPolicy.For(priority);
        targets.FirstResponseHours.ShouldBeLessThan(targets.ResolutionHours);
    }

    [Fact]
    public void An_unknown_priority_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => SlaPolicy.For((TicketPriority)99));
    }
}

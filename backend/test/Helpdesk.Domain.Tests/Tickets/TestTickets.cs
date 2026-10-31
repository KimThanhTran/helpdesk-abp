using System;
using Shouldly;
using Volo.Abp;

namespace Helpdesk.Tickets;

/// <summary>Builds tickets in any status by following the real transitions, so tests never fake state.</summary>
internal static class TestTickets
{
    public static readonly Guid Requester = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Agent = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid OtherAgent = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Stranger = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>A Monday morning. Every later moment in a test is an offset from this.</summary>
    public static readonly DateTime T0 = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    public static Ticket New(TicketPriority priority = TicketPriority.Normal) =>
        new(Guid.NewGuid(), "HD-2026-000001", Guid.NewGuid(), Requester, "Printer jams", "Page 2 always jams",
            priority, T0.AddHours(8), T0.AddHours(40), T0);

    public static Ticket InStatus(TicketStatus status)
    {
        var ticket = New();
        switch (status)
        {
            case TicketStatus.New:
                break;
            case TicketStatus.Open:
                ticket.Assign(Agent, Agent, T0.AddHours(1));
                break;
            case TicketStatus.Waiting:
                ticket.Assign(Agent, Agent, T0.AddHours(1));
                ticket.Wait("Need the model number", Agent, T0.AddHours(2));
                break;
            case TicketStatus.Resolved:
                ticket.Assign(Agent, Agent, T0.AddHours(1));
                ticket.Resolve("Replaced the roller", Agent, T0.AddHours(3));
                break;
            case TicketStatus.Closed:
                ticket.Assign(Agent, Agent, T0.AddHours(1));
                ticket.Resolve("Replaced the roller", Agent, T0.AddHours(3));
                ticket.Confirm(Requester, T0.AddHours(4));
                break;
            case TicketStatus.Cancelled:
                ticket.Cancel("No longer needed", Requester, T0.AddHours(1));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        ticket.Status.ShouldBe(status);
        return ticket;
    }

    public static BusinessException ShouldBeRejected(this Action action, string code)
    {
        var exception = Should.Throw<BusinessException>(action);
        exception.Code.ShouldBe(code);
        return exception;
    }
}

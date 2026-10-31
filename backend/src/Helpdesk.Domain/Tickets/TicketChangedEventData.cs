using System;

namespace Helpdesk.Tickets;

/// <summary>
/// Published on the local event bus after a ticket changed and the change is saved.
/// Handlers (for example the Google Sheets export) read the ticket again, so they always see the final state.
/// </summary>
public class TicketChangedEventData
{
    public Guid TicketId { get; set; }

    public TicketEventKind Kind { get; set; }

    public TicketChangedEventData()
    {
    }

    public TicketChangedEventData(Guid ticketId, TicketEventKind kind)
    {
        TicketId = ticketId;
        Kind = kind;
    }
}

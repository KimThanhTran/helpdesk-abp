namespace Helpdesk.Tickets;

public enum TicketEventKind
{
    Created = 1,
    Assigned = 2,
    PriorityChanged = 3,
    StatusChanged = 4,
    Reopened = 5,
    AutoClosed = 6,
}

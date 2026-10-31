namespace Helpdesk.Tickets;

/// <summary>Things a user can do to a ticket. The API tells each caller which ones are available.</summary>
public enum TicketAction
{
    Comment = 1,
    InternalNote = 2,
    Claim = 3,
    Assign = 4,
    ChangePriority = 5,
    Wait = 6,
    Resume = 7,
    Resolve = 8,
    Confirm = 9,
    Reject = 10,
    Reopen = 11,
    Cancel = 12,
}

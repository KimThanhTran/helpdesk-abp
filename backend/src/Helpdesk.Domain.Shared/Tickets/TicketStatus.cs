namespace Helpdesk.Tickets;

/// <summary>Stored and sent as a number: never renumber an existing value.</summary>
public enum TicketStatus
{
    /// <summary>Waiting for an agent to pick it up.</summary>
    New = 1,

    /// <summary>An agent is working on it.</summary>
    Open = 2,

    /// <summary>Paused until the requester answers.</summary>
    Waiting = 3,

    /// <summary>Agent finished; the requester can confirm or reject.</summary>
    Resolved = 4,

    /// <summary>Done. Can be reopened for a limited time.</summary>
    Closed = 5,

    /// <summary>Withdrawn before it was resolved.</summary>
    Cancelled = 6,
}

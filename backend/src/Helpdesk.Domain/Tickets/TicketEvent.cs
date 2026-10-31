using System;
using Volo.Abp.Domain.Entities;

namespace Helpdesk.Tickets;

/// <summary>One line of a ticket's history: who did what, when. Append-only.</summary>
public class TicketEvent : Entity<Guid>
{
    public Guid TicketId { get; private set; }

    public TicketEventKind Kind { get; private set; }

    /// <summary>Null when the system acted (for example the automatic close).</summary>
    public Guid? ActorId { get; private set; }

    public DateTime CreationTime { get; private set; }

    public TicketStatus? FromStatus { get; private set; }

    public TicketStatus? ToStatus { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public string? Note { get; private set; }

    protected TicketEvent()
    {
    }

    internal TicketEvent(
        Guid id,
        Guid ticketId,
        TicketEventKind kind,
        Guid? actorId,
        DateTime creationTime,
        TicketStatus? fromStatus,
        TicketStatus? toStatus,
        Guid? assigneeId,
        string? note)
        : base(id)
    {
        TicketId = ticketId;
        Kind = kind;
        ActorId = actorId;
        CreationTime = creationTime;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        AssigneeId = assigneeId;
        Note = note;
    }
}

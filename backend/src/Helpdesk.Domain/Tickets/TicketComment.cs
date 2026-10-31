using System;
using Volo.Abp.Domain.Entities;

namespace Helpdesk.Tickets;

/// <summary>A message on a ticket. Never edited and never deleted: it is the record of what was said.</summary>
public class TicketComment : Entity<Guid>
{
    public Guid TicketId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Body { get; private set; } = null!;

    public CommentVisibility Visibility { get; private set; }

    public DateTime CreationTime { get; private set; }

    protected TicketComment()
    {
    }

    internal TicketComment(
        Guid id, Guid ticketId, Guid authorId, string body, CommentVisibility visibility, DateTime creationTime)
        : base(id)
    {
        TicketId = ticketId;
        AuthorId = authorId;
        Body = body;
        Visibility = visibility;
        CreationTime = creationTime;
    }
}

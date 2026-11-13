using System;

namespace Helpdesk.Tickets;

/// <summary>
/// Published after a comment is saved. It is a separate event from <see cref="TicketChangedEventData"/> because most
/// comments change nothing a report or a notification cares about, but a requester still wants to read an agent's reply.
/// </summary>
public class TicketCommentedEventData
{
    public Guid TicketId { get; set; }

    public Guid CommentId { get; set; }

    public Guid AuthorId { get; set; }

    public CommentVisibility Visibility { get; set; }

    public TicketCommentedEventData()
    {
    }

    public TicketCommentedEventData(Guid ticketId, Guid commentId, Guid authorId, CommentVisibility visibility)
    {
        TicketId = ticketId;
        CommentId = commentId;
        AuthorId = authorId;
        Visibility = visibility;
    }
}

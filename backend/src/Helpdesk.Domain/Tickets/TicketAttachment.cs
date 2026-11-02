using System;
using Volo.Abp.Domain.Entities;

namespace Helpdesk.Tickets;

/// <summary>
/// An image attached to a ticket. It is its own aggregate, not part of <see cref="Ticket"/>, so loading a ticket
/// never loads the bytes. Never edited and never deleted, like comments.
/// </summary>
public class TicketAttachment : AggregateRoot<Guid>
{
    public Guid TicketId { get; private set; }

    public Guid UploaderId { get; private set; }

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public int Size { get; private set; }

    /// <summary>Internal attachments, like internal notes, are for agents only.</summary>
    public CommentVisibility Visibility { get; private set; }

    public DateTime CreationTime { get; private set; }

    public byte[] Content { get; private set; } = null!;

    protected TicketAttachment()
    {
    }

    public TicketAttachment(
        Guid id, Guid ticketId, Guid uploaderId, string fileName, AttachmentRules.ImageKind kind,
        byte[] content, CommentVisibility visibility, DateTime creationTime)
        : base(id)
    {
        TicketId = ticketId;
        UploaderId = uploaderId;
        FileName = AttachmentRules.SafeFileName(fileName, kind);
        ContentType = kind.ContentType;
        Size = content.Length;
        Content = content;
        Visibility = visibility;
        CreationTime = creationTime;
    }
}

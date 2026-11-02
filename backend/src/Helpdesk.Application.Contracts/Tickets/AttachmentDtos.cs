using System;
using Volo.Abp.Application.Dtos;

namespace Helpdesk.Tickets;

/// <summary>An attachment without its bytes: what a list shows. The image itself comes from the content route.</summary>
public class AttachmentDto : EntityDto<Guid>
{
    public Guid TicketId { get; set; }

    public string FileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public int Size { get; set; }

    public CommentVisibility Visibility { get; set; }

    public PersonDto Uploader { get; set; } = null!;

    public DateTime CreationTime { get; set; }
}

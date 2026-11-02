using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Helpdesk.Tickets;

/// <summary>
/// Images on a ticket (PNG, JPEG or WebP, up to 5 MB each, five per ticket). Whoever may see the ticket may list and
/// open its public attachments; whoever may comment may add one; internal ones are for agents.
/// Errors: 404 when the ticket is not yours to see, 415 for anything that is not an image, 413 when too big,
/// 409 over the limit.
/// </summary>
public interface ITicketAttachmentAppService : IApplicationService
{
    Task<ListResultDto<AttachmentDto>> GetListAsync(Guid ticketId);

    /// <summary>
    /// POST /api/app/ticket-attachment/upload/{ticketId}?visibility=1 as multipart/form-data with the image in a part named <c>file</c>.
    /// </summary>
    Task<AttachmentDto> UploadAsync(Guid ticketId, CommentVisibility visibility, IRemoteStreamContent file);

    /// <summary>The image itself.</summary>
    Task<IRemoteStreamContent> GetContentAsync(Guid id);
}

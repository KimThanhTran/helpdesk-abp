using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace Helpdesk.Tickets;

[Authorize(HelpdeskPermissions.Tickets.Default)]
public class TicketAttachmentAppService : HelpdeskAppService, ITicketAttachmentAppService
{
    private readonly IRepository<Ticket, Guid> _tickets;
    private readonly IRepository<TicketAttachment, Guid> _attachments;
    private readonly IIdentityUserRepository _users;
    private readonly TicketManager _manager;

    public TicketAttachmentAppService(
        IRepository<Ticket, Guid> tickets,
        IRepository<TicketAttachment, Guid> attachments,
        IIdentityUserRepository users,
        TicketManager manager)
    {
        _tickets = tickets;
        _attachments = attachments;
        _users = users;
        _manager = manager;
    }

    public async Task<ListResultDto<AttachmentDto>> GetListAsync(Guid ticketId)
    {
        var actor = await GetActorAsync();
        await LoadTicketAsync(ticketId, actor);

        // a projection: the bytes stay in the database
        var rows = await AsyncExecuter.ToListAsync(
            (await _attachments.GetQueryableAsync())
                .Where(a => a.TicketId == ticketId && (actor.IsAgent || a.Visibility == CommentVisibility.Public))
                .OrderBy(a => a.CreationTime)
                .Select(a => new { a.Id, a.TicketId, a.FileName, a.ContentType, a.Size, a.Visibility, a.UploaderId, a.CreationTime }));

        var users = await _users.GetListByIdsAsync(rows.Select(r => r.UploaderId).Distinct().ToList());
        var names = users.ToDictionary(user => user.Id, user => user.UserName);

        return new ListResultDto<AttachmentDto>(rows.Select(r => new AttachmentDto
        {
            Id = r.Id,
            TicketId = r.TicketId,
            FileName = r.FileName,
            ContentType = r.ContentType,
            Size = r.Size,
            Visibility = r.Visibility,
            Uploader = new PersonDto { Id = r.UploaderId, Name = names.GetValueOrDefault(r.UploaderId, "Unknown user") },
            CreationTime = r.CreationTime,
        }).ToList());
    }

    public async Task<AttachmentDto> UploadAsync(Guid ticketId, CommentVisibility visibility, IRemoteStreamContent file)
    {
        var actor = await GetActorAsync();
        var ticket = await LoadTicketAsync(ticketId, actor);

        // adding a file is a kind of comment, so it follows the same table: who, and in which status
        var action = visibility == CommentVisibility.Internal ? TicketAction.InternalNote : TicketAction.Comment;
        TicketPolicy.Ensure(action, ticket, actor, Clock.Now, _manager.ReopenWindowDays);

        if (await _attachments.CountAsync(a => a.TicketId == ticket.Id) >= TicketConsts.MaxAttachmentsPerTicket)
        {
            throw HelpdeskErrors.AttachmentLimit(TicketConsts.MaxAttachmentsPerTicket);
        }

        var content = await ReadAsync(file);
        var kind = AttachmentRules.Sniff(content) ?? throw HelpdeskErrors.AttachmentBadType();

        var attachment = new TicketAttachment(
            GuidGenerator.Create(), ticket.Id, actor.UserId, file.FileName ?? string.Empty, kind, content,
            visibility, Clock.Now);
        await _attachments.InsertAsync(attachment, autoSave: true);

        return new AttachmentDto
        {
            Id = attachment.Id,
            TicketId = attachment.TicketId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            Size = attachment.Size,
            Visibility = attachment.Visibility,
            Uploader = new PersonDto { Id = actor.UserId, Name = CurrentUser.UserName ?? "Unknown user" },
            CreationTime = attachment.CreationTime,
        };
    }

    public async Task<IRemoteStreamContent> GetContentAsync(Guid id)
    {
        var actor = await GetActorAsync();
        var attachment = await _attachments.FindAsync(a => a.Id == id);
        if (attachment is null)
        {
            throw new EntityNotFoundException(typeof(TicketAttachment), id);
        }

        await LoadTicketAsync(attachment.TicketId, actor);
        if (attachment.Visibility == CommentVisibility.Internal && !actor.IsAgent)
        {
            // same answer as a missing one, so a requester cannot learn that an internal file exists
            throw new EntityNotFoundException(typeof(TicketAttachment), id);
        }

        return new RemoteStreamContent(new MemoryStream(attachment.Content), attachment.FileName, attachment.ContentType);
    }

    /// <summary>Stops reading one byte past the limit, so a huge upload is refused without being held in memory.</summary>
    private static async Task<byte[]> ReadAsync(IRemoteStreamContent file)
    {
        await using var stream = file.GetStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            await buffer.WriteAsync(chunk.AsMemory(0, read));
            if (buffer.Length > TicketConsts.MaxAttachmentBytes)
            {
                throw HelpdeskErrors.AttachmentTooLarge(TicketConsts.MaxAttachmentBytes);
            }
        }

        if (buffer.Length == 0)
        {
            throw HelpdeskErrors.AttachmentEmpty();
        }

        return buffer.ToArray();
    }

    private async Task<TicketActor> GetActorAsync() =>
        new(CurrentUser.GetId(), await AuthorizationService.IsGrantedAsync(HelpdeskPermissions.Tickets.Work));

    /// <summary>Someone else's ticket is reported as not found, like everywhere else, so ids cannot be probed.</summary>
    private async Task<Ticket> LoadTicketAsync(Guid id, TicketActor actor)
    {
        var ticket = await _tickets.FindAsync(candidate => candidate.Id == id);
        if (ticket is null || (ticket.RequesterId != actor.UserId && !actor.IsAgent))
        {
            throw new EntityNotFoundException(typeof(Ticket), id);
        }

        return ticket;
    }
}

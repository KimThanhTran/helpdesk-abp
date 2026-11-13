using System;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Localization;
using Helpdesk.Tickets;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.Uow;

namespace Helpdesk.Email;

/// <summary>
/// Writes to the requester when something happens to their ticket that they did not do themselves: it was received,
/// an agent waits for an answer, it was resolved, closed or cancelled, or an agent wrote a public reply.
/// Internal notes and the agent's own actions never produce a mail. A failure is logged and never reaches the caller.
/// </summary>
public class TicketMailer : ITransientDependency
{
    private readonly HelpdeskOptions _options;
    private readonly IMailSender _sender;
    private readonly IRepository<Ticket, Guid> _tickets;
    private readonly IIdentityUserRepository _users;
    private readonly IStringLocalizer<HelpdeskResource> _l;
    private readonly IUnitOfWorkManager _unitOfWork;
    private readonly ILogger<TicketMailer> _logger;

    public TicketMailer(
        IOptions<HelpdeskOptions> options,
        IMailSender sender,
        IRepository<Ticket, Guid> tickets,
        IIdentityUserRepository users,
        IStringLocalizer<HelpdeskResource> localizer,
        IUnitOfWorkManager unitOfWork,
        ILogger<TicketMailer> logger)
    {
        _options = options.Value;
        _sender = sender;
        _tickets = tickets;
        _users = users;
        _l = localizer;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task OnChangedAsync(TicketChangedEventData change)
    {
        if (!_options.Email.Enabled)
        {
            return;
        }

        try
        {
            var (ticket, requester, _) = await LoadAsync(change.TicketId, commentId: null);
            if (ticket is null || requester is null)
            {
                return;
            }

            // what the requester did themselves is not news to them, except that a new ticket is confirmed
            var byRequester = change.ActorId == ticket.RequesterId;
            var kind = change.Kind switch
            {
                TicketEventKind.Created => "Created",
                TicketEventKind.AutoClosed => "AutoClosed",
                TicketEventKind.StatusChanged when !byRequester => ticket.Status switch
                {
                    TicketStatus.Waiting => "Waiting",
                    TicketStatus.Resolved => "Resolved",
                    TicketStatus.Cancelled => "Cancelled",
                    _ => null,
                },
                _ => null,
            };

            switch (kind)
            {
                case "Created" or "AutoClosed" or "Cancelled":
                    await SendAsync(requester, ticket, kind, ticket.Code, ticket.Title);
                    break;
                case "Waiting":
                    await SendAsync(requester, ticket, kind, ticket.Code, ticket.Title, ticket.WaitingNote ?? string.Empty);
                    break;
                case "Resolved":
                    await SendAsync(requester, ticket, kind, ticket.Code, ticket.Title, ticket.Resolution ?? string.Empty);
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The mail about ticket {TicketId} could not be sent", change.TicketId);
        }
    }

    public async Task OnCommentedAsync(TicketCommentedEventData commented)
    {
        if (!_options.Email.Enabled || commented.Visibility != CommentVisibility.Public)
        {
            return;
        }

        try
        {
            var (ticket, requester, comment) = await LoadAsync(commented.TicketId, commented.CommentId);
            if (ticket is null || requester is null || comment is null || comment.AuthorId == ticket.RequesterId)
            {
                return;
            }

            await SendAsync(requester, ticket, "Comment", comment.AuthorName, ticket.Code, ticket.Title, comment.Body);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The mail about a comment on ticket {TicketId} could not be sent", commented.TicketId);
        }
    }

    private sealed record Loaded(Ticket? Ticket, IdentityUser? Requester, LoadedComment? Comment)
    {
        public void Deconstruct(out Ticket? ticket, out IdentityUser? requester, out LoadedComment? comment)
        {
            ticket = Ticket;
            requester = Requester;
            comment = Comment;
        }
    }

    private sealed record LoadedComment(Guid AuthorId, string AuthorName, string Body);

    /// <summary>Reads the committed ticket, its requester (when they have an address) and optionally one comment, in a unit of work of its own.</summary>
    private async Task<Loaded> LoadAsync(Guid ticketId, Guid? commentId)
    {
        using var uow = _unitOfWork.Begin(requiresNew: true);
        var ticket = await _tickets.FindAsync(candidate => candidate.Id == ticketId, includeDetails: true);
        if (ticket is null)
        {
            return new Loaded(null, null, null);
        }

        var requester = await _users.FindAsync(ticket.RequesterId);
        if (requester is null || string.IsNullOrWhiteSpace(requester.Email))
        {
            return new Loaded(ticket, null, null);
        }

        LoadedComment? loaded = null;
        if (commentId is { } id && ticket.Comments.FirstOrDefault(c => c.Id == id) is { } comment)
        {
            var author = await _users.FindAsync(comment.AuthorId);
            loaded = new LoadedComment(comment.AuthorId, author is null ? "?" : DisplayName(author), comment.Body);
        }

        await uow.CompleteAsync();
        return new Loaded(ticket, requester, loaded);
    }

    private async Task SendAsync(IdentityUser requester, Ticket ticket, string kind, params string[] arguments)
    {
        string subject;
        string text;
        using (CultureHelper.Use(_options.Email.Language))
        {
            subject = _l[$"Email:{kind}:Subject", ticket.Code];
            text = string.Join("\n\n", new[]
            {
                _l["Email:Hello", DisplayName(requester)].Value,
                _l[$"Email:{kind}:Body", arguments].Value.TrimEnd(),
                string.IsNullOrWhiteSpace(_options.Email.PublicUrl)
                    ? string.Empty
                    : _l["Email:Link", $"{_options.Email.PublicUrl.TrimEnd('/')}/#/tickets/{ticket.Id}"].Value,
                _options.Email.InboundEnabled ? _l["Email:Reply"].Value : string.Empty,
            }.Where(part => part.Length > 0));
        }

        await _sender.SendAsync(requester.Email, subject, text);
    }

    private static string DisplayName(IdentityUser user)
    {
        var full = $"{user.Name} {user.Surname}".Trim();
        return full.Length > 0 ? full : user.UserName;
    }
}

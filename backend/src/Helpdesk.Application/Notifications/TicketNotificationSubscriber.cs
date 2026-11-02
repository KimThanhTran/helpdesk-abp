using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Helpdesk.Tickets;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace Helpdesk.Notifications;

/// <summary>
/// Tells the people involved in a ticket (its requester and assignee) and the agents that it changed.
/// The message is built from the committed ticket and says only what happened, never what was written.
/// </summary>
[ExposeServices(typeof(ITicketChangeSubscriber), IncludeSelf = true)]
public class TicketNotificationSubscriber : ITicketChangeSubscriber, ITransientDependency
{
    private readonly ITicketNotifier _notifier;
    private readonly IRepository<Ticket, Guid> _tickets;
    private readonly IUnitOfWorkManager _unitOfWork;
    private readonly ILogger<TicketNotificationSubscriber> _logger;

    public TicketNotificationSubscriber(
        ITicketNotifier notifier,
        IRepository<Ticket, Guid> tickets,
        IUnitOfWorkManager unitOfWork,
        ILogger<TicketNotificationSubscriber> logger)
    {
        _notifier = notifier;
        _tickets = tickets;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task OnTicketChangedAsync(TicketChangedEventData change)
    {
        try
        {
            TicketNotification notification;
            var people = new List<Guid>();

            using (var uow = _unitOfWork.Begin(requiresNew: true))
            {
                var ticket = await _tickets.FindAsync(candidate => candidate.Id == change.TicketId, includeDetails: false);
                if (ticket is null)
                {
                    return;
                }

                people.Add(ticket.RequesterId);
                if (ticket.AssigneeId is { } assignee)
                {
                    people.Add(assignee);
                }

                notification = new TicketNotification
                {
                    TicketId = ticket.Id,
                    Code = ticket.Code,
                    Title = ticket.Title,
                    Status = ticket.Status.ToString(),
                    Kind = change.Kind.ToString(),
                    Message = Describe(change.Kind, ticket),
                    ActorId = change.ActorId,
                    At = ticket.LastModificationTime ?? ticket.CreationTime,
                };

                await uow.CompleteAsync();
            }

            await _notifier.SendAsync(notification, people.ToArray(), toAgents: true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Notification for ticket {TicketId} failed", change.TicketId);
        }
    }

    private static string Describe(TicketEventKind kind, Ticket ticket) => kind switch
    {
        TicketEventKind.Created => $"New ticket {ticket.Code}",
        TicketEventKind.Assigned => $"{ticket.Code} was assigned and is now {ticket.Status}",
        TicketEventKind.PriorityChanged => $"{ticket.Code} is now {ticket.Priority} priority",
        TicketEventKind.Reopened => $"{ticket.Code} was reopened",
        TicketEventKind.AutoClosed => $"{ticket.Code} was closed automatically",
        _ => $"{ticket.Code} is now {ticket.Status}",
    };
}

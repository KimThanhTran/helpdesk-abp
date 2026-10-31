using System;
using System.Collections.Generic;
using System.Linq;

namespace Helpdesk.Tickets;

/// <summary>Who is acting: their id and whether they work tickets (agent) or only raise them.</summary>
public readonly record struct TicketActor(Guid UserId, bool IsAgent);

/// <summary>
/// The one table that answers "may this person do this to this ticket right now?".
/// The API uses it twice: to refuse a call (403 for the wrong role, 409 for the wrong status)
/// and to tell the client which buttons to show. A test checks it against the real aggregate.
/// </summary>
public static class TicketPolicy
{
    public static bool IsRoleAllowed(TicketAction action, Ticket ticket, TicketActor actor)
    {
        var isRequester = ticket.RequesterId == actor.UserId;
        return action switch
        {
            TicketAction.Comment => isRequester || actor.IsAgent,
            TicketAction.Cancel => isRequester || actor.IsAgent,
            TicketAction.Confirm or TicketAction.Reject or TicketAction.Reopen => isRequester,
            TicketAction.InternalNote or TicketAction.Claim or TicketAction.Assign or TicketAction.ChangePriority
                or TicketAction.Wait or TicketAction.Resume or TicketAction.Resolve => actor.IsAgent,
            _ => false,
        };
    }

    public static bool IsStatusAllowed(TicketAction action, Ticket ticket, TicketActor actor)
    {
        var status = ticket.Status;
        return action switch
        {
            TicketAction.Comment or TicketAction.InternalNote =>
                status is TicketStatus.New or TicketStatus.Open or TicketStatus.Waiting or TicketStatus.Resolved,
            TicketAction.Claim => status == TicketStatus.New,
            TicketAction.Assign or TicketAction.ChangePriority =>
                status is TicketStatus.New or TicketStatus.Open or TicketStatus.Waiting,
            TicketAction.Wait or TicketAction.Resolve => status == TicketStatus.Open,
            TicketAction.Resume => status == TicketStatus.Waiting,
            TicketAction.Confirm or TicketAction.Reject => status == TicketStatus.Resolved,
            TicketAction.Reopen => status == TicketStatus.Closed,
            // a requester can only withdraw a ticket nobody has picked up; an agent can cancel until it is resolved
            TicketAction.Cancel => status is TicketStatus.New or TicketStatus.Open or TicketStatus.Waiting
                && (actor.IsAgent || status == TicketStatus.New),
            _ => false,
        };
    }

    /// <summary>Reopening also depends on the clock: it is only offered inside the reopen window.</summary>
    public static bool IsReopenWindowOpen(Ticket ticket, DateTime now, int windowDays) =>
        ticket.ClosedAt is not null && now <= ticket.ClosedAt.Value.AddDays(windowDays);

    /// <summary>Everything this actor can do to this ticket right now.</summary>
    public static IReadOnlyCollection<TicketAction> AllowedActions(
        Ticket ticket, TicketActor actor, DateTime now, int reopenWindowDays) =>
        Enum.GetValues<TicketAction>()
            .Where(action => IsRoleAllowed(action, ticket, actor)
                && IsStatusAllowed(action, ticket, actor)
                && (action != TicketAction.Reopen || IsReopenWindowOpen(ticket, now, reopenWindowDays)))
            .ToList();

    /// <summary>Throws the right business error when the action is not allowed, so callers need no if-chains.</summary>
    public static void Ensure(
        TicketAction action, Ticket ticket, TicketActor actor, DateTime now, int reopenWindowDays)
    {
        if (!IsRoleAllowed(action, ticket, actor))
        {
            throw HelpdeskErrors.NotAllowed(action);
        }

        if (!IsStatusAllowed(action, ticket, actor))
        {
            throw HelpdeskErrors.InvalidTransition(ticket.Status, action);
        }

        if (action == TicketAction.Reopen && !IsReopenWindowOpen(ticket, now, reopenWindowDays))
        {
            throw HelpdeskErrors.ReopenWindowExpired(reopenWindowDays);
        }
    }
}

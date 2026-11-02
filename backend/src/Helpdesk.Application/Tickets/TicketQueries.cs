using System;
using System.Linq;

namespace Helpdesk.Tickets;

/// <summary>The filters of the agent queue, written once for the list and for the Excel export, so they cannot disagree.</summary>
internal static class TicketQueries
{
    public static IQueryable<Ticket> Filter(
        IQueryable<Ticket> query, string? filter, TicketStatus? status, TicketPriority? priority, Guid? categoryId)
    {
        var text = filter?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(text))
        {
            query = query.Where(ticket => ticket.Code.ToLower().Contains(text) || ticket.Title.ToLower().Contains(text));
        }

        if (status is { } wantedStatus)
        {
            query = query.Where(ticket => ticket.Status == wantedStatus);
        }

        if (priority is { } wantedPriority)
        {
            query = query.Where(ticket => ticket.Priority == wantedPriority);
        }

        if (categoryId is { } wantedCategory)
        {
            query = query.Where(ticket => ticket.CategoryId == wantedCategory);
        }

        return query;
    }

    public static IQueryable<Ticket> InScope(IQueryable<Ticket> query, QueueScope scope, Guid userId) => scope switch
    {
        QueueScope.Unassigned => query.Where(ticket => ticket.Status == TicketStatus.New),
        QueueScope.Mine => query.Where(ticket => ticket.AssigneeId == userId
            && ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Cancelled),
        _ => query,
    };

    /// <summary>Urgent and oldest first.</summary>
    public static IQueryable<Ticket> QueueOrder(IQueryable<Ticket> query) =>
        query.OrderByDescending(t => t.Priority).ThenBy(t => t.CreationTime);
}

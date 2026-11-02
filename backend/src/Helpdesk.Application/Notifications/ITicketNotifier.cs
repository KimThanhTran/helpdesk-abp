using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Helpdesk.Notifications;

/// <summary>A short message about a ticket change. It never carries the description or any comment.</summary>
public class TicketNotification
{
    public Guid EventId { get; set; } = Guid.NewGuid();

    public Guid TicketId { get; set; }

    public string Code { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Status { get; set; } = null!;

    /// <summary>The ticket's priority, so a client can word a priority change in its own language.</summary>
    public string Priority { get; set; } = null!;

    /// <summary>Created, Assigned, StatusChanged...</summary>
    public string Kind { get; set; } = null!;

    public string Message { get; set; } = null!;

    /// <summary>Who caused the change, so a client can skip the notice for its own action. Null for the system.</summary>
    public Guid? ActorId { get; set; }

    public DateTime At { get; set; }
}

public static class TicketNotificationGroups
{
    public const string Agents = "agents";

    public const string ClientMethod = "ticketChanged";

    public static string User(Guid userId) => $"user:{userId}";
}

/// <summary>Delivers a notification to the given people and, optionally, to every agent.</summary>
public interface ITicketNotifier
{
    Task SendAsync(TicketNotification notification, IReadOnlyCollection<Guid> userIds, bool toAgents);
}

/// <summary>The default: does nothing. The API host replaces it with a SignalR implementation.</summary>
public class NullTicketNotifier : ITicketNotifier, ITransientDependency
{
    public Task SendAsync(TicketNotification notification, IReadOnlyCollection<Guid> userIds, bool toAgents) =>
        Task.CompletedTask;
}

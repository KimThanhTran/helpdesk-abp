using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.DependencyInjection;

namespace Helpdesk.Notifications;

[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(ITicketNotifier))]
public class SignalRTicketNotifier : ITicketNotifier, ITransientDependency
{
    private readonly IHubContext<TicketHub> _hub;

    public SignalRTicketNotifier(IHubContext<TicketHub> hub)
    {
        _hub = hub;
    }

    public async Task SendAsync(TicketNotification notification, IReadOnlyCollection<Guid> userIds, bool toAgents)
    {
        var groups = userIds.Distinct().Select(TicketNotificationGroups.User).ToList();
        if (toAgents)
        {
            groups.Add(TicketNotificationGroups.Agents);
        }

        // an agent is only in the agents' group and anyone else only in their own, so each person gets one message
        await _hub.Clients.Groups(groups).SendAsync(TicketNotificationGroups.ClientMethod, notification);
    }
}

using System.Threading.Tasks;
using Helpdesk.Notifications;
using Helpdesk.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.AspNetCore.SignalR;
using Volo.Abp.Users;

namespace Helpdesk.Notifications;

/// <summary>
/// Pushes ticket changes to signed-in clients. It only receives: the server never accepts commands over this connection.
/// An agent joins the agents' group and gets every change. Anyone else joins a group of their own and gets
/// the changes to the tickets they raised or were assigned. A person is never in both, so nobody is told twice.
/// </summary>
[Authorize]
[HubRoute("/signalr-hubs/tickets")]
public class TicketHub : AbpHub
{
    public override async Task OnConnectedAsync()
    {
        var group = await AuthorizationService.IsGrantedAsync(HelpdeskPermissions.Tickets.Work)
            ? TicketNotificationGroups.Agents
            : TicketNotificationGroups.User(CurrentUser.GetId());

        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await base.OnConnectedAsync();
    }
}

using System.Threading.Tasks;

namespace Helpdesk.Tickets;

/// <summary>
/// Something that reacts to a saved ticket change (export to a sheet, push a notification...).
/// Subscribers run after the change is committed, each in a fresh scope, and must not throw:
/// a failed side effect never turns a saved change into an error for the caller.
/// </summary>
public interface ITicketChangeSubscriber
{
    Task OnTicketChangedAsync(TicketChangedEventData change);
}

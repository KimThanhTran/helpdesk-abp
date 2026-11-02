using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;
using Volo.Abp.Uow;

namespace Helpdesk.Tickets;

/// <summary>
/// Hands a saved ticket change to every <see cref="ITicketChangeSubscriber"/> (sheet export, notifications...).
/// </summary>
public class TicketChangedHandler : ILocalEventHandler<TicketChangedEventData>, ITransientDependency
{
    private readonly IUnitOfWorkManager _unitOfWork;
    private readonly IServiceScopeFactory _scopeFactory;

    public TicketChangedHandler(IUnitOfWorkManager unitOfWork, IServiceScopeFactory scopeFactory)
    {
        _unitOfWork = unitOfWork;
        _scopeFactory = scopeFactory;
    }

    public async Task HandleEventAsync(TicketChangedEventData eventData)
    {
        // Two traps, both found by running the real stack:
        //  1. ABP runs local event handlers inside the caller's transaction, BEFORE it commits. Reading the ticket
        //     now would show the state from before the change (and nothing at all for a brand-new ticket).
        //  2. ABP creates a short-lived scope just to run this handler and disposes it as soon as the handler returns.
        //     A callback that runs later must therefore not use anything injected into this class except the
        //     scope factory, which outlives it.
        // So the work is deferred until the unit of work has committed, and runs in a scope of its own.
        var current = _unitOfWork.Current;
        if (current is null)
        {
            await RunAsync(eventData);
            return;
        }

        current.OnCompleted(() => RunAsync(eventData));
    }

    private async Task RunAsync(TicketChangedEventData eventData)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        foreach (var subscriber in scope.ServiceProvider.GetServices<ITicketChangeSubscriber>())
        {
            await subscriber.OnTicketChangedAsync(eventData);
        }
    }
}

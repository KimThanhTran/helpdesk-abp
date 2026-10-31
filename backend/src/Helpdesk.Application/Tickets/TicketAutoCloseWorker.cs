using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.EventBus.Local;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace Helpdesk.Tickets;

/// <summary>
/// Every hour, closes resolved tickets whose requester never answered within the configured days.
/// Runs on every API instance. If you scale out, make sure only one instance registers it
/// (or guard it with a distributed lock): two instances would race to close the same tickets.
/// </summary>
public class TicketAutoCloseWorker : AsyncPeriodicBackgroundWorkerBase
{
    public TicketAutoCloseWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = 60 * 60 * 1000;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var unitOfWorkManager = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var manager = workerContext.ServiceProvider.GetRequiredService<TicketManager>();
        var eventBus = workerContext.ServiceProvider.GetRequiredService<ILocalEventBus>();

        using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var closed = await manager.AutoCloseDueAsync();
        foreach (var ticket in closed)
        {
            await eventBus.PublishAsync(new TicketChangedEventData(ticket.Id, TicketEventKind.AutoClosed));
        }

        await uow.CompleteAsync();

        if (closed.Count > 0)
        {
            Logger.LogInformation("Closed {Count} resolved ticket(s) that were never answered", closed.Count);
        }
    }
}

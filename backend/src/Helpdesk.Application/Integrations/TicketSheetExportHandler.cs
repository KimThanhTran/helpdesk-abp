using System;
using System.Threading.Tasks;
using Helpdesk.Tickets;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;
using Volo.Abp.Uow;

namespace Helpdesk.Integrations;

/// <summary>
/// Reacts to a ticket change by scheduling an export to Google Sheets (when configured).
/// The work itself is in <see cref="TicketSheetExportRunner"/>.
/// </summary>
public class TicketSheetExportHandler : ILocalEventHandler<TicketChangedEventData>, ITransientDependency
{
    private readonly SheetsExporter _exporter;
    private readonly IUnitOfWorkManager _unitOfWork;
    private readonly IServiceScopeFactory _scopeFactory;

    public TicketSheetExportHandler(
        SheetsExporter exporter, IUnitOfWorkManager unitOfWork, IServiceScopeFactory scopeFactory)
    {
        _exporter = exporter;
        _unitOfWork = unitOfWork;
        _scopeFactory = scopeFactory;
    }

    public async Task HandleEventAsync(TicketChangedEventData eventData)
    {
        if (!_exporter.Enabled)
        {
            return;
        }

        // Two traps, both found by running the real stack:
        //  1. ABP runs local event handlers inside the caller's transaction, BEFORE it commits. Reading the ticket
        //     now would show the state from before the change (and nothing at all for a brand-new ticket).
        //  2. ABP creates a short-lived scope just to run this handler and disposes it as soon as the handler returns.
        //     A callback that runs later must therefore not use anything injected into this class except the
        //     scope factory, which outlives it.
        // So the export is deferred until the unit of work has committed, and runs in a scope of its own.
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
        await scope.ServiceProvider.GetRequiredService<TicketSheetExportRunner>().RunAsync(eventData);
    }
}

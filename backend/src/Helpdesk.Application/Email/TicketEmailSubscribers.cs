using System.Threading.Tasks;
using Helpdesk.Tickets;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;
using Volo.Abp.Uow;

namespace Helpdesk.Email;

/// <summary>Mail about a saved change of status (the shared handler runs it after the commit, in a scope of its own).</summary>
[ExposeServices(typeof(ITicketChangeSubscriber), IncludeSelf = true)]
public class TicketEmailSubscriber : ITicketChangeSubscriber, ITransientDependency
{
    private readonly TicketMailer _mailer;

    public TicketEmailSubscriber(TicketMailer mailer)
    {
        _mailer = mailer;
    }

    public Task OnTicketChangedAsync(TicketChangedEventData change) => _mailer.OnChangedAsync(change);
}

/// <summary>
/// Mail about a saved comment. Same two rules as <see cref="TicketChangedHandler"/>: wait for the commit, and use a
/// scope of its own, because ABP disposes the one the handler runs in as soon as the handler returns.
/// </summary>
public class TicketCommentedHandler : ILocalEventHandler<TicketCommentedEventData>, ITransientDependency
{
    private readonly IUnitOfWorkManager _unitOfWork;
    private readonly IServiceScopeFactory _scopeFactory;

    public TicketCommentedHandler(IUnitOfWorkManager unitOfWork, IServiceScopeFactory scopeFactory)
    {
        _unitOfWork = unitOfWork;
        _scopeFactory = scopeFactory;
    }

    public async Task HandleEventAsync(TicketCommentedEventData eventData)
    {
        var current = _unitOfWork.Current;
        if (current is null)
        {
            await RunAsync(eventData);
            return;
        }

        current.OnCompleted(() => RunAsync(eventData));
    }

    private async Task RunAsync(TicketCommentedEventData eventData)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TicketMailer>().OnCommentedAsync(eventData);
    }
}

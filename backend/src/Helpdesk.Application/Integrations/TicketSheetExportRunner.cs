using System;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Categories;
using Helpdesk.Tickets;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace Helpdesk.Integrations;

/// <summary>
/// Reads the committed ticket, builds the summary row and sends it. It runs after the ticket change has
/// been saved and never throws: a failed export must not turn a saved change into an error for the caller.
/// </summary>
[ExposeServices(typeof(ITicketChangeSubscriber), IncludeSelf = true)]
public class TicketSheetExportRunner : ITicketChangeSubscriber, ITransientDependency
{
    private readonly SheetsExporter _exporter;
    private readonly IRepository<Ticket, Guid> _tickets;
    private readonly IRepository<Category, Guid> _categories;
    private readonly IIdentityUserRepository _users;
    private readonly IUnitOfWorkManager _unitOfWork;
    private readonly ILogger<TicketSheetExportRunner> _logger;

    public TicketSheetExportRunner(
        SheetsExporter exporter,
        IRepository<Ticket, Guid> tickets,
        IRepository<Category, Guid> categories,
        IIdentityUserRepository users,
        IUnitOfWorkManager unitOfWork,
        ILogger<TicketSheetExportRunner> logger)
    {
        _exporter = exporter;
        _tickets = tickets;
        _categories = categories;
        _users = users;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public Task OnTicketChangedAsync(TicketChangedEventData change) => RunAsync(change);

    public async Task RunAsync(TicketChangedEventData eventData)
    {
        if (!_exporter.Enabled)
        {
            return;
        }

        try
        {
            TicketSheetRow? row;
            using (var uow = _unitOfWork.Begin(requiresNew: true))
            {
                row = await BuildRowAsync(eventData);
                await uow.CompleteAsync();
            }

            if (row is not null)
            {
                await _exporter.SendAsync(row);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Sheets export for ticket {TicketId} failed", eventData.TicketId);
        }
    }

    private async Task<TicketSheetRow?> BuildRowAsync(TicketChangedEventData eventData)
    {
        var ticket = await _tickets.FindAsync(candidate => candidate.Id == eventData.TicketId, includeDetails: false);
        if (ticket is null)
        {
            return null;
        }

        var category = await _categories.FindAsync(ticket.CategoryId);
        var row = new TicketSheetRow
        {
            Code = ticket.Code,
            Category = category?.Name ?? "Unknown category",
            Status = ticket.Status.ToString(),
            Priority = ticket.Priority.ToString(),
            CreatedAt = ticket.CreationTime,
            UpdatedAt = ticket.LastModificationTime ?? ticket.CreationTime,
            FirstResponseDueAt = ticket.FirstResponseDueAt,
            ResolutionDueAt = ticket.ResolutionDueAt,
            FirstRespondedAt = ticket.FirstRespondedAt,
            ResolvedAt = ticket.ResolvedAt,
            ClosedAt = ticket.ClosedAt,
            ReopenCount = ticket.ReopenCount,
            Event = eventData.Kind.ToString(),
        };

        if (_exporter.IncludeTitle)
        {
            row.Title = ticket.Title;
        }

        if (_exporter.IncludePeople)
        {
            var ids = new[] { ticket.RequesterId, ticket.AssigneeId ?? Guid.Empty }.Where(id => id != Guid.Empty).ToList();
            var people = (await _users.GetListByIdsAsync(ids)).ToDictionary(user => user.Id);
            row.Requester = people.TryGetValue(ticket.RequesterId, out var requester) ? requester.UserName : null;
            row.Assignee = ticket.AssigneeId is { } assignee && people.TryGetValue(assignee, out var agent)
                ? agent.UserName
                : null;
        }

        return row;
    }
}

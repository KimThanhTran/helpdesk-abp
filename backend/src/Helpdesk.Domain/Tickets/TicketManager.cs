using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Categories;
using Helpdesk.Sla;
using Microsoft.Extensions.Options;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Helpdesk.Tickets;

/// <summary>Rules that involve more than one record: numbering, categories, SLA targets and the automatic close.</summary>
public class TicketManager : DomainService
{
    private readonly IRepository<Ticket, Guid> _tickets;
    private readonly IRepository<Category, Guid> _categories;
    private readonly ITicketCodeGenerator _codes;
    private readonly BusinessCalendar _calendar;
    private readonly HelpdeskOptions _options;

    public TicketManager(
        IRepository<Ticket, Guid> tickets,
        IRepository<Category, Guid> categories,
        ITicketCodeGenerator codes,
        BusinessCalendar calendar,
        IOptions<HelpdeskOptions> options)
    {
        _tickets = tickets;
        _categories = categories;
        _codes = codes;
        _calendar = calendar;
        _options = options.Value;
    }

    public int ReopenWindowDays => _options.ReopenWindowDays;

    public async Task<Ticket> CreateAsync(
        Guid requesterId, Guid categoryId, string title, string description, TicketPriority priority)
    {
        var category = await _categories.GetAsync(categoryId);
        if (!category.IsActive)
        {
            throw HelpdeskErrors.CategoryInactive();
        }

        var now = Clock.Now;
        var (firstResponseDue, resolutionDue) = Targets(priority, now);
        var code = await _codes.NextAsync(now.Year);

        return new Ticket(
            GuidGenerator.Create(), code, categoryId, requesterId, title, description, priority,
            firstResponseDue, resolutionDue, now);
    }

    /// <summary>Changes the priority. The targets are re-measured from when the ticket was created, not from the change.</summary>
    public void ChangePriority(Ticket ticket, TicketPriority priority, Guid actorId)
    {
        var now = Clock.Now;
        var (firstResponseDue, resolutionDue) = Targets(priority, ticket.CreationTime);
        ticket.ChangePriority(priority, firstResponseDue, resolutionDue, actorId, now);
    }

    /// <summary>Closes every resolved ticket that has waited long enough for an answer. Returns the tickets closed.</summary>
    public async Task<IReadOnlyList<Ticket>> AutoCloseDueAsync()
    {
        var now = Clock.Now;
        var cutoff = now.AddDays(-_options.AutoCloseAfterDays);
        var candidates = await _tickets.GetListAsync(ticket =>
            ticket.Status == TicketStatus.Resolved && ticket.ResolvedAt <= cutoff);

        var closed = candidates.Where(ticket => ticket.AutoClose(now, _options.AutoCloseAfterDays)).ToList();
        foreach (var ticket in closed)
        {
            await _tickets.UpdateAsync(ticket);
        }

        return closed;
    }

    private (DateTime FirstResponseDue, DateTime ResolutionDue) Targets(TicketPriority priority, DateTime from)
    {
        var targets = SlaPolicy.For(priority);
        return (
            _calendar.AddBusinessHours(from, targets.FirstResponseHours),
            _calendar.AddBusinessHours(from, targets.ResolutionHours));
    }
}

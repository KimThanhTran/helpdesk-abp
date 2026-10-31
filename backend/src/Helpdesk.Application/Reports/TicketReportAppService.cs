using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Categories;
using Helpdesk.Permissions;
using Helpdesk.Tickets;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Helpdesk.Reports;

[Authorize(HelpdeskPermissions.Reports.Default)]
public class TicketReportAppService : HelpdeskAppService, ITicketReportAppService
{
    private readonly IRepository<Ticket, Guid> _tickets;
    private readonly IRepository<Category, Guid> _categories;

    public TicketReportAppService(IRepository<Ticket, Guid> tickets, IRepository<Category, Guid> categories)
    {
        _tickets = tickets;
        _categories = categories;
    }

    public async Task<TicketSummaryDto> GetSummaryAsync(GetReportInput input)
    {
        var now = Clock.Now;
        var to = input.To ?? now;
        var from = input.From ?? to.AddDays(-30);

        var inRange = (await _tickets.GetQueryableAsync())
            .Where(ticket => ticket.CreationTime >= from && ticket.CreationTime < to);

        var byStatus = await AsyncExecuter.ToListAsync(
            inRange.GroupBy(ticket => ticket.Status).Select(group => new { group.Key, Count = group.Count() }));
        var byPriority = await AsyncExecuter.ToListAsync(
            inRange.GroupBy(ticket => ticket.Priority).Select(group => new { group.Key, Count = group.Count() }));
        var byCategory = await AsyncExecuter.ToListAsync(
            inRange.GroupBy(ticket => ticket.CategoryId).Select(group => new { group.Key, Count = group.Count() }));

        var categoryIds = byCategory.Select(row => row.Key).ToList();
        var categoryNames = (await _categories.GetListAsync(category => categoryIds.Contains(category.Id)))
            .ToDictionary(category => category.Id, category => category.Name);

        // overdue is about right now, so it looks at every active ticket, not only those created in the range
        var active = (await _tickets.GetQueryableAsync())
            .Where(ticket => ticket.Status == TicketStatus.New || ticket.Status == TicketStatus.Open);
        var overdueNow = await AsyncExecuter.CountAsync(active.Where(ticket => ticket.ResolutionDueAt < now));
        var noFirstResponse = await AsyncExecuter.CountAsync(
            active.Where(ticket => ticket.FirstRespondedAt == null && ticket.FirstResponseDueAt < now));

        var resolved = await AsyncExecuter.ToListAsync(
            inRange.Where(ticket => ticket.ResolvedAt != null)
                .Select(ticket => new { ticket.CreationTime, ResolvedAt = ticket.ResolvedAt!.Value }));

        return new TicketSummaryDto
        {
            From = from,
            To = to,
            Total = byStatus.Sum(row => row.Count),
            ByStatus = byStatus.Select(row => new CountByDto<TicketStatus> { Key = row.Key, Count = row.Count })
                .OrderBy(row => row.Key).ToList(),
            ByPriority = byPriority.Select(row => new CountByDto<TicketPriority> { Key = row.Key, Count = row.Count })
                .OrderBy(row => row.Key).ToList(),
            ByCategory = byCategory
                .Select(row => new CountByDto<string>
                {
                    Key = categoryNames.GetValueOrDefault(row.Key, "Unknown category"),
                    Count = row.Count,
                })
                .OrderByDescending(row => row.Count).ThenBy(row => row.Key).ToList(),
            OverdueNow = overdueNow,
            WaitingForFirstResponseOverdue = noFirstResponse,
            AverageResolutionHours = resolved.Count == 0
                ? null
                : Math.Round(resolved.Average(row => (row.ResolvedAt - row.CreationTime).TotalHours), 1),
        };
    }
}

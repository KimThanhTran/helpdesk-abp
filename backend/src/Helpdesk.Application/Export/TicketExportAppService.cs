using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Categories;
using Helpdesk.Permissions;
using Helpdesk.Reports;
using Helpdesk.Tickets;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace Helpdesk.Export;

[Authorize(HelpdeskPermissions.Tickets.Work)]
public class TicketExportAppService : HelpdeskAppService, ITicketExportAppService
{
    // shortcut: one workbook built in memory, fine up to a few thousand rows; stream it when that hurts
    internal const int MaxRows = 5000;

    private readonly IRepository<Ticket, Guid> _tickets;
    private readonly IRepository<Category, Guid> _categories;
    private readonly IIdentityUserRepository _users;
    private readonly ITicketReportAppService _reports;

    public TicketExportAppService(
        IRepository<Ticket, Guid> tickets,
        IRepository<Category, Guid> categories,
        IIdentityUserRepository users,
        ITicketReportAppService reports)
    {
        _tickets = tickets;
        _categories = categories;
        _users = users;
        _reports = reports;
    }

    public async Task<IRemoteStreamContent> GetTicketsAsync(ExportTicketsInput input)
    {
        var query = TicketQueries.Filter(
            await _tickets.GetQueryableAsync(), input.Filter, input.Status, input.Priority, input.CategoryId);
        query = TicketQueries.InScope(query, input.Scope, CurrentUser.GetId());

        var rows = await AsyncExecuter.ToListAsync(
            TicketQueries.QueueOrder(query).Take(MaxRows + 1).Select(t => new
            {
                t.Code, t.Title, t.Status, t.Priority, t.CategoryId, t.RequesterId, t.AssigneeId, t.CreationTime,
                t.FirstResponseDueAt, t.ResolutionDueAt, t.FirstRespondedAt, t.ResolvedAt, t.ClosedAt,
                t.PausedBusinessHours, t.ReopenCount,
            }));
        var truncated = rows.Count > MaxRows;
        rows = rows.Take(MaxRows).ToList();

        var people = await NamesAsync(rows.SelectMany(r => new[] { r.RequesterId, r.AssigneeId ?? Guid.Empty }));
        var categoryIds = rows.Select(r => r.CategoryId).Distinct().ToList();
        var categories = (await _categories.GetListAsync(c => categoryIds.Contains(c.Id))).ToDictionary(c => c.Id, c => c.Name);
        var now = Clock.Now;

        var sheet = new XlsxSheet("Tickets", new[]
        {
            "Code", "Title", "Status", "Priority", "Category", "Requester", "Assignee", "Created (UTC)",
            "First response due (UTC)", "Resolution due (UTC)", "First answered (UTC)", "Resolved (UTC)", "Closed (UTC)",
            "Overdue", "Hours paused", "Reopened",
        });
        foreach (var r in rows)
        {
            var open = r.Status is TicketStatus.New or TicketStatus.Open;
            sheet.Rows.Add(new object?[]
            {
                r.Code, r.Title, r.Status.ToString(), r.Priority.ToString(),
                categories.GetValueOrDefault(r.CategoryId, "Unknown category"),
                people.GetValueOrDefault(r.RequesterId, "Unknown user"),
                r.AssigneeId is { } assignee ? people.GetValueOrDefault(assignee, "Unknown user") : null,
                r.CreationTime, r.FirstResponseDueAt, r.ResolutionDueAt, r.FirstRespondedAt, r.ResolvedAt, r.ClosedAt,
                open && now > r.ResolutionDueAt ? "Yes" : "No",
                Math.Round(r.PausedBusinessHours, 2), r.ReopenCount,
            });
        }

        var sheets = new List<XlsxSheet> { sheet };
        if (truncated)
        {
            var note = new XlsxSheet("Notes", new[] { "Note" });
            note.Rows.Add(new object?[] { $"Only the first {MaxRows} matching tickets are included. Narrow the filters to see the rest." });
            sheets.Add(note);
        }

        return File("helpdesk-tickets", XlsxWriter.Write(sheets));
    }

    [Authorize(HelpdeskPermissions.Reports.Default)]
    public async Task<IRemoteStreamContent> GetSummaryAsync(GetReportInput input)
    {
        var summary = await _reports.GetSummaryAsync(input);

        var totals = new XlsxSheet("Summary", new[] { "Measure", "Value" });
        totals.Rows.Add(new object?[] { "From (UTC)", summary.From });
        totals.Rows.Add(new object?[] { "To (UTC)", summary.To });
        totals.Rows.Add(new object?[] { "Tickets created", summary.Total });
        totals.Rows.Add(new object?[] { "Overdue now", summary.OverdueNow });
        totals.Rows.Add(new object?[] { "No first answer, past target", summary.WaitingForFirstResponseOverdue });
        totals.Rows.Add(new object?[] { "Average hours to resolve", summary.AverageResolutionHours is { } hours ? Math.Round(hours, 1) : null });

        static XlsxSheet Counts<TKey>(string name, IEnumerable<CountByDto<TKey>> rows)
        {
            var sheet = new XlsxSheet(name, new[] { name.Replace("By ", string.Empty, StringComparison.Ordinal), "Tickets" });
            foreach (var row in rows)
            {
                sheet.Rows.Add(new object?[] { Convert.ToString(row.Key, CultureInfo.InvariantCulture), row.Count });
            }

            return sheet;
        }

        return File("helpdesk-report", XlsxWriter.Write(new[]
        {
            totals,
            Counts("By status", summary.ByStatus),
            Counts("By priority", summary.ByPriority),
            Counts("By category", summary.ByCategory),
        }));
    }

    private RemoteStreamContent File(string stem, byte[] bytes) => new(
        new MemoryStream(bytes),
        $"{stem}-{Clock.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.xlsx",
        XlsxWriter.ContentType);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids)
    {
        var wanted = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return new();
        }

        var users = await _users.GetListByIdsAsync(wanted);
        return users.ToDictionary(user => user.Id, user => string.IsNullOrWhiteSpace(user.Name) ? user.UserName : $"{user.Name} {user.Surname}".Trim());
    }
}

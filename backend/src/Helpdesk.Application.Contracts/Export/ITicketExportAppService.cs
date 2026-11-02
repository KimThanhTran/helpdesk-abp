using System;
using System.Threading.Tasks;
using Helpdesk.Reports;
using Helpdesk.Tickets;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Helpdesk.Export;

public class ExportTicketsInput
{
    /// <summary>Matches the code or the title, ignoring case.</summary>
    public string? Filter { get; set; }

    public TicketStatus? Status { get; set; }

    public TicketPriority? Priority { get; set; }

    public Guid? CategoryId { get; set; }

    /// <summary>The same scopes as the queue; the default is every ticket that matches.</summary>
    public QueueScope Scope { get; set; } = QueueScope.All;
}

/// <summary>Excel files (.xlsx) for agents. Unlike the Google Sheets export they carry titles and names: the agent asks for them.</summary>
public interface ITicketExportAppService : IApplicationService
{
    /// <summary>The tickets that match the filters, as one sheet. At most 5,000 rows; times are UTC.</summary>
    Task<IRemoteStreamContent> GetTicketsAsync(ExportTicketsInput input);

    /// <summary>The report (totals, by status, priority and category), one sheet each. Times are UTC.</summary>
    Task<IRemoteStreamContent> GetSummaryAsync(GetReportInput input);
}

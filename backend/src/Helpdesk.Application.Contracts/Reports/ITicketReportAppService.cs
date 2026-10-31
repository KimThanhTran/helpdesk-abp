using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Helpdesk.Reports;

public interface ITicketReportAppService : IApplicationService
{
    Task<TicketSummaryDto> GetSummaryAsync(GetReportInput input);
}

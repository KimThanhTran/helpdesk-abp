using System;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Reports;
using Helpdesk.Tickets;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Reports;

public abstract class TicketReportAppServiceTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ITicketReportAppService _reports;

    protected TicketReportAppServiceTests()
    {
        _reports = GetRequiredService<ITicketReportAppService>();
    }

    [Fact]
    public async Task The_summary_counts_tickets_by_status_priority_and_category()
    {
        await CreateAs(Alice, "New one");
        await CreateAs(Bob, "New two", "Facilities");
        var taken = await OpenTicket("Open one");
        using (As(Agent))
        {
            await Tickets.ChangePriorityAsync(taken.Id, new ChangePriorityDto { Priority = TicketPriority.High });
        }

        using (As(Agent))
        {
            var summary = await _reports.GetSummaryAsync(new GetReportInput());

            summary.Total.ShouldBe(3);
            summary.ByStatus.Single(row => row.Key == TicketStatus.New).Count.ShouldBe(2);
            summary.ByStatus.Single(row => row.Key == TicketStatus.Open).Count.ShouldBe(1);
            summary.ByPriority.Single(row => row.Key == TicketPriority.High).Count.ShouldBe(1);
            summary.ByCategory.Single(row => row.Key == "IT support").Count.ShouldBe(2);
            summary.ByCategory.Single(row => row.Key == "Facilities").Count.ShouldBe(1);
            summary.AverageResolutionHours.ShouldBeNull(); // nothing resolved yet
        }
    }

    [Fact]
    public async Task The_summary_reports_the_average_hours_to_resolve()
    {
        var open = await OpenTicket();
        Time.Advance(TimeSpan.FromHours(6));
        using (As(Agent))
        {
            await Tickets.ResolveAsync(open.Id, new ResolveTicketDto { Resolution = "Done" });
            var summary = await _reports.GetSummaryAsync(new GetReportInput());

            summary.AverageResolutionHours.ShouldNotBeNull();
            summary.AverageResolutionHours!.Value.ShouldBe(6, tolerance: 0.1);
        }
    }

    [Fact]
    public async Task The_summary_flags_tickets_that_are_overdue_right_now()
    {
        await CreateAs(Alice);
        using (As(Agent))
        {
            (await _reports.GetSummaryAsync(new GetReportInput())).OverdueNow.ShouldBe(0);
        }

        Time.Advance(TimeSpan.FromDays(10));
        using (As(Agent))
        {
            var summary = await _reports.GetSummaryAsync(new GetReportInput { From = Clock.Now.AddDays(-30) });

            summary.OverdueNow.ShouldBe(1);
            summary.WaitingForFirstResponseOverdue.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Only_tickets_created_inside_the_range_are_counted()
    {
        await CreateAs(Alice);

        using (As(Agent))
        {
            var future = await _reports.GetSummaryAsync(
                new GetReportInput { From = Clock.Now.AddDays(1), To = Clock.Now.AddDays(2) });
            future.Total.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Requesters_cannot_read_reports()
    {
        using (As(Alice))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _reports.GetSummaryAsync(new GetReportInput()));
        }
    }
}

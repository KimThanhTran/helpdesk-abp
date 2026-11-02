using System;
using System.Linq;
using Helpdesk.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Tickets;

/// <summary>The demo tickets a fresh install ships with (Helpdesk:SeedDemoTickets) must be as sound as real ones.</summary>
public abstract class DemoDataTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Fact]
    public async Task Demo_tickets_have_real_deadlines_measured_from_when_they_were_created()
    {
        var options = GetRequiredService<IOptions<HelpdeskOptions>>().Value;
        options.SeedDemoTickets = true;
        try
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var seeder = GetRequiredService<System.Collections.Generic.IEnumerable<IDataSeedContributor>>()
                    .Single(contributor => contributor is HelpdeskDataSeedContributor);
                await seeder.SeedAsync(new DataSeedContext());
            });
        }
        finally
        {
            options.SeedDemoTickets = false;
        }

        using (As(Agent))
        {
            var all = await Tickets.GetQueueAsync(new GetTicketListInput { Scope = QueueScope.All, MaxResultCount = 100 });

            all.TotalCount.ShouldBeGreaterThanOrEqualTo(12);
            foreach (var ticket in all.Items)
            {
                ticket.FirstResponseDueAt.Year.ShouldBeGreaterThan(2000, ticket.Code);
                ticket.FirstResponseDueAt.ShouldBeGreaterThan(ticket.CreationTime, ticket.Code);
                ticket.ResolutionDueAt.ShouldBeGreaterThan(ticket.FirstResponseDueAt, ticket.Code);
            }
        }
    }

    [Fact]
    public async Task A_ticket_that_is_not_saved_yet_cannot_have_its_deadline_measured_from_nothing()
    {
        var manager = GetRequiredService<TicketManager>();
        Ticket unsaved = null!;
        await WithUnitOfWorkAsync(async () =>
        {
            unsaved = await manager.CreateAsync(UserId(Alice), CategoryId(), "Printer jams", "Page two jams", TicketPriority.Normal);
        });

        Should.Throw<InvalidOperationException>(() => manager.ChangePriority(unsaved, TicketPriority.Urgent, UserId(Agent)));
    }
}

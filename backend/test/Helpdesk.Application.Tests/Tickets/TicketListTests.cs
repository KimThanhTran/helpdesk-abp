using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Tickets;

public abstract class TicketListTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Fact]
    public async Task My_tickets_lists_only_the_ones_I_opened()
    {
        await CreateAs(Alice, "Alice one");
        await CreateAs(Alice, "Alice two");
        await CreateAs(Bob, "Bob one");

        using (As(Alice))
        {
            var mine = await Tickets.GetMyListAsync(new GetTicketListInput());
            mine.TotalCount.ShouldBe(2);
            mine.Items.Select(ticket => ticket.Title).ShouldBe(new[] { "Alice two", "Alice one" }); // newest first
            mine.Items.ShouldAllBe(ticket => ticket.Requester.Name == "alice");
        }

        using (As(Bob))
        {
            (await Tickets.GetMyListAsync(new GetTicketListInput())).Items.Single().Title.ShouldBe("Bob one");
        }
    }

    [Fact]
    public async Task Even_an_agents_my_tickets_only_shows_tickets_they_opened_themselves()
    {
        await CreateAs(Alice);

        using (As(Agent))
        {
            (await Tickets.GetMyListAsync(new GetTicketListInput())).TotalCount.ShouldBe(0);
        }
    }

    [Fact]
    public async Task The_queue_is_for_agents_only()
    {
        using (As(Alice))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Tickets.GetQueueAsync(new GetTicketListInput()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Tickets.GetAssignableAgentsAsync());
        }
    }

    [Fact]
    public async Task The_unassigned_queue_shows_only_new_tickets_and_a_taken_one_moves_to_mine()
    {
        var first = await CreateAs(Alice, "First");
        await CreateAs(Bob, "Second");

        using (As(Agent))
        {
            (await Tickets.GetQueueAsync(new GetTicketListInput { Scope = QueueScope.Unassigned })).TotalCount.ShouldBe(2);

            await Tickets.ClaimAsync(first.Id);

            var unassigned = await Tickets.GetQueueAsync(new GetTicketListInput { Scope = QueueScope.Unassigned });
            unassigned.Items.Select(ticket => ticket.Title).ShouldBe(new[] { "Second" });

            var mine = await Tickets.GetQueueAsync(new GetTicketListInput { Scope = QueueScope.Mine });
            mine.Items.Select(ticket => ticket.Title).ShouldBe(new[] { "First" });
            mine.Items.Single().Assignee!.Name.ShouldBe("agent1");
        }
    }

    [Fact]
    public async Task All_shows_every_ticket_including_finished_ones()
    {
        var closed = await ClosedTicket();
        await CreateAs(Bob, "Still new");

        using (As(Agent))
        {
            (await Tickets.GetQueueAsync(new GetTicketListInput { Scope = QueueScope.All })).TotalCount.ShouldBe(2);
            (await Tickets.GetQueueAsync(new GetTicketListInput { Scope = QueueScope.All, Status = TicketStatus.Closed }))
                .Items.Single().Id.ShouldBe(closed.Id);
        }
    }

    [Fact]
    public async Task The_queue_puts_urgent_tickets_first_by_default()
    {
        await CreateAs(Alice, "Routine");
        var urgent = await CreateAs(Bob, "Server down");

        using (As(Agent))
        {
            await Tickets.ChangePriorityAsync(urgent.Id, new ChangePriorityDto { Priority = TicketPriority.Urgent });

            var queue = await Tickets.GetQueueAsync(new GetTicketListInput { Scope = QueueScope.Unassigned });

            queue.Items.Select(ticket => ticket.Title).ShouldBe(new[] { "Server down", "Routine" });
        }
    }

    [Fact]
    public async Task Filters_ignore_case_and_combine()
    {
        await CreateAs(Alice, "Printer jams");
        await CreateAs(Alice, "VPN is down", "Facilities");
        await CreateAs(Bob, "Another PRINTER problem");

        using (As(Agent))
        {
            (await Tickets.GetQueueAsync(new GetTicketListInput { Scope = QueueScope.All, Filter = "printer" }))
                .TotalCount.ShouldBe(2);
            (await Tickets.GetQueueAsync(new GetTicketListInput { Scope = QueueScope.All, CategoryId = CategoryId("Facilities") }))
                .Items.Single().Title.ShouldBe("VPN is down");
            (await Tickets.GetQueueAsync(new GetTicketListInput { Scope = QueueScope.All, Filter = "printer", Priority = TicketPriority.High }))
                .TotalCount.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Filtering_by_ticket_code_works()
    {
        await CreateAs(Alice, "One");
        var second = await CreateAs(Alice, "Two");

        using (As(Alice))
        {
            var found = await Tickets.GetMyListAsync(new GetTicketListInput { Filter = second.Code.ToLowerInvariant() });
            found.Items.Single().Title.ShouldBe("Two");
        }
    }

    [Fact]
    public async Task Paging_returns_the_requested_slice_and_the_full_total()
    {
        for (var i = 1; i <= 5; i++)
        {
            await CreateAs(Alice, $"Ticket {i}");
        }

        using (As(Alice))
        {
            var page = await Tickets.GetMyListAsync(new GetTicketListInput { SkipCount = 2, MaxResultCount = 2 });

            page.TotalCount.ShouldBe(5);
            page.Items.Select(ticket => ticket.Title).ShouldBe(new[] { "Ticket 3", "Ticket 2" });
        }
    }

    [Fact]
    public async Task Sorting_works_on_whitelisted_columns_and_anything_else_is_ignored_safely()
    {
        await CreateAs(Alice, "Bravo");
        await CreateAs(Alice, "Alpha");

        using (As(Alice))
        {
            (await Tickets.GetMyListAsync(new GetTicketListInput { Sorting = "title asc" }))
                .Items.Select(ticket => ticket.Title).ShouldBe(new[] { "Alpha", "Bravo" });
            (await Tickets.GetMyListAsync(new GetTicketListInput { Sorting = "title desc" }))
                .Items.Select(ticket => ticket.Title).ShouldBe(new[] { "Bravo", "Alpha" });

            // not a column we allow: falls back to the default order instead of reaching the query
            var hostile = await Tickets.GetMyListAsync(new GetTicketListInput { Sorting = "title; DROP TABLE AppTickets --" });
            hostile.TotalCount.ShouldBe(2);
        }
    }
}

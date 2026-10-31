using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Tickets;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Integrations;

/// <summary>What leaves the system for Google Sheets: when, and how little.</summary>
public abstract class SheetsExportTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private SheetsRecorder Recorder => GetRequiredService<SheetsRecorder>();

    [Fact]
    public async Task Creating_a_ticket_exports_a_row_without_personal_data_by_default()
    {
        var created = await CreateAs(Alice, "My salary is wrong");

        var row = Recorder.Rows.Single();
        row.Code.ShouldBe(created.Code);
        row.Event.ShouldBe("Created");
        row.Status.ShouldBe("New");
        row.Priority.ShouldBe("Normal");
        row.Category.ShouldBe("IT support");

        // the title may contain personal details and the names identify people: both are off unless switched on
        row.Title.ShouldBeNull();
        row.Requester.ShouldBeNull();
        row.Assignee.ShouldBeNull();
    }

    [Fact]
    public async Task Titles_and_names_are_included_only_when_enabled()
    {
        Recorder.IncludeTitle = true;
        Recorder.IncludePeople = true;

        var created = await CreateAs(Alice, "Printer jams");
        using (As(Agent))
        {
            await Tickets.ClaimAsync(created.Id);
        }

        var assigned = Recorder.Rows.Last();
        assigned.Title.ShouldBe("Printer jams");
        assigned.Requester.ShouldBe("alice");
        assigned.Assignee.ShouldBe("agent1");
        assigned.Event.ShouldBe("Assigned");
        assigned.Status.ShouldBe("Open");
    }

    [Fact]
    public async Task The_description_and_comments_never_leave_the_system()
    {
        var open = await OpenTicket();
        using (As(Agent))
        {
            await Tickets.AddCommentAsync(open.Id, new CreateCommentDto
            {
                Body = "TOP-SECRET-NOTE",
                Visibility = CommentVisibility.Internal,
            });
        }

        Recorder.IncludeTitle = true;
        Recorder.IncludePeople = true;
        using (As(Agent))
        {
            await Tickets.WaitAsync(open.Id, new TicketReasonDto { Reason = "TOP-SECRET-REASON" });
        }

        var serialised = System.Text.Json.JsonSerializer.Serialize(Recorder.Rows);
        serialised.ShouldNotContain("TOP-SECRET");
        serialised.ShouldNotContain("Page two always jams"); // the description
    }

    [Fact]
    public async Task Every_status_change_is_exported_with_the_event_that_caused_it()
    {
        var closed = await ClosedTicket();

        Recorder.Rows.Where(row => row.Code == closed.Code).Select(row => row.Status).ShouldBe(
            new[] { "New", "Open", "Resolved", "Closed" });
    }

    [Fact]
    public async Task A_comment_that_changes_nothing_exports_nothing()
    {
        var open = await OpenTicket();
        var before = Recorder.Rows.Count;

        using (As(Agent))
        {
            await Tickets.AddCommentAsync(open.Id, new CreateCommentDto { Body = "internal chatter", Visibility = CommentVisibility.Internal });
        }

        Recorder.Rows.Count.ShouldBe(before);
    }

    [Fact]
    public async Task The_automatic_close_is_exported_too()
    {
        var resolved = await ResolvedTicket();
        Time.Advance(System.TimeSpan.FromDays(6));

        await WithUnitOfWorkAsync(() => GetRequiredService<TicketManager>().AutoCloseDueAsync());
        // the worker publishes the event; do the same here as it would
        await GetRequiredService<Volo.Abp.EventBus.Local.ILocalEventBus>()
            .PublishAsync(new TicketChangedEventData(resolved.Id, TicketEventKind.AutoClosed));

        var last = Recorder.Rows.Last();
        last.Event.ShouldBe("AutoClosed");
        last.Status.ShouldBe("Closed");
    }
}

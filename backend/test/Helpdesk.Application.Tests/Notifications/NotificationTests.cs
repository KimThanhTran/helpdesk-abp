using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Helpdesk.Tickets;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Notifications;

/// <summary>Collects what would have been pushed to clients, so tests can check it without a network.</summary>
public class NotificationRecorder : ISingletonDependency
{
    private readonly ConcurrentQueue<Delivery> _deliveries = new();

    public IReadOnlyList<Delivery> Deliveries => _deliveries.ToList();

    public void Add(Delivery delivery) => _deliveries.Enqueue(delivery);

    public record Delivery(TicketNotification Notification, IReadOnlyCollection<Guid> Users, bool ToAgents);
}

[Dependency(ServiceLifetime.Transient, ReplaceServices = true)]
[ExposeServices(typeof(ITicketNotifier))]
public class RecordingTicketNotifier : ITicketNotifier
{
    private readonly NotificationRecorder _recorder;

    public RecordingTicketNotifier(NotificationRecorder recorder)
    {
        _recorder = recorder;
    }

    public Task SendAsync(TicketNotification notification, IReadOnlyCollection<Guid> userIds, bool toAgents)
    {
        _recorder.Add(new NotificationRecorder.Delivery(notification, userIds, toAgents));
        return Task.CompletedTask;
    }
}

public abstract class NotificationTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private IReadOnlyList<NotificationRecorder.Delivery> Deliveries => GetRequiredService<NotificationRecorder>().Deliveries;

    [Fact]
    public async Task A_new_ticket_notifies_its_requester_and_the_agents_without_naming_anyone_else()
    {
        var created = await CreateAs(Alice, "Printer jams");

        var delivery = Deliveries.Single();
        delivery.Notification.Kind.ShouldBe("Created");
        delivery.Notification.Code.ShouldBe(created.Code);
        delivery.Notification.Status.ShouldBe("New");
        delivery.Notification.ActorId.ShouldBe(UserId(Alice));
        delivery.Users.ShouldBe(new[] { UserId(Alice) });
        delivery.ToAgents.ShouldBeTrue();
    }

    [Fact]
    public async Task Taking_a_ticket_notifies_the_requester_and_the_assignee()
    {
        var created = await CreateAs(Alice);
        using (As(Agent))
        {
            await Tickets.ClaimAsync(created.Id);
        }

        var delivery = Deliveries.Last();
        delivery.Notification.Kind.ShouldBe("Assigned");
        delivery.Notification.Status.ShouldBe("Open");
        delivery.Notification.ActorId.ShouldBe(UserId(Agent));
        delivery.Users.ShouldBe(new[] { UserId(Alice), UserId(Agent) }, ignoreOrder: true);
    }

    [Fact]
    public async Task Every_status_change_is_announced_with_the_status_after_the_change()
    {
        await ClosedTicket();

        Deliveries.Select(d => d.Notification.Status).ShouldBe(new[] { "New", "Open", "Resolved", "Closed" });
    }

    [Fact]
    public async Task An_internal_note_notifies_nobody()
    {
        var open = await OpenTicket();
        var before = Deliveries.Count;

        using (As(Agent))
        {
            await Tickets.AddCommentAsync(open.Id, new CreateCommentDto { Body = "SECRET-NOTE", Visibility = CommentVisibility.Internal });
        }

        Deliveries.Count.ShouldBe(before);
    }

    [Fact]
    public async Task A_requesters_reply_that_resumes_a_waiting_ticket_is_announced()
    {
        var open = await OpenTicket();
        using (As(Agent))
        {
            await Tickets.WaitAsync(open.Id, new TicketReasonDto { Reason = "Need the model" });
        }

        using (As(Alice))
        {
            await Tickets.AddCommentAsync(open.Id, new CreateCommentDto { Body = "A LaserJet" });
        }

        var last = Deliveries.Last();
        last.Notification.Status.ShouldBe("Open");
        last.Notification.ActorId.ShouldBe(UserId(Alice));
    }

    [Fact]
    public async Task A_notification_says_what_happened_and_never_what_was_written()
    {
        var created = await CreateAs(Alice, "Printer jams");
        using (As(Agent))
        {
            await Tickets.ClaimAsync(created.Id);
            await Tickets.WaitAsync(created.Id, new TicketReasonDto { Reason = "WAIT-REASON-TEXT" });
            await Tickets.AddCommentAsync(created.Id, new CreateCommentDto { Body = "COMMENT-BODY-TEXT" });
        }

        var everything = JsonSerializer.Serialize(Deliveries.Select(d => d.Notification));

        everything.ShouldNotContain("WAIT-REASON-TEXT");
        everything.ShouldNotContain("COMMENT-BODY-TEXT");
        everything.ShouldNotContain("Page two always jams"); // the description
    }

    [Fact]
    public async Task A_ticket_that_changes_back_and_forth_gets_a_unique_id_per_notification()
    {
        await ClosedTicket();

        Deliveries.Select(d => d.Notification.EventId).Distinct().Count().ShouldBe(Deliveries.Count);
    }
}

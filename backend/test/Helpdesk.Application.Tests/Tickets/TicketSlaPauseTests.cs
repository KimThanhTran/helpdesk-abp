using System;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Tickets;

/// <summary>
/// While a ticket waits for the requester the SLA clock is stopped. The tests run on a calendar that is open
/// around the clock (see HelpdeskDomainTestModule), so "two days" is exactly 48 working hours.
/// </summary>
public abstract class TicketSlaPauseTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    private async Task<TicketDto> WaitFor(TimeSpan duration, TicketDto ticket, bool resumeByRequesterReply = false)
    {
        using (As(Agent))
        {
            await Tickets.WaitAsync(ticket.Id, new TicketReasonDto { Reason = "Need the model number" });
        }

        Time.Advance(duration);

        if (resumeByRequesterReply)
        {
            using (As(Alice))
            {
                await Tickets.AddCommentAsync(ticket.Id, new CreateCommentDto { Body = "A LaserJet 400" });
            }

            return await Reload(ticket.Id);
        }

        using (As(Agent))
        {
            return await Tickets.ResumeAsync(ticket.Id);
        }
    }

    [Fact]
    public async Task Resuming_moves_both_targets_by_the_time_spent_waiting()
    {
        var open = await OpenTicket();

        var resumed = await WaitFor(TimeSpan.FromDays(2), open);

        resumed.Status.ShouldBe(TicketStatus.Open);
        resumed.WaitingSince.ShouldBeNull();
        resumed.PausedBusinessHours.ShouldBe(48, tolerance: 0.1);
        (resumed.ResolutionDueAt - open.ResolutionDueAt).ShouldBe(TimeSpan.FromDays(2), tolerance: OneMinute);
        (resumed.FirstResponseDueAt - open.FirstResponseDueAt).ShouldBe(TimeSpan.FromDays(2), tolerance: OneMinute);
    }

    [Fact]
    public async Task A_reply_from_the_requester_resumes_the_ticket_and_moves_the_targets_too()
    {
        var open = await OpenTicket();

        var resumed = await WaitFor(TimeSpan.FromDays(1), open, resumeByRequesterReply: true);

        resumed.Status.ShouldBe(TicketStatus.Open);
        resumed.PausedBusinessHours.ShouldBe(24, tolerance: 0.1);
        (resumed.ResolutionDueAt - open.ResolutionDueAt).ShouldBe(TimeSpan.FromDays(1), tolerance: OneMinute);
    }

    [Fact]
    public async Task Several_waits_add_up()
    {
        var open = await OpenTicket();

        await WaitFor(TimeSpan.FromDays(1), open);
        var again = await WaitFor(TimeSpan.FromDays(2), open);

        again.PausedBusinessHours.ShouldBe(72, tolerance: 0.2);
        (again.ResolutionDueAt - open.ResolutionDueAt).ShouldBe(TimeSpan.FromDays(3), tolerance: TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task A_waiting_ticket_is_never_flagged_overdue_however_long_it_waits()
    {
        var open = await OpenTicket();
        using (As(Agent))
        {
            await Tickets.WaitAsync(open.Id, new TicketReasonDto { Reason = "Need the model number" });
        }

        Time.Advance(TimeSpan.FromDays(60));

        var waiting = await Reload(open.Id);
        waiting.Status.ShouldBe(TicketStatus.Waiting);
        waiting.WaitingSince.ShouldNotBeNull();
        waiting.IsResolutionOverdue.ShouldBeFalse();
        waiting.IsFirstResponseOverdue.ShouldBeFalse();
    }

    [Fact]
    public async Task The_clock_starts_again_after_the_wait_and_the_ticket_can_still_become_overdue()
    {
        var open = await OpenTicket();
        var resumed = await WaitFor(TimeSpan.FromDays(5), open);
        resumed.IsResolutionOverdue.ShouldBeFalse();

        Time.Advance(TimeSpan.FromDays(10)); // well past the moved target

        (await Reload(open.Id)).IsResolutionOverdue.ShouldBeTrue();
    }

    [Fact]
    public async Task Changing_priority_after_a_wait_keeps_the_time_already_paused()
    {
        var open = await OpenTicket();
        await WaitFor(TimeSpan.FromDays(1), open);

        TicketDto urgent;
        using (As(Agent))
        {
            urgent = await Tickets.ChangePriorityAsync(open.Id, new ChangePriorityDto { Priority = TicketPriority.Urgent });
        }

        // Urgent is resolved within 8 working hours; the day spent waiting is added on top
        (urgent.ResolutionDueAt - urgent.CreationTime).ShouldBe(TimeSpan.FromHours(8 + 24), tolerance: TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task An_already_answered_ticket_keeps_its_first_response_target()
    {
        var open = await OpenTicket();
        using (As(Agent))
        {
            await Tickets.AddCommentAsync(open.Id, new CreateCommentDto { Body = "On it" });
        }

        var before = await Reload(open.Id);
        var resumed = await WaitFor(TimeSpan.FromDays(2), open);

        resumed.FirstResponseDueAt.ShouldBe(before.FirstResponseDueAt);
        resumed.ResolutionDueAt.ShouldBeGreaterThan(before.ResolutionDueAt);
    }
}

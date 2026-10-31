using System;
using System.Linq;
using Shouldly;
using Xunit;
using static Helpdesk.Tickets.TestTickets;

namespace Helpdesk.Tickets;

public class TicketTests
{
    // ------------------------------------------------------------------ creation

    [Fact]
    public void A_new_ticket_starts_new_and_unassigned_with_a_created_event()
    {
        var ticket = New();

        ticket.Status.ShouldBe(TicketStatus.New);
        ticket.AssigneeId.ShouldBeNull();
        ticket.ReopenCount.ShouldBe(0);
        ticket.Events.Single().Kind.ShouldBe(TicketEventKind.Created);
        ticket.Events.Single().ActorId.ShouldBe(Requester);
    }

    [Fact]
    public void Title_and_description_are_trimmed()
    {
        var ticket = new Ticket(Guid.NewGuid(), "HD-2026-000002", Guid.NewGuid(), Requester, "  Printer  ", "  Jams  ",
            TicketPriority.Low, T0.AddHours(1), T0.AddHours(2), T0);

        ticket.Title.ShouldBe("Printer");
        ticket.Description.ShouldBe("Jams");
    }

    [Fact]
    public void Empty_title_or_description_is_refused()
    {
        Should.Throw<ArgumentException>(() => new Ticket(Guid.NewGuid(), "HD-2026-000002", Guid.NewGuid(), Requester,
            " ", "text", TicketPriority.Low, T0, T0, T0));
        Should.Throw<ArgumentException>(() => new Ticket(Guid.NewGuid(), "HD-2026-000002", Guid.NewGuid(), Requester,
            "title", "", TicketPriority.Low, T0, T0, T0));
    }

    // ------------------------------------------------------------------ assignment and priority

    [Fact]
    public void Assigning_a_new_ticket_opens_it_and_records_who_and_to_whom()
    {
        var ticket = New();

        ticket.Assign(Agent, OtherAgent, T0.AddHours(1));

        ticket.Status.ShouldBe(TicketStatus.Open);
        ticket.AssigneeId.ShouldBe(Agent);
        var assigned = ticket.Events.Last();
        assigned.Kind.ShouldBe(TicketEventKind.Assigned);
        assigned.ActorId.ShouldBe(OtherAgent);
        assigned.AssigneeId.ShouldBe(Agent);
        assigned.FromStatus.ShouldBe(TicketStatus.New);
        assigned.ToStatus.ShouldBe(TicketStatus.Open);
    }

    [Fact]
    public void Reassigning_keeps_the_status_and_adds_a_history_line()
    {
        var ticket = InStatus(TicketStatus.Waiting);
        var events = ticket.Events.Count;

        ticket.Assign(OtherAgent, Agent, T0.AddHours(5));

        ticket.Status.ShouldBe(TicketStatus.Waiting);
        ticket.AssigneeId.ShouldBe(OtherAgent);
        ticket.Events.Count.ShouldBe(events + 1);
    }

    [Fact]
    public void Assigning_to_the_current_holder_changes_nothing_and_records_nothing()
    {
        var ticket = InStatus(TicketStatus.Open);
        var events = ticket.Events.Count;

        ticket.Assign(Agent, Agent, T0.AddHours(5));

        ticket.Events.Count.ShouldBe(events);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public void A_finished_ticket_cannot_be_assigned(TicketStatus status)
    {
        var ticket = InStatus(status);

        ((Action)(() => ticket.Assign(OtherAgent, Agent, T0.AddDays(1))))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.InvalidTransition);
    }

    [Fact]
    public void Changing_priority_moves_the_targets_and_records_the_change()
    {
        var ticket = InStatus(TicketStatus.Open);

        ticket.ChangePriority(TicketPriority.Urgent, T0.AddHours(1), T0.AddHours(8), Agent, T0.AddHours(2));

        ticket.Priority.ShouldBe(TicketPriority.Urgent);
        ticket.FirstResponseDueAt.ShouldBe(T0.AddHours(1));
        ticket.ResolutionDueAt.ShouldBe(T0.AddHours(8));
        ticket.Events.Last().Kind.ShouldBe(TicketEventKind.PriorityChanged);
        ticket.Events.Last().Note.ShouldBe("Normal -> Urgent");
    }

    [Fact]
    public void An_answered_ticket_keeps_its_first_response_target_when_priority_changes()
    {
        var ticket = InStatus(TicketStatus.Open);
        ticket.AddComment(Agent, "Looking into it", CommentVisibility.Public, byRequester: false, T0.AddHours(2));
        var firstDue = ticket.FirstResponseDueAt;

        ticket.ChangePriority(TicketPriority.Urgent, T0.AddHours(1), T0.AddHours(8), Agent, T0.AddHours(3));

        ticket.FirstResponseDueAt.ShouldBe(firstDue);
        ticket.ResolutionDueAt.ShouldBe(T0.AddHours(8));
    }

    [Fact]
    public void Setting_the_same_priority_records_nothing()
    {
        var ticket = InStatus(TicketStatus.Open);
        var events = ticket.Events.Count;

        ticket.ChangePriority(TicketPriority.Normal, T0, T0, Agent, T0.AddHours(2));

        ticket.Events.Count.ShouldBe(events);
    }

    // ------------------------------------------------------------------ waiting, resolving, closing

    [Fact]
    public void Waiting_needs_a_note_and_pauses_the_ticket()
    {
        var ticket = InStatus(TicketStatus.Open);

        ((Action)(() => ticket.Wait("  ", Agent, T0.AddHours(2))))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.ReasonRequired);

        ticket.Wait("Need the model number", Agent, T0.AddHours(2));
        ticket.Status.ShouldBe(TicketStatus.Waiting);
        ticket.WaitingNote.ShouldBe("Need the model number");
    }

    [Fact]
    public void Resuming_clears_the_waiting_note()
    {
        var ticket = InStatus(TicketStatus.Waiting);

        ticket.Resume(Agent, T0.AddHours(5));

        ticket.Status.ShouldBe(TicketStatus.Open);
        ticket.WaitingNote.ShouldBeNull();
    }

    [Fact]
    public void Resolving_needs_a_resolution_and_counts_as_the_first_response()
    {
        var ticket = InStatus(TicketStatus.Open);

        ((Action)(() => ticket.Resolve("", Agent, T0.AddHours(3))))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.ReasonRequired);

        ticket.Resolve("Replaced the roller", Agent, T0.AddHours(3));
        ticket.Status.ShouldBe(TicketStatus.Resolved);
        ticket.Resolution.ShouldBe("Replaced the roller");
        ticket.ResolvedAt.ShouldBe(T0.AddHours(3));
        ticket.FirstRespondedAt.ShouldBe(T0.AddHours(3));
    }

    [Fact]
    public void Only_a_resolved_ticket_can_be_confirmed()
    {
        ((Action)(() => InStatus(TicketStatus.Open).Confirm(Requester, T0.AddHours(3))))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.InvalidTransition);

        var resolved = InStatus(TicketStatus.Resolved);
        resolved.Confirm(Requester, T0.AddHours(4));

        resolved.Status.ShouldBe(TicketStatus.Closed);
        resolved.CloseReason.ShouldBe(CloseReason.ConfirmedByRequester);
        resolved.ClosedAt.ShouldBe(T0.AddHours(4));
    }

    [Fact]
    public void Rejecting_a_resolution_sends_the_ticket_back_to_the_agent_with_the_reason()
    {
        var ticket = InStatus(TicketStatus.Resolved);

        ((Action)(() => ticket.Reject(" ", Requester, T0.AddHours(4))))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.ReasonRequired);

        ticket.Reject("Still jamming", Requester, T0.AddHours(4));

        ticket.Status.ShouldBe(TicketStatus.Open);
        ticket.AssigneeId.ShouldBe(Agent);
        ticket.Resolution.ShouldBeNull();
        ticket.ResolvedAt.ShouldBeNull();
        ticket.Events.Last().Note.ShouldBe("Still jamming");
    }

    // ------------------------------------------------------------------ reopening

    [Fact]
    public void A_closed_ticket_can_be_reopened_inside_the_window()
    {
        var ticket = InStatus(TicketStatus.Closed);

        ticket.Reopen("It broke again", Requester, T0.AddHours(4).AddDays(9), windowDays: 10);

        ticket.Status.ShouldBe(TicketStatus.Open);
        ticket.ReopenCount.ShouldBe(1);
        ticket.ClosedAt.ShouldBeNull();
        ticket.CloseReason.ShouldBeNull();
        ticket.AssigneeId.ShouldBe(Agent);
        ticket.Events.Last().Kind.ShouldBe(TicketEventKind.Reopened);
    }

    [Fact]
    public void Reopening_is_refused_after_the_window()
    {
        var ticket = InStatus(TicketStatus.Closed);

        ((Action)(() => ticket.Reopen("It broke again", Requester, T0.AddHours(4).AddDays(11), windowDays: 10)))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.ReopenWindowExpired);
        ticket.Status.ShouldBe(TicketStatus.Closed);
    }

    [Fact]
    public void Reopening_needs_a_reason_and_a_closed_ticket()
    {
        ((Action)(() => InStatus(TicketStatus.Closed).Reopen(" ", Requester, T0.AddDays(1), 10)))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.ReasonRequired);
        ((Action)(() => InStatus(TicketStatus.Cancelled).Reopen("Please", Requester, T0.AddDays(1), 10)))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.InvalidTransition);
    }

    // ------------------------------------------------------------------ cancelling and auto-close

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.Waiting)]
    public void An_unfinished_ticket_can_be_cancelled_with_a_reason(TicketStatus status)
    {
        var ticket = InStatus(status);

        ticket.Cancel("Fixed it myself", Requester, T0.AddHours(6));

        ticket.Status.ShouldBe(TicketStatus.Cancelled);
        ticket.CancelledAt.ShouldBe(T0.AddHours(6));
    }

    [Fact]
    public void Cancelling_needs_a_reason()
    {
        ((Action)(() => New().Cancel("", Requester, T0.AddHours(1))))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.ReasonRequired);
    }

    [Fact]
    public void A_resolved_ticket_nobody_answered_closes_itself_after_the_wait()
    {
        var ticket = InStatus(TicketStatus.Resolved); // resolved at T0 + 3h

        ticket.AutoClose(T0.AddHours(3).AddDays(5).AddMinutes(-1), afterDays: 5).ShouldBeFalse();
        ticket.Status.ShouldBe(TicketStatus.Resolved);

        ticket.AutoClose(T0.AddHours(3).AddDays(5), afterDays: 5).ShouldBeTrue();
        ticket.Status.ShouldBe(TicketStatus.Closed);
        ticket.CloseReason.ShouldBe(CloseReason.AutoClosed);
        ticket.Events.Last().Kind.ShouldBe(TicketEventKind.AutoClosed);
        ticket.Events.Last().ActorId.ShouldBeNull();
    }

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.Waiting)]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public void Auto_close_only_touches_resolved_tickets(TicketStatus status)
    {
        var ticket = InStatus(status);

        ticket.AutoClose(T0.AddYears(1), afterDays: 5).ShouldBeFalse();
        ticket.Status.ShouldBe(status);
    }

    // ------------------------------------------------------------------ comments

    [Fact]
    public void An_agents_first_public_comment_is_the_first_response()
    {
        var ticket = InStatus(TicketStatus.Open);

        ticket.AddComment(Agent, "On it", CommentVisibility.Public, byRequester: false, T0.AddHours(2));

        ticket.FirstRespondedAt.ShouldBe(T0.AddHours(2));
    }

    [Fact]
    public void Internal_notes_and_requester_comments_are_not_a_first_response()
    {
        var ticket = InStatus(TicketStatus.Open);

        ticket.AddComment(Agent, "Check the spooler", CommentVisibility.Internal, byRequester: false, T0.AddHours(2));
        ticket.AddComment(Requester, "Any news?", CommentVisibility.Public, byRequester: true, T0.AddHours(3));

        ticket.FirstRespondedAt.ShouldBeNull();
    }

    [Fact]
    public void The_first_response_time_is_never_overwritten()
    {
        var ticket = InStatus(TicketStatus.Open);
        ticket.AddComment(Agent, "On it", CommentVisibility.Public, byRequester: false, T0.AddHours(2));

        ticket.AddComment(Agent, "Still on it", CommentVisibility.Public, byRequester: false, T0.AddHours(5));

        ticket.FirstRespondedAt.ShouldBe(T0.AddHours(2));
    }

    [Fact]
    public void A_requester_reply_on_a_waiting_ticket_puts_it_back_to_open()
    {
        var ticket = InStatus(TicketStatus.Waiting);

        ticket.AddComment(Requester, "It is a LaserJet 400", CommentVisibility.Public, byRequester: true, T0.AddHours(5));

        ticket.Status.ShouldBe(TicketStatus.Open);
        ticket.WaitingNote.ShouldBeNull();
    }

    [Fact]
    public void An_agent_note_on_a_waiting_ticket_does_not_resume_it()
    {
        var ticket = InStatus(TicketStatus.Waiting);

        ticket.AddComment(Agent, "Reminder sent", CommentVisibility.Public, byRequester: false, T0.AddHours(5));

        ticket.Status.ShouldBe(TicketStatus.Waiting);
    }

    [Theory]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public void Nobody_can_comment_on_a_finished_ticket(TicketStatus status)
    {
        var ticket = InStatus(status);

        ((Action)(() => ticket.AddComment(Requester, "Hello?", CommentVisibility.Public, byRequester: true, T0.AddDays(1))))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.InvalidTransition);
    }

    [Fact]
    public void A_comment_needs_text()
    {
        ((Action)(() => InStatus(TicketStatus.Open).AddComment(Agent, "  ", CommentVisibility.Public, false, T0.AddHours(2))))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.ReasonRequired);
    }

    // ------------------------------------------------------------------ nothing changes when a call is refused

    [Fact]
    public void A_refused_call_leaves_the_ticket_exactly_as_it_was()
    {
        var ticket = InStatus(TicketStatus.Closed);
        var events = ticket.Events.Count;
        var closedAt = ticket.ClosedAt;

        ((Action)(() => ticket.Resolve("Again", Agent, T0.AddDays(1)))).ShouldBeRejected(HelpdeskDomainErrorCodes.InvalidTransition);
        ((Action)(() => ticket.Wait("Why", Agent, T0.AddDays(1)))).ShouldBeRejected(HelpdeskDomainErrorCodes.InvalidTransition);

        ticket.Status.ShouldBe(TicketStatus.Closed);
        ticket.Events.Count.ShouldBe(events);
        ticket.ClosedAt.ShouldBe(closedAt);
        ticket.Resolution.ShouldBe("Replaced the roller");
    }
}

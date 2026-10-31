using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Modularity;
using Volo.Abp.Validation;
using Xunit;

namespace Helpdesk.Tickets;

public abstract class TicketWorkflowTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static async Task ShouldBeRejected(Func<Task> call, string code) =>
        (await Should.ThrowAsync<BusinessException>(call)).Code.ShouldBe(code);

    // ------------------------------------------------------------------ creating

    [Fact]
    public async Task A_requester_opens_a_new_unassigned_ticket_with_a_numbered_code_and_sla_targets()
    {
        var before = Clock.Now;

        var ticket = await CreateAs(Alice);

        Regex.IsMatch(ticket.Code, @"^HD-\d{4}-\d{6}$").ShouldBeTrue();
        ticket.Code.ShouldBe($"HD-{before.Year}-000001");
        ticket.Status.ShouldBe(TicketStatus.New);
        ticket.Priority.ShouldBe(TicketPriority.Normal);
        ticket.Assignee.ShouldBeNull();
        ticket.Requester.Name.ShouldBe("alice");
        ticket.CategoryName.ShouldBe("IT support");
        ticket.FirstResponseDueAt.ShouldBeGreaterThan(ticket.CreationTime);
        ticket.ResolutionDueAt.ShouldBeGreaterThan(ticket.FirstResponseDueAt);
        ticket.Events.Single().Kind.ShouldBe(TicketEventKind.Created);
        ticket.Actions.ShouldContain(TicketAction.Comment);
        ticket.Actions.ShouldContain(TicketAction.Cancel);
    }

    [Fact]
    public async Task Ticket_numbers_count_up_and_are_never_reused()
    {
        var codes = new[]
        {
            (await CreateAs(Alice)).Code,
            (await CreateAs(Bob)).Code,
            (await CreateAs(Alice)).Code,
        };

        codes.Select(code => code[^6..]).ShouldBe(new[] { "000001", "000002", "000003" });
        codes.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task A_disabled_category_cannot_receive_tickets()
    {
        var facilities = CategoryId("Facilities");
        using (As(Admin))
        {
            await Categories.SetActiveAsync(facilities, new Categories.SetCategoryActiveDto { IsActive = false });
        }

        await ShouldBeRejected(() => CreateAs(Alice, category: "Facilities"), HelpdeskDomainErrorCodes.CategoryInactive);
    }

    [Fact]
    public async Task An_unknown_category_is_not_found()
    {
        using (As(Alice))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => Tickets.CreateAsync(new CreateTicketDto
            {
                CategoryId = Guid.NewGuid(),
                Title = "x",
                Description = "y",
            }));
        }
    }

    [Fact]
    public async Task An_empty_title_is_a_validation_error()
    {
        using (As(Alice))
        {
            await Should.ThrowAsync<AbpValidationException>(() => Tickets.CreateAsync(new CreateTicketDto
            {
                CategoryId = CategoryId(),
                Title = "",
                Description = "y",
            }));
        }
    }

    // ------------------------------------------------------------------ the whole life of a ticket

    [Fact]
    public async Task A_ticket_goes_from_new_to_closed_and_the_history_tells_the_story()
    {
        var created = await CreateAs(Alice);

        using (As(Agent))
        {
            var claimed = await Tickets.ClaimAsync(created.Id);
            claimed.Status.ShouldBe(TicketStatus.Open);
            claimed.Assignee!.Name.ShouldBe("agent1");

            await Tickets.AddCommentAsync(created.Id, new CreateCommentDto { Body = "Which printer?" });
            (await Tickets.WaitAsync(created.Id, new TicketReasonDto { Reason = "Need the model" }))
                .Status.ShouldBe(TicketStatus.Waiting);
        }

        using (As(Alice))
        {
            await Tickets.AddCommentAsync(created.Id, new CreateCommentDto { Body = "A LaserJet 400" });
            (await Tickets.GetAsync(created.Id)).Status.ShouldBe(TicketStatus.Open); // her reply resumed it
        }

        using (As(Agent))
        {
            (await Tickets.ResolveAsync(created.Id, new ResolveTicketDto { Resolution = "Replaced the roller" }))
                .Status.ShouldBe(TicketStatus.Resolved);
        }

        TicketDto closed;
        using (As(Alice))
        {
            closed = await Tickets.ConfirmAsync(created.Id);
        }

        closed.Status.ShouldBe(TicketStatus.Closed);
        closed.CloseReason.ShouldBe(CloseReason.ConfirmedByRequester);
        closed.FirstRespondedAt.ShouldNotBeNull();
        closed.Actions.ShouldNotContain(TicketAction.Comment); // nothing more can be said on a closed ticket
        closed.Events.Select(e => e.Kind).ShouldBe(new[]
        {
            TicketEventKind.Created, TicketEventKind.Assigned, TicketEventKind.StatusChanged,
            TicketEventKind.StatusChanged, TicketEventKind.StatusChanged, TicketEventKind.StatusChanged,
        });
        closed.Events.Select(e => e.ToStatus).ShouldBe(new TicketStatus?[]
        {
            TicketStatus.New, TicketStatus.Open, TicketStatus.Waiting, TicketStatus.Open, TicketStatus.Resolved, TicketStatus.Closed,
        });
    }

    [Fact]
    public async Task Rejecting_a_resolution_sends_it_back_to_the_assigned_agent()
    {
        var resolved = await ResolvedTicket();

        using (As(Alice))
        {
            var back = await Tickets.RejectAsync(resolved.Id, new TicketReasonDto { Reason = "Still jamming" });

            back.Status.ShouldBe(TicketStatus.Open);
            back.Assignee!.Name.ShouldBe("agent1");
            back.Resolution.ShouldBeNull();
            back.Events.Last().Note.ShouldBe("Still jamming");
        }
    }

    [Fact]
    public async Task A_closed_ticket_can_be_reopened_inside_the_window_but_not_after()
    {
        var closed = await ClosedTicket();

        using (As(Alice))
        {
            Time.Advance(TimeSpan.FromDays(9));
            var reopened = await Tickets.ReopenAsync(closed.Id, new TicketReasonDto { Reason = "It broke again" });
            reopened.Status.ShouldBe(TicketStatus.Open);
            reopened.ReopenCount.ShouldBe(1);
        }

        // close it again, then let the window run out
        using (As(Agent))
        {
            await Tickets.ResolveAsync(closed.Id, new ResolveTicketDto { Resolution = "Fixed properly" });
        }

        using (As(Alice))
        {
            await Tickets.ConfirmAsync(closed.Id);
            Time.Advance(TimeSpan.FromDays(11));
            await ShouldBeRejected(
                () => Tickets.ReopenAsync(closed.Id, new TicketReasonDto { Reason = "Again" }),
                HelpdeskDomainErrorCodes.ReopenWindowExpired);
        }
    }

    [Fact]
    public async Task A_requester_can_withdraw_an_untouched_ticket_but_not_one_an_agent_has_taken()
    {
        var untouched = await CreateAs(Alice);
        var taken = await OpenTicket("Second printer");

        using (As(Alice))
        {
            (await Tickets.CancelAsync(untouched.Id, new TicketReasonDto { Reason = "Fixed it myself" }))
                .Status.ShouldBe(TicketStatus.Cancelled);

            await ShouldBeRejected(
                () => Tickets.CancelAsync(taken.Id, new TicketReasonDto { Reason = "Never mind" }),
                HelpdeskDomainErrorCodes.InvalidTransition);
        }

        using (As(Agent))
        {
            (await Tickets.CancelAsync(taken.Id, new TicketReasonDto { Reason = "Duplicate" }))
                .Status.ShouldBe(TicketStatus.Cancelled);
        }
    }

    // ------------------------------------------------------------------ who may do what (403) and when (409)

    [Fact]
    public async Task A_requester_cannot_do_an_agents_work()
    {
        var created = await CreateAs(Alice);
        var open = await OpenTicket("Second");

        using (As(Alice))
        {
            await ShouldBeRejected(() => Tickets.ClaimAsync(created.Id), HelpdeskDomainErrorCodes.NotAllowed);
            await ShouldBeRejected(
                () => Tickets.ResolveAsync(open.Id, new ResolveTicketDto { Resolution = "Done" }),
                HelpdeskDomainErrorCodes.NotAllowed);
            await ShouldBeRejected(
                () => Tickets.ChangePriorityAsync(created.Id, new ChangePriorityDto { Priority = TicketPriority.Urgent }),
                HelpdeskDomainErrorCodes.NotAllowed);
        }
    }

    [Fact]
    public async Task An_agent_cannot_confirm_reject_or_reopen_for_the_requester()
    {
        var resolved = await ResolvedTicket();

        using (As(Agent))
        {
            await ShouldBeRejected(() => Tickets.ConfirmAsync(resolved.Id), HelpdeskDomainErrorCodes.NotAllowed);
            await ShouldBeRejected(
                () => Tickets.RejectAsync(resolved.Id, new TicketReasonDto { Reason = "x" }),
                HelpdeskDomainErrorCodes.NotAllowed);
        }
    }

    [Fact]
    public async Task A_ticket_in_the_wrong_status_is_a_conflict_not_a_permission_problem()
    {
        var created = await CreateAs(Alice);

        using (As(Agent))
        {
            await ShouldBeRejected(
                () => Tickets.ResolveAsync(created.Id, new ResolveTicketDto { Resolution = "Done" }),
                HelpdeskDomainErrorCodes.InvalidTransition);
            await ShouldBeRejected(
                () => Tickets.WaitAsync(created.Id, new TicketReasonDto { Reason = "x" }),
                HelpdeskDomainErrorCodes.InvalidTransition);
        }

        using (As(Alice))
        {
            await ShouldBeRejected(() => Tickets.ConfirmAsync(created.Id), HelpdeskDomainErrorCodes.InvalidTransition);
        }
    }

    [Fact]
    public async Task Someone_elses_ticket_does_not_exist_for_you()
    {
        var alices = await CreateAs(Alice);

        using (As(Bob))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => Tickets.GetAsync(alices.Id));
            await Should.ThrowAsync<EntityNotFoundException>(
                () => Tickets.AddCommentAsync(alices.Id, new CreateCommentDto { Body = "Hi" }));
            await Should.ThrowAsync<EntityNotFoundException>(
                () => Tickets.CancelAsync(alices.Id, new TicketReasonDto { Reason = "Hi" }));
        }
    }

    [Fact]
    public async Task Anonymous_callers_are_turned_away()
    {
        await Should.ThrowAsync<Volo.Abp.Authorization.AbpAuthorizationException>(
            () => Tickets.GetAsync(Guid.NewGuid()));
    }

    // ------------------------------------------------------------------ assignment and priority

    [Fact]
    public async Task Tickets_can_only_be_assigned_to_agents()
    {
        var created = await CreateAs(Alice);

        using (As(Agent))
        {
            await ShouldBeRejected(
                () => Tickets.AssignAsync(created.Id, new AssignTicketDto { AssigneeId = UserId(Bob) }),
                HelpdeskDomainErrorCodes.AssigneeNotAgent);

            var assigned = await Tickets.AssignAsync(created.Id, new AssignTicketDto { AssigneeId = UserId(Agent) });
            assigned.Assignee!.Name.ShouldBe("agent1");
            assigned.Status.ShouldBe(TicketStatus.Open);
        }
    }

    [Fact]
    public async Task The_list_of_assignable_agents_has_the_agent_and_the_admin_but_not_requesters()
    {
        using (As(Agent))
        {
            var names = (await Tickets.GetAssignableAgentsAsync()).Items.Select(person => person.Name).ToList();

            names.ShouldContain("agent1");
            names.ShouldContain("admin");
            names.ShouldNotContain("alice");
        }
    }

    [Fact]
    public async Task A_higher_priority_pulls_the_targets_closer()
    {
        var created = await CreateAs(Alice);

        using (As(Agent))
        {
            var urgent = await Tickets.ChangePriorityAsync(created.Id, new ChangePriorityDto { Priority = TicketPriority.Urgent });

            urgent.Priority.ShouldBe(TicketPriority.Urgent);
            urgent.ResolutionDueAt.ShouldBeLessThan(created.ResolutionDueAt);
            urgent.Events.Last().Kind.ShouldBe(TicketEventKind.PriorityChanged);
        }
    }

    // ------------------------------------------------------------------ SLA flags and automatic close

    [Fact]
    public async Task A_ticket_nobody_answered_is_flagged_once_the_first_response_target_passes()
    {
        var created = await CreateAs(Alice);
        created.IsFirstResponseOverdue.ShouldBeFalse();

        Time.Advance(TimeSpan.FromDays(10)); // well past any business-hours target

        var late = await Reload(created.Id);
        late.IsFirstResponseOverdue.ShouldBeTrue();
        late.IsResolutionOverdue.ShouldBeTrue();
    }

    [Fact]
    public async Task An_answered_ticket_is_no_longer_flagged_as_unanswered()
    {
        var open = await OpenTicket();
        using (As(Agent))
        {
            await Tickets.AddCommentAsync(open.Id, new CreateCommentDto { Body = "On it" });
        }

        Time.Advance(TimeSpan.FromDays(10));

        var later = await Reload(open.Id);
        later.IsFirstResponseOverdue.ShouldBeFalse();
        later.IsResolutionOverdue.ShouldBeTrue(); // still unresolved
    }

    [Fact]
    public async Task A_resolved_ticket_nobody_answers_is_closed_automatically_after_the_wait()
    {
        var recent = await ResolvedTicket();
        var manager = GetRequiredService<TicketManager>();

        // 4 days: still inside the 5-day wait
        Time.Advance(TimeSpan.FromDays(4));
        (await WithUnitOfWorkAsync(() => manager.AutoCloseDueAsync())).Count.ShouldBe(0);
        (await Reload(recent.Id)).Status.ShouldBe(TicketStatus.Resolved);

        Time.Advance(TimeSpan.FromDays(2));
        (await WithUnitOfWorkAsync(() => manager.AutoCloseDueAsync())).Count.ShouldBe(1);

        var closed = await Reload(recent.Id);
        closed.Status.ShouldBe(TicketStatus.Closed);
        closed.CloseReason.ShouldBe(CloseReason.AutoClosed);
        closed.Events.Last().Kind.ShouldBe(TicketEventKind.AutoClosed);
        closed.Events.Last().Actor.ShouldBeNull();
    }
}

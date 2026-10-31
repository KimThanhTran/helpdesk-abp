using System;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;
using static Helpdesk.Tickets.TestTickets;

namespace Helpdesk.Tickets;

public class TicketPolicyTests
{
    private static readonly TicketActor Requesting = new(Requester, IsAgent: false);
    private static readonly TicketActor Working = new(Agent, IsAgent: true);
    private static readonly TicketActor Outsider = new(Stranger, IsAgent: false);
    private static readonly DateTime Now = T0.AddHours(5);

    private static TicketAction[] Allowed(Ticket ticket, TicketActor actor) =>
        TicketPolicy.AllowedActions(ticket, actor, Now, reopenWindowDays: 10).OrderBy(action => action).ToArray();

    // ------------------------------------------------------------------ the policy and the aggregate must agree

    /// <summary>Runs the aggregate method that matches an action, as the agent unless the action is the requester's.</summary>
    private static void Apply(Ticket ticket, TicketAction action)
    {
        var when = T0.AddHours(6);
        switch (action)
        {
            case TicketAction.Comment:
                ticket.AddComment(Agent, "text", CommentVisibility.Public, byRequester: false, when);
                break;
            case TicketAction.InternalNote:
                ticket.AddComment(Agent, "text", CommentVisibility.Internal, byRequester: false, when);
                break;
            case TicketAction.Claim:
            case TicketAction.Assign:
                ticket.Assign(OtherAgent, Agent, when);
                break;
            case TicketAction.ChangePriority:
                ticket.ChangePriority(TicketPriority.High, when, when, Agent, when);
                break;
            case TicketAction.Wait:
                ticket.Wait("reason", Agent, when);
                break;
            case TicketAction.Resume:
                ticket.Resume(Agent, when);
                break;
            case TicketAction.Resolve:
                ticket.Resolve("done", Agent, when);
                break;
            case TicketAction.Confirm:
                ticket.Confirm(Requester, when);
                break;
            case TicketAction.Reject:
                ticket.Reject("no", Requester, when);
                break;
            case TicketAction.Reopen:
                ticket.Reopen("again", Requester, when, windowDays: 10);
                break;
            case TicketAction.Cancel:
                ticket.Cancel("reason", Agent, when);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private static bool AggregateAccepts(TicketStatus status, TicketAction action)
    {
        var ticket = InStatus(status);
        try
        {
            Apply(ticket, action);
            return true;
        }
        catch (BusinessException exception) when (exception.Code == HelpdeskDomainErrorCodes.InvalidTransition)
        {
            return false;
        }
    }

    public static TheoryData<TicketStatus, TicketAction> EveryStatusAndAction()
    {
        var data = new TheoryData<TicketStatus, TicketAction>();
        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            foreach (var action in Enum.GetValues<TicketAction>())
            {
                data.Add(status, action);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryStatusAndAction))]
    public void The_policy_never_offers_what_the_aggregate_would_refuse(TicketStatus status, TicketAction action)
    {
        var ticket = InStatus(status);

        if (TicketPolicy.IsStatusAllowed(action, ticket, Working))
        {
            AggregateAccepts(status, action).ShouldBeTrue(
                $"the policy offers {action} on a {status} ticket but the aggregate refuses it");
        }
    }

    [Theory]
    [MemberData(nameof(EveryStatusAndAction))]
    public void The_aggregate_accepts_nothing_the_policy_hides_except_the_documented_cases(
        TicketStatus status, TicketAction action)
    {
        var ticket = InStatus(status);

        // Claim is "take a ticket nobody has": reassigning an open ticket is Assign, so the policy is stricter on purpose.
        if (action == TicketAction.Claim)
        {
            return;
        }

        if (AggregateAccepts(status, action))
        {
            TicketPolicy.IsStatusAllowed(action, ticket, Working).ShouldBeTrue(
                $"the aggregate accepts {action} on a {status} ticket but the policy would hide the button");
        }
    }

    // ------------------------------------------------------------------ what each person sees

    [Fact]
    public void The_requester_of_a_resolved_ticket_can_confirm_reject_or_comment()
    {
        Allowed(InStatus(TicketStatus.Resolved), Requesting).ShouldBe(
            new[] { TicketAction.Comment, TicketAction.Confirm, TicketAction.Reject });
    }

    [Fact]
    public void The_requester_of_a_new_ticket_can_comment_or_withdraw_it()
    {
        Allowed(New(), Requesting).ShouldBe(new[] { TicketAction.Comment, TicketAction.Cancel });
    }

    [Fact]
    public void The_requester_cannot_withdraw_a_ticket_an_agent_is_already_working()
    {
        Allowed(InStatus(TicketStatus.Open), Requesting).ShouldBe(new[] { TicketAction.Comment });
    }

    [Fact]
    public void An_agent_can_work_a_new_ticket_but_never_confirms_it()
    {
        var allowed = Allowed(New(), Working);

        allowed.ShouldBe(new[]
        {
            TicketAction.Comment, TicketAction.InternalNote, TicketAction.Claim, TicketAction.Assign,
            TicketAction.ChangePriority, TicketAction.Cancel,
        });
        allowed.ShouldNotContain(TicketAction.Confirm);
    }

    [Fact]
    public void An_open_ticket_offers_the_agent_wait_and_resolve_but_not_resume()
    {
        var allowed = Allowed(InStatus(TicketStatus.Open), Working);

        allowed.ShouldContain(TicketAction.Wait);
        allowed.ShouldContain(TicketAction.Resolve);
        allowed.ShouldNotContain(TicketAction.Resume);
        allowed.ShouldNotContain(TicketAction.Claim);
    }

    [Fact]
    public void A_finished_ticket_offers_agents_nothing()
    {
        Allowed(InStatus(TicketStatus.Closed), Working).ShouldBeEmpty();
        Allowed(InStatus(TicketStatus.Cancelled), Working).ShouldBeEmpty();
    }

    [Fact]
    public void The_requester_can_reopen_a_closed_ticket_only_inside_the_window()
    {
        var closed = InStatus(TicketStatus.Closed); // closed at T0 + 4h

        TicketPolicy.AllowedActions(closed, Requesting, T0.AddHours(4).AddDays(10), 10).ShouldContain(TicketAction.Reopen);
        TicketPolicy.AllowedActions(closed, Requesting, T0.AddHours(4).AddDays(10).AddMinutes(1), 10)
            .ShouldNotContain(TicketAction.Reopen);
    }

    [Fact]
    public void Someone_who_is_neither_requester_nor_agent_can_do_nothing()
    {
        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            Allowed(InStatus(status), Outsider).ShouldBeEmpty();
        }
    }

    [Fact]
    public void A_person_who_is_both_requester_and_agent_gets_both_sets_of_actions()
    {
        var both = new TicketActor(Requester, IsAgent: true);

        var allowed = Allowed(InStatus(TicketStatus.Resolved), both);

        allowed.ShouldContain(TicketAction.Confirm);   // as requester
        allowed.ShouldContain(TicketAction.InternalNote); // as agent
    }

    // ------------------------------------------------------------------ errors: 403 for the role, 409 for the status

    [Fact]
    public void Ensure_reports_the_wrong_role_before_the_wrong_status()
    {
        // an agent confirming a New ticket is wrong on both counts: the role is what gets reported
        ((Action)(() => TicketPolicy.Ensure(TicketAction.Confirm, New(), Working, Now, 10)))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.NotAllowed);
    }

    [Fact]
    public void Ensure_reports_the_wrong_status_for_the_right_role()
    {
        ((Action)(() => TicketPolicy.Ensure(TicketAction.Resolve, New(), Working, Now, 10)))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.InvalidTransition);
    }

    [Fact]
    public void Ensure_reports_an_expired_reopen_window_with_its_own_error()
    {
        var closed = InStatus(TicketStatus.Closed);

        ((Action)(() => TicketPolicy.Ensure(TicketAction.Reopen, closed, Requesting, T0.AddDays(30), 10)))
            .ShouldBeRejected(HelpdeskDomainErrorCodes.ReopenWindowExpired);
    }

    [Fact]
    public void Ensure_lets_an_allowed_action_through()
    {
        Should.NotThrow(() => TicketPolicy.Ensure(TicketAction.Claim, New(), Working, Now, 10));
        Should.NotThrow(() => TicketPolicy.Ensure(TicketAction.Cancel, New(), Requesting, Now, 10));
    }
}

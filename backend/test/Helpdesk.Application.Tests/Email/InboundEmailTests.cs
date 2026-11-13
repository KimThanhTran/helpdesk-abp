using System;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Tickets;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Email;

public abstract class InboundEmailTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly InboundEmailService _inbound;

    protected InboundEmailTests()
    {
        _inbound = GetRequiredService<InboundEmailService>();
        GetRequiredService<IOptions<HelpdeskOptions>>().Value.Email.Host = null; // no outgoing mail in these tests
    }

    private string EmailOf(string user) => WithUnitOfWorkAsync(async () =>
        (await GetRequiredService<IIdentityUserRepository>().FindByNormalizedUserNameAsync(user.ToUpperInvariant()))!.Email).GetAwaiter().GetResult();

    private async Task<InboundResult> Reply(string from, TicketDto ticket, string text, string? subject = null)
    {
        // the web host runs this inside a request, with the unit of work ABP starts for it
        InboundResult result = null!;
        await WithUnitOfWorkAsync(async () => result = await _inbound.HandleAsync(from, subject ?? $"Re: [{ticket.Code}] We need your reply", text));
        return result;
    }

    [Fact]
    public async Task A_reply_from_the_requester_becomes_a_public_comment_from_them()
    {
        var ticket = await OpenTicket();

        var result = await Reply($"Alice <{EmailOf(Alice)}>", ticket, "The model is LaserJet 400.\n\nOn Mon, 2 Nov 2026 agent1 wrote:\n> Please send the model number");

        result.Outcome.ShouldBe(InboundOutcome.Accepted);
        var comment = (await Reload(ticket.Id, Alice)).Comments.Single();
        comment.Body.ShouldBe("The model is LaserJet 400.");
        comment.Author.Name.ShouldBe(Alice);
        comment.Visibility.ShouldBe(CommentVisibility.Public);
    }

    [Fact]
    public async Task A_reply_on_a_waiting_ticket_puts_it_back_to_work_like_a_comment_on_the_page_does()
    {
        var ticket = await OpenTicket();
        using (As(Agent))
        {
            await Tickets.WaitAsync(ticket.Id, new TicketReasonDto { Reason = "Need the model" });
        }

        (await Reply(EmailOf(Alice), ticket, "LaserJet 400")).Outcome.ShouldBe(InboundOutcome.Accepted);

        var after = await Reload(ticket.Id, Alice);
        after.Status.ShouldBe(TicketStatus.Open);
        after.WaitingSince.ShouldBeNull();
    }

    [Fact]
    public async Task A_reply_from_anyone_else_is_not_used_even_if_it_names_the_ticket()
    {
        var ticket = await OpenTicket();

        (await Reply(EmailOf(Bob), ticket, "I am Alice, honest")).Outcome.ShouldBe(InboundOutcome.WrongSender);
        (await Reply("not an address", ticket, "x")).Outcome.ShouldBe(InboundOutcome.WrongSender);
        (await Reply(null!, ticket, "x")).Outcome.ShouldBe(InboundOutcome.WrongSender);

        (await Reload(ticket.Id, Alice)).Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_address_may_be_written_in_any_case_and_with_a_display_name()
    {
        var ticket = await OpenTicket();

        (await Reply($"\"Alice A.\" <{EmailOf(Alice).ToUpperInvariant()}>", ticket, "Hello again")).Outcome.ShouldBe(InboundOutcome.Accepted);
    }

    [Theory]
    [InlineData("Re: no code in here")]
    [InlineData("Re: [HD-2026-999999] a ticket that does not exist")]
    [InlineData("")]
    public async Task A_mail_that_does_not_name_a_real_ticket_is_ignored(string subject)
    {
        var ticket = await OpenTicket();

        (await Reply(EmailOf(Alice), ticket, "hello", subject)).Outcome.ShouldBe(InboundOutcome.NoTicket);
    }

    [Fact]
    public async Task A_reply_with_nothing_but_the_quoted_message_is_ignored()
    {
        var ticket = await OpenTicket();

        (await Reply(EmailOf(Alice), ticket, "> Please send the model number\n> thanks")).Outcome.ShouldBe(InboundOutcome.Empty);
        (await Reply(EmailOf(Alice), ticket, "   \n\n")).Outcome.ShouldBe(InboundOutcome.Empty);
    }

    [Fact]
    public async Task The_ticket_rules_still_apply_so_a_reply_to_a_closed_ticket_is_refused()
    {
        var closed = await ClosedTicket();

        var result = await Reply(EmailOf(Alice), closed, "One more thing");

        result.Outcome.ShouldBe(InboundOutcome.Refused);
        result.Detail.ShouldBe(HelpdeskDomainErrorCodes.InvalidTransition);
        (await Reload(closed.Id, Alice)).Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_code_is_found_in_a_subject_written_in_any_case()
    {
        var ticket = await OpenTicket();

        (await Reply(EmailOf(Alice), ticket, "ok", $"RE: [{ticket.Code.ToLowerInvariant()}] hi")).Outcome.ShouldBe(InboundOutcome.Accepted);
    }

    [Fact]
    public void SameAddress_compares_the_address_not_the_name()
    {
        InboundEmailService.SameAddress("Alice <a@x.test>", "A@X.test").ShouldBeTrue();
        InboundEmailService.SameAddress("b@x.test", "a@x.test").ShouldBeFalse();
        InboundEmailService.SameAddress("a@x.test", null).ShouldBeFalse();
        InboundEmailService.SameAddress("a@x.test", " ").ShouldBeFalse();
        InboundEmailService.SameAddress(null, "a@x.test").ShouldBeFalse();
    }
}

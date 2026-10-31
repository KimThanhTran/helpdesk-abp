using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Tickets;

public abstract class TicketCommentTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string Secret = "INTERNAL-ONLY-MARKER-7f3a";

    private async Task<TicketDto> WithInternalNote()
    {
        var open = await OpenTicket();
        using (As(Agent))
        {
            await Tickets.AddCommentAsync(open.Id, new CreateCommentDto
            {
                Body = $"Customer is on the old driver. {Secret}",
                Visibility = CommentVisibility.Internal,
            });
            await Tickets.AddCommentAsync(open.Id, new CreateCommentDto { Body = "I am looking at it." });
        }

        return open;
    }

    [Fact]
    public async Task The_requester_never_sees_internal_notes_on_the_ticket()
    {
        var ticket = await WithInternalNote();

        var seenByRequester = await Reload(ticket.Id, Alice);

        seenByRequester.Comments.Select(comment => comment.Body).ShouldBe(new[] { "I am looking at it." });
        seenByRequester.Comments.ShouldAllBe(comment => comment.Visibility == CommentVisibility.Public);
    }

    [Fact]
    public async Task Nothing_the_requester_receives_contains_the_internal_text_anywhere()
    {
        var ticket = await WithInternalNote();

        // check the whole serialised response, so a leak through any field (events, notes, titles) would show up
        var json = JsonSerializer.Serialize(await Reload(ticket.Id, Alice));
        json.ShouldNotContain(Secret);

        using (As(Alice))
        {
            var list = await Tickets.GetMyListAsync(new GetTicketListInput());
            JsonSerializer.Serialize(list).ShouldNotContain(Secret);

            // and the comment the requester adds comes back without the agent's notes
            var comment = await Tickets.AddCommentAsync(ticket.Id, new CreateCommentDto { Body = "Thanks" });
            JsonSerializer.Serialize(comment).ShouldNotContain(Secret);
        }
    }

    [Fact]
    public async Task Agents_see_internal_notes_marked_as_internal()
    {
        var ticket = await WithInternalNote();

        var seenByAgent = await Reload(ticket.Id, Agent);

        seenByAgent.Comments.Count.ShouldBe(2);
        seenByAgent.Comments.Single(comment => comment.Visibility == CommentVisibility.Internal)
            .Body.ShouldContain(Secret);
    }

    [Fact]
    public async Task A_requester_cannot_write_an_internal_note()
    {
        var created = await CreateAs(Alice);

        using (As(Alice))
        {
            var rejected = await Should.ThrowAsync<BusinessException>(() => Tickets.AddCommentAsync(
                created.Id, new CreateCommentDto { Body = "secret", Visibility = CommentVisibility.Internal }));
            rejected.Code.ShouldBe(HelpdeskDomainErrorCodes.NotAllowed);
        }
    }

    [Fact]
    public async Task The_requester_is_not_offered_the_internal_note_action()
    {
        var created = await CreateAs(Alice);

        (await Reload(created.Id, Alice)).Actions.ShouldNotContain(TicketAction.InternalNote);
        (await Reload(created.Id, Agent)).Actions.ShouldContain(TicketAction.InternalNote);
    }

    [Fact]
    public async Task Comments_come_back_in_the_order_they_were_written_with_their_authors()
    {
        var open = await OpenTicket();
        using (As(Agent))
        {
            await Tickets.AddCommentAsync(open.Id, new CreateCommentDto { Body = "First" });
        }

        Time.Advance(TimeSpan.FromMinutes(5));
        using (As(Alice))
        {
            await Tickets.AddCommentAsync(open.Id, new CreateCommentDto { Body = "Second" });
        }

        var comments = (await Reload(open.Id, Alice)).Comments;
        comments.Select(comment => comment.Body).ShouldBe(new[] { "First", "Second" });
        comments.Select(comment => comment.Author.Name).ShouldBe(new[] { "agent1", "alice" });
    }

    [Fact]
    public async Task An_agents_public_comment_is_the_first_response_but_an_internal_note_is_not()
    {
        var open = await OpenTicket();

        using (As(Agent))
        {
            await Tickets.AddCommentAsync(
                open.Id, new CreateCommentDto { Body = "note", Visibility = CommentVisibility.Internal });
        }

        (await Reload(open.Id)).FirstRespondedAt.ShouldBeNull();

        using (As(Agent))
        {
            await Tickets.AddCommentAsync(open.Id, new CreateCommentDto { Body = "Hello Alice" });
        }

        (await Reload(open.Id)).FirstRespondedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_closed_ticket_accepts_no_more_comments()
    {
        var closed = await ClosedTicket();

        using (As(Alice))
        {
            var rejected = await Should.ThrowAsync<BusinessException>(
                () => Tickets.AddCommentAsync(closed.Id, new CreateCommentDto { Body = "One more thing" }));
            rejected.Code.ShouldBe(HelpdeskDomainErrorCodes.InvalidTransition);
        }
    }

    [Fact]
    public async Task A_comment_must_not_be_empty()
    {
        var open = await OpenTicket();

        using (As(Agent))
        {
            await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(
                () => Tickets.AddCommentAsync(open.Id, new CreateCommentDto { Body = "" }));
        }
    }
}

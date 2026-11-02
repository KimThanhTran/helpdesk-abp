using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Helpdesk.Tickets;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Triage;

public abstract class TicketTriageTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private ITicketTriageAppService Triage => GetRequiredService<ITicketTriageAppService>();

    private async Task<TicketDto> Past(string title, string description, string category, string? resolution = null,
        bool close = false)
    {
        TicketDto ticket;
        using (As(Alice))
        {
            ticket = await Tickets.CreateAsync(new CreateTicketDto
            {
                CategoryId = CategoryId(category), Title = title, Description = description,
            });
        }

        using (As(Agent))
        {
            await Tickets.ClaimAsync(ticket.Id);
            if (resolution is not null)
            {
                await Tickets.ResolveAsync(ticket.Id, new ResolveTicketDto { Resolution = resolution });
            }
        }

        if (close)
        {
            using (As(Alice))
            {
                await Tickets.ConfirmAsync(ticket.Id);
            }
        }

        return ticket;
    }

    private async Task Learn()
    {
        await Past("VPN disconnects every few minutes", "The VPN drops when I work from home", "IT support",
            "Updated the VPN client and disabled IPv6.", close: true);
        await Past("Printer jams on the second page", "Paper error on the floor two printer", "IT support",
            "Replaced the pickup roller.", close: true);
        await Past("Air conditioner leaks water in room 204", "Water drips onto the desks", "Facilities",
            "Cleared the blocked drain.", close: true);
        await Past("Light tubes out in the corridor", "The corridor on floor 3 is dark", "Facilities",
            "Replaced the tubes.", close: true);
        await Past("Allowance missing from my payslip", "The transport allowance was not paid", "HR & payroll",
            "Paid it as a supplementary payment.", close: true);
    }

    private Task<TriageSuggestionDto> Ask(string user, string title, string description = "", Guid? exclude = null)
    {
        using (As(user))
        {
            return Triage.GetSuggestionAsync(new GetTriageInput { Title = title, Description = description, ExcludeTicketId = exclude });
        }
    }

    // ------------------------------------------------------------------ categories (everyone)

    [Fact]
    public async Task A_new_ticket_is_matched_to_the_category_of_the_tickets_it_resembles()
    {
        await Learn();

        var suggestion = await Ask(Bob, "My VPN keeps disconnecting", "It drops every few minutes");

        suggestion.Categories.First().Name.ShouldBe("IT support");
        suggestion.Categories.First().Confidence.ShouldBeGreaterThan(0.5);
    }

    [Theory]
    [InlineData("Water is leaking from the air conditioner", "Facilities")]
    [InlineData("The printer shows a paper jam", "IT support")]
    [InlineData("My payslip is missing the allowance", "HR & payroll")]
    public async Task Different_problems_get_different_categories(string title, string expected)
    {
        await Learn();

        (await Ask(Bob, title)).Categories.First().Name.ShouldBe(expected);
    }

    [Fact]
    public async Task With_no_history_there_is_nothing_to_suggest()
    {
        var suggestion = await Ask(Bob, "My VPN keeps disconnecting");

        suggestion.Categories.ShouldBeEmpty();
        suggestion.SimilarTickets.ShouldBeEmpty();
        suggestion.SuggestedReply.ShouldBeNull();
    }

    [Fact]
    public async Task Text_unlike_anything_seen_before_suggests_nothing()
    {
        await Learn();

        (await Ask(Bob, "Quarterly budget forecast spreadsheet")).Categories.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_request_is_answered_with_nothing_instead_of_an_error()
    {
        await Learn();

        var suggestion = await Ask(Bob, "", "");

        suggestion.Categories.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_disabled_category_is_never_suggested()
    {
        await Learn();
        var facilities = CategoryId("Facilities");
        using (As(Admin))
        {
            await Categories.SetActiveAsync(facilities, new Categories.SetCategoryActiveDto { IsActive = false });
        }

        var suggestion = await Ask(Bob, "Water is leaking from the air conditioner");

        suggestion.Categories.ShouldNotContain(category => category.Name == "Facilities");
    }

    // ------------------------------------------------------------------ other people's tickets (agents only)

    [Fact]
    public async Task A_requester_gets_categories_but_never_other_peoples_tickets_or_answers()
    {
        await Learn();

        var suggestion = await Ask(Bob, "My VPN keeps disconnecting", "It drops every few minutes");

        suggestion.Categories.ShouldNotBeEmpty();
        suggestion.SimilarTickets.ShouldBeEmpty();
        suggestion.SuggestedReply.ShouldBeNull();
        suggestion.PossibleDuplicate.ShouldBeFalse();

        // nothing about the other tickets, not even a trace of their text, is in the answer
        var json = JsonSerializer.Serialize(suggestion);
        json.ShouldNotContain("IPv6");
        json.ShouldNotContain("HD-");
    }

    [Fact]
    public async Task An_agent_gets_similar_tickets_and_the_answer_given_last_time()
    {
        await Learn();

        var suggestion = await Ask(Agent, "VPN keeps disconnecting", "It drops every few minutes at home");

        suggestion.SimilarTickets.ShouldNotBeEmpty();
        suggestion.SimilarTickets.First().Title.ShouldContain("VPN");
        suggestion.SuggestedReply.ShouldNotBeNull();
        suggestion.SuggestedReply!.Text.ShouldContain("VPN client");
        suggestion.SuggestedReply.FromTicketCode.ShouldStartWith("HD-");
    }

    [Fact]
    public async Task Similar_tickets_do_not_carry_their_description_or_comments()
    {
        await Learn();

        var json = JsonSerializer.Serialize((await Ask(Agent, "VPN keeps disconnecting")).SimilarTickets);

        json.ShouldNotContain("work from home"); // the description of the past ticket
    }

    [Fact]
    public async Task An_unresolved_ticket_is_not_offered_as_an_answer_but_a_near_copy_is_flagged_as_a_duplicate()
    {
        await Past("Badge reader at the entrance does not work", "The badge reader stays red and the door stays locked",
            "Facilities"); // taken but not resolved

        var suggestion = await Ask(Agent, "Badge reader at the entrance does not work",
            "The badge reader stays red and the door stays locked");

        suggestion.PossibleDuplicate.ShouldBeTrue();
        suggestion.SimilarTickets.Single().Status.ShouldBe(TicketStatus.Open);
        suggestion.SuggestedReply.ShouldBeNull();
    }

    [Fact]
    public async Task A_ticket_is_never_matched_with_itself()
    {
        var past = await Past("VPN disconnects every few minutes", "The VPN drops", "IT support", "Updated the client.", close: true);

        var suggestion = await Ask(Agent, past.Title, "The VPN drops", exclude: past.Id);

        suggestion.SimilarTickets.ShouldNotContain(similar => similar.Id == past.Id);
        suggestion.PossibleDuplicate.ShouldBeFalse();
    }

    [Fact]
    public async Task A_cancelled_ticket_is_not_used_to_answer()
    {
        var cancelled = await CreateAs(Alice, "Fax machine on floor 4 is broken", category: "Facilities");
        using (As(Alice))
        {
            await Tickets.CancelAsync(cancelled.Id, new TicketReasonDto { Reason = "Found another one" });
        }

        (await Ask(Agent, "Fax machine on floor 4 is broken")).SuggestedReply.ShouldBeNull();
    }

    // ------------------------------------------------------------------ access

    [Fact]
    public async Task Only_people_who_can_open_tickets_may_ask()
    {
        await Should.ThrowAsync<Volo.Abp.Authorization.AbpAuthorizationException>(
            () => Triage.GetSuggestionAsync(new GetTriageInput { Title = "x" }));
    }

    [Fact]
    public async Task Overlong_text_is_a_validation_error()
    {
        using (As(Bob))
        {
            await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(() => Triage.GetSuggestionAsync(
                new GetTriageInput { Title = new string('x', TicketConsts.MaxTitleLength + 1) }));
        }
    }
}

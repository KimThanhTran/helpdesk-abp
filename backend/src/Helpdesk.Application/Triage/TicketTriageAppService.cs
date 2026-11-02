using System;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Categories;
using Helpdesk.Permissions;
using Helpdesk.Tickets;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Helpdesk.Triage;

/// <summary>
/// Learns from the tickets that already exist: no external service, no model to train. The index is built
/// from the 2,000 most recent tickets on every call.
/// </summary>
[Authorize(HelpdeskPermissions.Tickets.Create)]
public class TicketTriageAppService : HelpdeskAppService, ITicketTriageAppService
{
    // shortcut: rebuilt per request, fine up to a few thousand tickets; cache it and refresh on change when that hurts
    private const int CorpusSize = 2000;
    private const int Neighbours = 7;
    private const double NeighbourFloor = 0.05;
    private const double SimilarFloor = 0.25;
    private const double ReplyFloor = 0.30;
    private const double DuplicateFloor = 0.60;

    private readonly IRepository<Ticket, Guid> _tickets;
    private readonly IRepository<Category, Guid> _categories;

    public TicketTriageAppService(IRepository<Ticket, Guid> tickets, IRepository<Category, Guid> categories)
    {
        _tickets = tickets;
        _categories = categories;
    }

    public async Task<TriageSuggestionDto> GetSuggestionAsync(GetTriageInput input)
    {
        var suggestion = new TriageSuggestionDto();
        var query = Text(input.Title, input.Description);
        if (string.IsNullOrWhiteSpace(query))
        {
            return suggestion;
        }

        var corpus = await AsyncExecuter.ToListAsync(
            (await _tickets.GetQueryableAsync())
                .OrderByDescending(ticket => ticket.CreationTime)
                .Take(CorpusSize)
                .Select(ticket => new
                {
                    ticket.Id, ticket.Code, ticket.Title, ticket.Description, ticket.CategoryId, ticket.Status, ticket.Resolution,
                }));
        if (corpus.Count == 0)
        {
            return suggestion;
        }

        var index = new TfIdfIndex(corpus.Select(ticket => (ticket.Id, Text(ticket.Title, ticket.Description))));
        var byId = corpus.ToDictionary(ticket => ticket.Id);
        var matches = index.Search(query, take: 10, exclude: input.ExcludeTicketId);

        suggestion.Categories = await SuggestCategoriesAsync(matches.Take(Neighbours)
            .Where(match => match.Score >= NeighbourFloor)
            .Select(match => (byId[match.Id].CategoryId, match.Score)));

        // Other people's tickets and the answers written for them are for agents only.
        if (!await AuthorizationService.IsGrantedAsync(HelpdeskPermissions.Tickets.Work))
        {
            return suggestion;
        }

        suggestion.SimilarTickets = matches
            .Where(match => match.Score >= SimilarFloor)
            .Take(3)
            .Select(match => new SimilarTicketDto
            {
                Id = match.Id,
                Code = byId[match.Id].Code,
                Title = byId[match.Id].Title,
                Status = byId[match.Id].Status,
                Similarity = Math.Round(match.Score, 2),
            })
            .ToList();

        suggestion.PossibleDuplicate = matches.Any(match =>
            match.Score >= DuplicateFloor
            && byId[match.Id].Status is TicketStatus.New or TicketStatus.Open or TicketStatus.Waiting);

        var answered = matches.FirstOrDefault(match =>
            match.Score >= ReplyFloor
            && byId[match.Id].Status is TicketStatus.Resolved or TicketStatus.Closed
            && !string.IsNullOrWhiteSpace(byId[match.Id].Resolution));
        if (answered != default)
        {
            suggestion.SuggestedReply = new SuggestedReplyDto
            {
                FromTicketCode = byId[answered.Id].Code,
                Text = byId[answered.Id].Resolution!,
                Similarity = Math.Round(answered.Score, 2),
            };
        }

        return suggestion;
    }

    /// <summary>Each close ticket votes for its category with its similarity; only enabled categories are offered.</summary>
    private async Task<System.Collections.Generic.List<CategorySuggestionDto>> SuggestCategoriesAsync(
        System.Collections.Generic.IEnumerable<(Guid CategoryId, double Score)> votes)
    {
        var active = (await _categories.GetListAsync(category => category.IsActive))
            .ToDictionary(category => category.Id, category => category.Name);

        var counted = votes.Where(vote => active.ContainsKey(vote.CategoryId)).ToList();
        var total = counted.Sum(vote => vote.Score);
        if (total <= 0)
        {
            return new();
        }

        return counted
            .GroupBy(vote => vote.CategoryId)
            .Select(group => new CategorySuggestionDto
            {
                CategoryId = group.Key,
                Name = active[group.Key],
                Confidence = Math.Round(group.Sum(vote => vote.Score) / total, 2),
            })
            .OrderByDescending(suggestion => suggestion.Confidence)
            .ThenBy(suggestion => suggestion.Name)
            .Take(3)
            .ToList();
    }

    /// <summary>The title counts double: it is the shortest and most telling part of a ticket.</summary>
    private static string Text(string? title, string? description) => $"{title} {title} {description}";
}

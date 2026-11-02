using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Helpdesk.Tickets;

namespace Helpdesk.Triage;

public class GetTriageInput
{
    [StringLength(TicketConsts.MaxTitleLength)]
    public string? Title { get; set; }

    [StringLength(TicketConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>For an existing ticket: leave it out so it is not matched with itself. Only agents get similar tickets.</summary>
    public Guid? ExcludeTicketId { get; set; }
}

public class CategorySuggestionDto
{
    public Guid CategoryId { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Share of the closest past tickets that were in this category, from 0 to 1.</summary>
    public double Confidence { get; set; }
}

public class SimilarTicketDto
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Title { get; set; } = null!;

    public TicketStatus Status { get; set; }

    /// <summary>Text similarity from 0 to 1.</summary>
    public double Similarity { get; set; }
}

public class SuggestedReplyDto
{
    /// <summary>The ticket the text was taken from.</summary>
    public string FromTicketCode { get; set; } = null!;

    /// <summary>How it was resolved last time. A starting point for the agent, never sent by itself.</summary>
    public string Text { get; set; } = null!;

    public double Similarity { get; set; }
}

public class TriageSuggestionDto
{
    /// <summary>Best first. Empty when there is not enough history to say.</summary>
    public List<CategorySuggestionDto> Categories { get; set; } = new();

    /// <summary>Agents only: past tickets that look alike.</summary>
    public List<SimilarTicketDto> SimilarTickets { get; set; } = new();

    /// <summary>Agents only: an unresolved ticket that looks almost the same.</summary>
    public bool PossibleDuplicate { get; set; }

    /// <summary>Agents only: how the most similar resolved ticket was resolved.</summary>
    public SuggestedReplyDto? SuggestedReply { get; set; }
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace Helpdesk.Tickets;

public class PersonDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;
}

/// <summary>A row in a ticket list. Light on purpose: no description and no conversation.</summary>
public class TicketListItemDto : EntityDto<Guid>
{
    public string Code { get; set; } = null!;

    public string Title { get; set; } = null!;

    public TicketStatus Status { get; set; }

    public TicketPriority Priority { get; set; }

    public Guid CategoryId { get; set; }

    public string CategoryName { get; set; } = null!;

    public PersonDto Requester { get; set; } = null!;

    public PersonDto? Assignee { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime FirstResponseDueAt { get; set; }

    public DateTime ResolutionDueAt { get; set; }

    /// <summary>No agent has answered yet and the first-response target has passed.</summary>
    public bool IsFirstResponseOverdue { get; set; }

    /// <summary>Still being worked on and the resolution target has passed.</summary>
    public bool IsResolutionOverdue { get; set; }
}

/// <summary>A whole ticket: the list fields plus the text, the conversation and the history.</summary>
public class TicketDto : TicketListItemDto
{
    public string Description { get; set; } = null!;

    public string? WaitingNote { get; set; }

    public string? Resolution { get; set; }

    public DateTime? FirstRespondedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public CloseReason? CloseReason { get; set; }

    public DateTime? CancelledAt { get; set; }

    public int ReopenCount { get; set; }

    /// <summary>Set while the ticket waits for the requester. The SLA clock is stopped until then.</summary>
    public DateTime? WaitingSince { get; set; }

    /// <summary>Working hours already spent waiting. They have been added to both SLA targets.</summary>
    public double PausedBusinessHours { get; set; }

    /// <summary>What the person asking can do right now. Clients should show exactly these buttons.</summary>
    public List<TicketAction> Actions { get; set; } = new();

    /// <summary>Internal notes are included only for agents.</summary>
    public List<TicketCommentDto> Comments { get; set; } = new();

    public List<TicketEventDto> Events { get; set; } = new();
}

public class TicketCommentDto : EntityDto<Guid>
{
    public PersonDto Author { get; set; } = null!;

    public string Body { get; set; } = null!;

    public CommentVisibility Visibility { get; set; }

    public DateTime CreationTime { get; set; }
}

public class TicketEventDto : EntityDto<Guid>
{
    public TicketEventKind Kind { get; set; }

    /// <summary>Null when the system acted, for example the automatic close.</summary>
    public PersonDto? Actor { get; set; }

    public DateTime CreationTime { get; set; }

    public TicketStatus? FromStatus { get; set; }

    public TicketStatus? ToStatus { get; set; }

    public PersonDto? Assignee { get; set; }

    public string? Note { get; set; }
}

public class CreateTicketDto
{
    public Guid CategoryId { get; set; }

    [Required]
    [StringLength(TicketConsts.MaxTitleLength)]
    public string Title { get; set; } = null!;

    [Required]
    [StringLength(TicketConsts.MaxDescriptionLength)]
    public string Description { get; set; } = null!;
}

public enum QueueScope
{
    /// <summary>New tickets nobody has taken.</summary>
    Unassigned = 1,

    /// <summary>Tickets assigned to me that are still active.</summary>
    Mine = 2,

    /// <summary>Every ticket that matches the other filters.</summary>
    All = 3,
}

public class GetTicketListInput : PagedAndSortedResultRequestDto
{
    /// <summary>Matches the code or the title, ignoring case.</summary>
    public string? Filter { get; set; }

    public TicketStatus? Status { get; set; }

    public TicketPriority? Priority { get; set; }

    public Guid? CategoryId { get; set; }

    /// <summary>Only used by the agent queue.</summary>
    public QueueScope Scope { get; set; } = QueueScope.Unassigned;
}

public class AssignTicketDto
{
    public Guid AssigneeId { get; set; }
}

public class ChangePriorityDto
{
    public TicketPriority Priority { get; set; }
}

/// <summary>Used where an action needs a written reason: wait, reject, reopen, cancel.</summary>
public class TicketReasonDto
{
    [Required]
    [StringLength(TicketConsts.MaxReasonLength)]
    public string Reason { get; set; } = null!;
}

public class ResolveTicketDto
{
    [Required]
    [StringLength(TicketConsts.MaxCommentLength)]
    public string Resolution { get; set; } = null!;
}

public class CreateCommentDto
{
    [Required]
    [StringLength(TicketConsts.MaxCommentLength)]
    public string Body { get; set; } = null!;

    public CommentVisibility Visibility { get; set; } = CommentVisibility.Public;
}

using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.Guids;

namespace Helpdesk.Tickets;

/// <summary>
/// A support request. Every change goes through a method here, so the status rules live in one place:
/// a call that is not allowed in the current status throws and leaves the ticket untouched.
/// The history (events) and the conversation (comments) are append-only.
/// </summary>
public class Ticket : FullAuditedAggregateRoot<Guid>
{
    public string Code { get; private set; } = null!;

    public Guid CategoryId { get; private set; }

    public Guid RequesterId { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public string Title { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public TicketPriority Priority { get; private set; }

    public TicketStatus Status { get; private set; }

    /// <summary>Why the ticket is waiting for the requester.</summary>
    public string? WaitingNote { get; private set; }

    public string? Resolution { get; private set; }

    public DateTime FirstResponseDueAt { get; private set; }

    public DateTime ResolutionDueAt { get; private set; }

    public DateTime? FirstRespondedAt { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public DateTime? ClosedAt { get; private set; }

    public CloseReason? CloseReason { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public int ReopenCount { get; private set; }

    public ICollection<TicketComment> Comments { get; private set; } = new List<TicketComment>();

    public ICollection<TicketEvent> Events { get; private set; } = new List<TicketEvent>();

    protected Ticket()
    {
    }

    internal Ticket(
        Guid id,
        string code,
        Guid categoryId,
        Guid requesterId,
        string title,
        string description,
        TicketPriority priority,
        DateTime firstResponseDueAt,
        DateTime resolutionDueAt,
        DateTime now)
        : base(id)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), TicketConsts.MaxCodeLength);
        CategoryId = categoryId;
        RequesterId = requesterId;
        Title = Check.NotNullOrWhiteSpace(title.Trim(), nameof(title), TicketConsts.MaxTitleLength);
        Description = Check.NotNullOrWhiteSpace(description.Trim(), nameof(description), TicketConsts.MaxDescriptionLength);
        Priority = priority;
        Status = TicketStatus.New;
        FirstResponseDueAt = firstResponseDueAt;
        ResolutionDueAt = resolutionDueAt;

        Log(TicketEventKind.Created, requesterId, now, to: TicketStatus.New);
    }

    /// <summary>Gives the ticket to an agent. A new ticket becomes open; an open or waiting one keeps its status.</summary>
    public void Assign(Guid assigneeId, Guid actorId, DateTime now)
    {
        EnsureStatus(TicketAction.Assign, TicketStatus.New, TicketStatus.Open, TicketStatus.Waiting);

        if (AssigneeId == assigneeId && Status != TicketStatus.New)
        {
            return; // assigning to the current holder changes nothing, so nothing is recorded
        }

        var from = Status;
        AssigneeId = assigneeId;
        if (Status == TicketStatus.New)
        {
            Status = TicketStatus.Open;
        }

        Log(TicketEventKind.Assigned, actorId, now, from, Status, assignee: assigneeId);
    }

    /// <summary>Changes the priority and the targets that follow from it.</summary>
    public void ChangePriority(
        TicketPriority priority, DateTime firstResponseDueAt, DateTime resolutionDueAt, Guid actorId, DateTime now)
    {
        EnsureStatus(TicketAction.ChangePriority, TicketStatus.New, TicketStatus.Open, TicketStatus.Waiting);

        if (priority == Priority)
        {
            return;
        }

        var previous = Priority;
        Priority = priority;
        if (FirstRespondedAt is null)
        {
            FirstResponseDueAt = firstResponseDueAt;
        }

        ResolutionDueAt = resolutionDueAt;
        Log(TicketEventKind.PriorityChanged, actorId, now, note: $"{previous} -> {priority}");
    }

    public void Wait(string note, Guid actorId, DateTime now)
    {
        EnsureStatus(TicketAction.Wait, TicketStatus.Open);
        WaitingNote = RequireText(note, TicketConsts.MaxReasonLength);
        Transition(TicketStatus.Waiting, actorId, now, WaitingNote);
    }

    public void Resume(Guid actorId, DateTime now)
    {
        EnsureStatus(TicketAction.Resume, TicketStatus.Waiting);
        WaitingNote = null;
        Transition(TicketStatus.Open, actorId, now);
    }

    public void Resolve(string resolution, Guid actorId, DateTime now)
    {
        EnsureStatus(TicketAction.Resolve, TicketStatus.Open);
        Resolution = RequireText(resolution, TicketConsts.MaxCommentLength);
        ResolvedAt = now;
        FirstRespondedAt ??= now;
        Transition(TicketStatus.Resolved, actorId, now);
    }

    /// <summary>The requester accepts the resolution.</summary>
    public void Confirm(Guid actorId, DateTime now)
    {
        EnsureStatus(TicketAction.Confirm, TicketStatus.Resolved);
        Close(Tickets.CloseReason.ConfirmedByRequester, actorId, now);
    }

    /// <summary>The requester says the problem is not solved: the ticket goes back to the agent.</summary>
    public void Reject(string reason, Guid actorId, DateTime now)
    {
        EnsureStatus(TicketAction.Reject, TicketStatus.Resolved);
        var text = RequireText(reason, TicketConsts.MaxReasonLength);
        Resolution = null;
        ResolvedAt = null;
        Transition(TicketStatus.Open, actorId, now, text);
    }

    /// <summary>Brings a closed ticket back to open within <paramref name="windowDays"/> of closing.</summary>
    public void Reopen(string reason, Guid actorId, DateTime now, int windowDays)
    {
        EnsureStatus(TicketAction.Reopen, TicketStatus.Closed);
        var text = RequireText(reason, TicketConsts.MaxReasonLength);

        if (ClosedAt is null || now > ClosedAt.Value.AddDays(windowDays))
        {
            throw HelpdeskErrors.ReopenWindowExpired(windowDays);
        }

        ReopenCount++;
        ClosedAt = null;
        CloseReason = null;
        Resolution = null;
        ResolvedAt = null;
        var from = Status;
        Status = TicketStatus.Open;
        Log(TicketEventKind.Reopened, actorId, now, from, Status, note: text);
    }

    public void Cancel(string reason, Guid actorId, DateTime now)
    {
        EnsureStatus(TicketAction.Cancel, TicketStatus.New, TicketStatus.Open, TicketStatus.Waiting);
        var text = RequireText(reason, TicketConsts.MaxReasonLength);
        CancelledAt = now;
        Transition(TicketStatus.Cancelled, actorId, now, text);
    }

    /// <summary>
    /// Closes a resolved ticket nobody answered for <paramref name="afterDays"/> days.
    /// Returns false (and changes nothing) when it is not due yet.
    /// </summary>
    public bool AutoClose(DateTime now, int afterDays)
    {
        if (Status != TicketStatus.Resolved || ResolvedAt is null || now < ResolvedAt.Value.AddDays(afterDays))
        {
            return false;
        }

        Close(Tickets.CloseReason.AutoClosed, null, now);
        return true;
    }

    /// <summary>
    /// Adds a message. A requester's message on a waiting ticket puts it back to open,
    /// and an agent's first public message counts as the first response.
    /// </summary>
    public TicketComment AddComment(
        Guid authorId, string body, CommentVisibility visibility, bool byRequester, DateTime now)
    {
        EnsureStatus(
            visibility == CommentVisibility.Internal ? TicketAction.InternalNote : TicketAction.Comment,
            TicketStatus.New, TicketStatus.Open, TicketStatus.Waiting, TicketStatus.Resolved);

        var comment = new TicketComment(
            SimpleGuidGenerator.Instance.Create(), Id, authorId, RequireText(body, TicketConsts.MaxCommentLength), visibility, now);
        Comments.Add(comment);

        if (!byRequester && visibility == CommentVisibility.Public)
        {
            FirstRespondedAt ??= now;
        }

        if (byRequester && Status == TicketStatus.Waiting)
        {
            WaitingNote = null;
            Transition(TicketStatus.Open, authorId, now, "Requester replied");
        }

        return comment;
    }

    private void Close(CloseReason reason, Guid? actorId, DateTime now)
    {
        ClosedAt = now;
        CloseReason = reason;
        var from = Status;
        Status = TicketStatus.Closed;
        Log(reason == Tickets.CloseReason.AutoClosed ? TicketEventKind.AutoClosed : TicketEventKind.StatusChanged,
            actorId, now, from, Status);
    }

    private void Transition(TicketStatus to, Guid actorId, DateTime now, string? note = null)
    {
        var from = Status;
        Status = to;
        Log(TicketEventKind.StatusChanged, actorId, now, from, to, note: note);
    }

    private void EnsureStatus(TicketAction action, params TicketStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw HelpdeskErrors.InvalidTransition(Status, action);
        }
    }

    private static string RequireText(string? text, int maxLength)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw HelpdeskErrors.ReasonRequired();
        }

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private void Log(
        TicketEventKind kind,
        Guid? actorId,
        DateTime now,
        TicketStatus? from = null,
        TicketStatus? to = null,
        string? note = null,
        Guid? assignee = null)
    {
        Events.Add(new TicketEvent(SimpleGuidGenerator.Instance.Create(), Id, kind, actorId, now, from, to, assignee, note));
    }
}

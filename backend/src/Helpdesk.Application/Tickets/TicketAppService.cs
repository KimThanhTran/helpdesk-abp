using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Categories;
using Helpdesk.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Local;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace Helpdesk.Tickets;

/// <summary>
/// Thin on purpose: load the ticket, ask <see cref="TicketPolicy"/> whether this person may do this,
/// call one method on the aggregate, save, publish the change, return the new state.
/// </summary>
[Authorize(HelpdeskPermissions.Tickets.Default)]
public class TicketAppService : HelpdeskAppService, ITicketAppService
{
    private readonly IRepository<Ticket, Guid> _tickets;
    private readonly IRepository<Category, Guid> _categories;
    private readonly IIdentityUserRepository _users;
    private readonly IdentityUserManager _userManager;
    private readonly TicketManager _manager;
    private readonly ILocalEventBus _eventBus;

    public TicketAppService(
        IRepository<Ticket, Guid> tickets,
        IRepository<Category, Guid> categories,
        IIdentityUserRepository users,
        IdentityUserManager userManager,
        TicketManager manager,
        ILocalEventBus eventBus)
    {
        _tickets = tickets;
        _categories = categories;
        _users = users;
        _userManager = userManager;
        _manager = manager;
        _eventBus = eventBus;
    }

    // ---------------------------------------------------------------- create and read

    [Authorize(HelpdeskPermissions.Tickets.Create)]
    public async Task<TicketDto> CreateAsync(CreateTicketDto input)
    {
        var actor = await GetActorAsync();
        var ticket = await _manager.CreateAsync(
            actor.UserId, input.CategoryId, input.Title, input.Description, TicketPriority.Normal);

        await _tickets.InsertAsync(ticket, autoSave: true);
        await _eventBus.PublishAsync(new TicketChangedEventData(ticket.Id, TicketEventKind.Created, actor.UserId));
        return await ToDtoAsync(ticket, actor);
    }

    public async Task<TicketDto> GetAsync(Guid id)
    {
        var actor = await GetActorAsync();
        return await ToDtoAsync(await LoadAsync(id, actor), actor);
    }

    public async Task<PagedResultDto<TicketListItemDto>> GetMyListAsync(GetTicketListInput input)
    {
        var userId = CurrentUser.GetId();
        var query = ApplyFilters(await _tickets.GetQueryableAsync(), input)
            .Where(ticket => ticket.RequesterId == userId);
        return await ToPagedAsync(query, input, queueOrder: false);
    }

    [Authorize(HelpdeskPermissions.Tickets.Work)]
    public async Task<PagedResultDto<TicketListItemDto>> GetQueueAsync(GetTicketListInput input)
    {
        var userId = CurrentUser.GetId();
        var query = ApplyFilters(await _tickets.GetQueryableAsync(), input);

        query = input.Scope switch
        {
            QueueScope.Unassigned => query.Where(ticket => ticket.Status == TicketStatus.New),
            QueueScope.Mine => query.Where(ticket => ticket.AssigneeId == userId
                && ticket.Status != TicketStatus.Closed && ticket.Status != TicketStatus.Cancelled),
            _ => query,
        };

        return await ToPagedAsync(query, input, queueOrder: true);
    }

    [Authorize(HelpdeskPermissions.Tickets.Work)]
    public async Task<ListResultDto<PersonDto>> GetAssignableAgentsAsync()
    {
        var agents = await _users.GetListByNormalizedRoleNameAsync(HelpdeskPermissions.Roles.Agent.ToUpperInvariant());
        var admins = await _users.GetListByNormalizedRoleNameAsync("ADMIN");

        var people = agents.Concat(admins)
            .DistinctBy(user => user.Id)
            .OrderBy(user => DisplayName(user))
            .Select(user => new PersonDto { Id = user.Id, Name = DisplayName(user) })
            .ToList();
        return new ListResultDto<PersonDto>(people);
    }

    // ---------------------------------------------------------------- actions

    public Task<TicketDto> ClaimAsync(Guid id) =>
        ActAsync(id, TicketAction.Claim, TicketEventKind.Assigned,
            (ticket, actor, now) => ticket.Assign(actor.UserId, actor.UserId, now));

    public async Task<TicketDto> AssignAsync(Guid id, AssignTicketDto input)
    {
        if (!await IsAgentAsync(input.AssigneeId))
        {
            throw HelpdeskErrors.AssigneeNotAgent();
        }

        return await ActAsync(id, TicketAction.Assign, TicketEventKind.Assigned,
            (ticket, actor, now) => ticket.Assign(input.AssigneeId, actor.UserId, now));
    }

    public Task<TicketDto> ChangePriorityAsync(Guid id, ChangePriorityDto input) =>
        ActAsync(id, TicketAction.ChangePriority, TicketEventKind.PriorityChanged,
            (ticket, actor, _) => _manager.ChangePriority(ticket, input.Priority, actor.UserId));

    public Task<TicketDto> WaitAsync(Guid id, TicketReasonDto input) =>
        ActAsync(id, TicketAction.Wait, TicketEventKind.StatusChanged,
            (ticket, actor, now) => ticket.Wait(input.Reason, actor.UserId, now));

    public Task<TicketDto> ResumeAsync(Guid id) =>
        ActAsync(id, TicketAction.Resume, TicketEventKind.StatusChanged,
            (ticket, actor, _) => _manager.Resume(ticket, actor.UserId));

    public Task<TicketDto> ResolveAsync(Guid id, ResolveTicketDto input) =>
        ActAsync(id, TicketAction.Resolve, TicketEventKind.StatusChanged,
            (ticket, actor, now) => ticket.Resolve(input.Resolution, actor.UserId, now));

    public Task<TicketDto> ConfirmAsync(Guid id) =>
        ActAsync(id, TicketAction.Confirm, TicketEventKind.StatusChanged,
            (ticket, actor, now) => ticket.Confirm(actor.UserId, now));

    public Task<TicketDto> RejectAsync(Guid id, TicketReasonDto input) =>
        ActAsync(id, TicketAction.Reject, TicketEventKind.StatusChanged,
            (ticket, actor, now) => ticket.Reject(input.Reason, actor.UserId, now));

    public Task<TicketDto> ReopenAsync(Guid id, TicketReasonDto input) =>
        ActAsync(id, TicketAction.Reopen, TicketEventKind.Reopened,
            (ticket, actor, now) => ticket.Reopen(input.Reason, actor.UserId, now, _manager.ReopenWindowDays));

    public Task<TicketDto> CancelAsync(Guid id, TicketReasonDto input) =>
        ActAsync(id, TicketAction.Cancel, TicketEventKind.StatusChanged,
            (ticket, actor, now) => ticket.Cancel(input.Reason, actor.UserId, now));

    public async Task<TicketCommentDto> AddCommentAsync(Guid id, CreateCommentDto input)
    {
        var actor = await GetActorAsync();
        var ticket = await LoadAsync(id, actor);
        var action = input.Visibility == CommentVisibility.Internal ? TicketAction.InternalNote : TicketAction.Comment;
        TicketPolicy.Ensure(action, ticket, actor, Clock.Now, _manager.ReopenWindowDays);

        var statusBefore = ticket.Status;
        var respondedBefore = ticket.FirstRespondedAt;
        var comment = _manager.AddComment(
            ticket, actor.UserId, input.Body, input.Visibility, byRequester: ticket.RequesterId == actor.UserId);

        await _tickets.UpdateAsync(ticket, autoSave: true);

        // most comments change nothing a report cares about; tell listeners only when something did
        if (ticket.Status != statusBefore || ticket.FirstRespondedAt != respondedBefore)
        {
            await _eventBus.PublishAsync(new TicketChangedEventData(ticket.Id, TicketEventKind.StatusChanged, actor.UserId));
        }

        var names = await NamesAsync(new[] { comment.AuthorId });
        return ToCommentDto(comment, names);
    }

    private async Task<TicketDto> ActAsync(
        Guid id, TicketAction action, TicketEventKind kind, Action<Ticket, TicketActor, DateTime> change)
    {
        var actor = await GetActorAsync();
        var ticket = await LoadAsync(id, actor);
        var now = Clock.Now;

        TicketPolicy.Ensure(action, ticket, actor, now, _manager.ReopenWindowDays);
        change(ticket, actor, now);

        await _tickets.UpdateAsync(ticket, autoSave: true);
        await _eventBus.PublishAsync(new TicketChangedEventData(ticket.Id, kind, actor.UserId));
        return await ToDtoAsync(ticket, actor);
    }

    // ---------------------------------------------------------------- who and what

    private async Task<TicketActor> GetActorAsync() =>
        new(CurrentUser.GetId(), await AuthorizationService.IsGrantedAsync(HelpdeskPermissions.Tickets.Work));

    /// <summary>Loads a ticket the actor may see. Someone else's ticket is reported as not found, so ids cannot be probed.</summary>
    private async Task<Ticket> LoadAsync(Guid id, TicketActor actor)
    {
        var ticket = await _tickets.FindAsync(candidate => candidate.Id == id, includeDetails: true);
        if (ticket is null || (ticket.RequesterId != actor.UserId && !actor.IsAgent))
        {
            throw new EntityNotFoundException(typeof(Ticket), id);
        }

        return ticket;
    }

    private async Task<bool> IsAgentAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is not null
            && (await _userManager.IsInRoleAsync(user, HelpdeskPermissions.Roles.Agent)
                || await _userManager.IsInRoleAsync(user, "admin"));
    }

    // ---------------------------------------------------------------- lists

    private static IQueryable<Ticket> ApplyFilters(IQueryable<Ticket> query, GetTicketListInput input)
    {
        var text = input.Filter?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(text))
        {
            query = query.Where(ticket => ticket.Code.ToLower().Contains(text) || ticket.Title.ToLower().Contains(text));
        }

        if (input.Status is { } status)
        {
            query = query.Where(ticket => ticket.Status == status);
        }

        if (input.Priority is { } priority)
        {
            query = query.Where(ticket => ticket.Priority == priority);
        }

        if (input.CategoryId is { } categoryId)
        {
            query = query.Where(ticket => ticket.CategoryId == categoryId);
        }

        return query;
    }

    /// <summary>Only whitelisted columns can be sorted on, so the caller never controls the query text.</summary>
    private static IQueryable<Ticket> ApplySorting(IQueryable<Ticket> query, string? sorting, bool queueOrder)
    {
        var parts = sorting?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

        return parts.FirstOrDefault()?.ToLowerInvariant() switch
        {
            "code" => descending ? query.OrderByDescending(t => t.Code) : query.OrderBy(t => t.Code),
            "title" => descending ? query.OrderByDescending(t => t.Title) : query.OrderBy(t => t.Title),
            "status" => descending ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            "priority" => descending ? query.OrderByDescending(t => t.Priority) : query.OrderBy(t => t.Priority),
            "resolutionduedat" => descending
                ? query.OrderByDescending(t => t.ResolutionDueAt)
                : query.OrderBy(t => t.ResolutionDueAt),
            "creationtime" => descending
                ? query.OrderByDescending(t => t.CreationTime)
                : query.OrderBy(t => t.CreationTime),
            // default: the queue shows urgent and oldest first, "my tickets" shows newest first
            _ => queueOrder
                ? query.OrderByDescending(t => t.Priority).ThenBy(t => t.CreationTime)
                : query.OrderByDescending(t => t.CreationTime),
        };
    }

    private async Task<PagedResultDto<TicketListItemDto>> ToPagedAsync(
        IQueryable<Ticket> query, GetTicketListInput input, bool queueOrder)
    {
        var total = await AsyncExecuter.CountAsync(query);
        var page = await AsyncExecuter.ToListAsync(
            ApplySorting(query, input.Sorting, queueOrder).Skip(input.SkipCount).Take(input.MaxResultCount));

        var names = await NamesAsync(page.SelectMany(ticket => new[] { ticket.RequesterId, ticket.AssigneeId ?? Guid.Empty }));
        var categories = await CategoryNamesAsync(page.Select(ticket => ticket.CategoryId));
        var now = Clock.Now;

        return new PagedResultDto<TicketListItemDto>(
            total, page.Select(ticket => ToListItem(new TicketListItemDto(), ticket, names, categories, now)).ToList());
    }

    // ---------------------------------------------------------------- mapping

    private async Task<TicketDto> ToDtoAsync(Ticket ticket, TicketActor actor)
    {
        var ids = new HashSet<Guid> { ticket.RequesterId };
        if (ticket.AssigneeId is { } assignee)
        {
            ids.Add(assignee);
        }

        ids.UnionWith(ticket.Comments.Select(comment => comment.AuthorId));
        ids.UnionWith(ticket.Events.Where(e => e.ActorId is not null).Select(e => e.ActorId!.Value));
        ids.UnionWith(ticket.Events.Where(e => e.AssigneeId is not null).Select(e => e.AssigneeId!.Value));

        var names = await NamesAsync(ids);
        var categories = await CategoryNamesAsync(new[] { ticket.CategoryId });
        var now = Clock.Now;

        var dto = ToListItem(new TicketDto(), ticket, names, categories, now);
        dto.Description = ticket.Description;
        dto.WaitingNote = ticket.WaitingNote;
        dto.Resolution = ticket.Resolution;
        dto.FirstRespondedAt = ticket.FirstRespondedAt;
        dto.ResolvedAt = ticket.ResolvedAt;
        dto.ClosedAt = ticket.ClosedAt;
        dto.CloseReason = ticket.CloseReason;
        dto.CancelledAt = ticket.CancelledAt;
        dto.ReopenCount = ticket.ReopenCount;
        dto.WaitingSince = ticket.WaitingSince;
        dto.PausedBusinessHours = Math.Round(ticket.PausedBusinessHours, 2);
        dto.Actions = TicketPolicy.AllowedActions(ticket, actor, now, _manager.ReopenWindowDays).ToList();

        // Internal notes are agents-only: they are removed here, before anything is serialised,
        // so no endpoint that returns this DTO can leak them to a requester.
        dto.Comments = ticket.Comments
            .Where(comment => actor.IsAgent || comment.Visibility == CommentVisibility.Public)
            .OrderBy(comment => comment.CreationTime)
            .Select(comment => ToCommentDto(comment, names))
            .ToList();

        dto.Events = ticket.Events
            .OrderBy(e => e.CreationTime)
            .Select(e => new TicketEventDto
            {
                Id = e.Id,
                Kind = e.Kind,
                Actor = e.ActorId is { } actorId ? Person(actorId, names) : null,
                CreationTime = e.CreationTime,
                FromStatus = e.FromStatus,
                ToStatus = e.ToStatus,
                Assignee = e.AssigneeId is { } assigneeId ? Person(assigneeId, names) : null,
                Note = e.Note,
            })
            .ToList();

        return dto;
    }

    private static T ToListItem<T>(
        T dto, Ticket ticket, IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<Guid, string> categories, DateTime now)
        where T : TicketListItemDto
    {
        var open = ticket.Status is TicketStatus.New or TicketStatus.Open;

        dto.Id = ticket.Id;
        dto.Code = ticket.Code;
        dto.Title = ticket.Title;
        dto.Status = ticket.Status;
        dto.Priority = ticket.Priority;
        dto.CategoryId = ticket.CategoryId;
        dto.CategoryName = categories.GetValueOrDefault(ticket.CategoryId, "Unknown category");
        dto.Requester = Person(ticket.RequesterId, names);
        dto.Assignee = ticket.AssigneeId is { } assignee ? Person(assignee, names) : null;
        dto.CreationTime = ticket.CreationTime;
        dto.FirstResponseDueAt = ticket.FirstResponseDueAt;
        dto.ResolutionDueAt = ticket.ResolutionDueAt;
        dto.IsFirstResponseOverdue = open && ticket.FirstRespondedAt is null && now > ticket.FirstResponseDueAt;
        dto.IsResolutionOverdue = open && now > ticket.ResolutionDueAt;
        return dto;
    }

    private static TicketCommentDto ToCommentDto(TicketComment comment, IReadOnlyDictionary<Guid, string> names) => new()
    {
        Id = comment.Id,
        Author = Person(comment.AuthorId, names),
        Body = comment.Body,
        Visibility = comment.Visibility,
        CreationTime = comment.CreationTime,
    };

    private static PersonDto Person(Guid id, IReadOnlyDictionary<Guid, string> names) =>
        new() { Id = id, Name = names.GetValueOrDefault(id, "Unknown user") };

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids)
    {
        var wanted = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var users = await _users.GetListByIdsAsync(wanted);
        return users.ToDictionary(user => user.Id, DisplayName);
    }

    private async Task<Dictionary<Guid, string>> CategoryNamesAsync(IEnumerable<Guid> ids)
    {
        var wanted = ids.Distinct().ToList();
        var categories = await _categories.GetListAsync(category => wanted.Contains(category.Id));
        return categories.ToDictionary(category => category.Id, category => category.Name);
    }

    private static string DisplayName(IdentityUser user)
    {
        var full = $"{user.Name} {user.Surname}".Trim();
        return full.Length > 0 ? full : user.UserName;
    }
}

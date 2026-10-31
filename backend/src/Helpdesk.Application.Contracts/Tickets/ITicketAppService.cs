using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Helpdesk.Tickets;

/// <summary>
/// Method names follow the ABP auto-API convention (Get..., Create..., Add... map to GET and POST),
/// and the rest become POST /api/app/ticket/{id}/{action}.
/// Errors: 403 when the role cannot do it, 404 when the ticket is not yours to see, 409 when its status forbids it.
/// </summary>
public interface ITicketAppService : IApplicationService
{
    Task<TicketDto> CreateAsync(CreateTicketDto input);

    Task<TicketDto> GetAsync(Guid id);

    /// <summary>Tickets I opened.</summary>
    Task<PagedResultDto<TicketListItemDto>> GetMyListAsync(GetTicketListInput input);

    /// <summary>Agents only: the work queue.</summary>
    Task<PagedResultDto<TicketListItemDto>> GetQueueAsync(GetTicketListInput input);

    /// <summary>Agents only: the people a ticket can be assigned to.</summary>
    Task<ListResultDto<PersonDto>> GetAssignableAgentsAsync();

    /// <summary>Agent takes an unassigned ticket for themselves.</summary>
    Task<TicketDto> ClaimAsync(Guid id);

    Task<TicketDto> AssignAsync(Guid id, AssignTicketDto input);

    Task<TicketDto> ChangePriorityAsync(Guid id, ChangePriorityDto input);

    /// <summary>Pauses the ticket until the requester answers.</summary>
    Task<TicketDto> WaitAsync(Guid id, TicketReasonDto input);

    Task<TicketDto> ResumeAsync(Guid id);

    Task<TicketDto> ResolveAsync(Guid id, ResolveTicketDto input);

    /// <summary>The requester accepts the resolution and the ticket closes.</summary>
    Task<TicketDto> ConfirmAsync(Guid id);

    /// <summary>The requester says it is not solved: back to the agent.</summary>
    Task<TicketDto> RejectAsync(Guid id, TicketReasonDto input);

    /// <summary>The requester brings a closed ticket back, within the reopen window.</summary>
    Task<TicketDto> ReopenAsync(Guid id, TicketReasonDto input);

    Task<TicketDto> CancelAsync(Guid id, TicketReasonDto input);

    Task<TicketCommentDto> AddCommentAsync(Guid id, CreateCommentDto input);
}

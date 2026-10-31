using Helpdesk.Tickets;
using Volo.Abp;

namespace Helpdesk;

/// <summary>The only place that builds business exceptions, so codes and data stay consistent.</summary>
public static class HelpdeskErrors
{
    public static BusinessException NotAllowed(TicketAction action) =>
        new BusinessException(HelpdeskDomainErrorCodes.NotAllowed).WithData("Action", action);

    public static BusinessException InvalidTransition(TicketStatus status, TicketAction action) =>
        new BusinessException(HelpdeskDomainErrorCodes.InvalidTransition)
            .WithData("Status", status)
            .WithData("Action", action);

    public static BusinessException ReasonRequired() => new(HelpdeskDomainErrorCodes.ReasonRequired);

    public static BusinessException ReopenWindowExpired(int days) =>
        new BusinessException(HelpdeskDomainErrorCodes.ReopenWindowExpired).WithData("Days", days);

    public static BusinessException CategoryInactive() => new(HelpdeskDomainErrorCodes.CategoryInactive);

    public static BusinessException AssigneeNotAgent() => new(HelpdeskDomainErrorCodes.AssigneeNotAgent);

    public static BusinessException CategoryDuplicateName(string name) =>
        new BusinessException(HelpdeskDomainErrorCodes.CategoryDuplicateName).WithData("Name", name);

    public static BusinessException CategoryInUse() => new(HelpdeskDomainErrorCodes.CategoryInUse);
}

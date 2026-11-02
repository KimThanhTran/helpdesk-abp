namespace Helpdesk;

public static class HelpdeskDomainErrorCodes
{
    /// <summary>The caller's role may not do this (HTTP 403).</summary>
    public const string NotAllowed = "Helpdesk:NotAllowed";

    /// <summary>The ticket is in a state that does not allow this (HTTP 409).</summary>
    public const string InvalidTransition = "Helpdesk:Ticket:InvalidTransition";

    public const string ReasonRequired = "Helpdesk:Ticket:ReasonRequired";
    public const string ReopenWindowExpired = "Helpdesk:Ticket:ReopenWindowExpired";
    public const string CategoryInactive = "Helpdesk:Ticket:CategoryInactive";
    public const string AssigneeNotAgent = "Helpdesk:Ticket:AssigneeNotAgent";

    public const string AttachmentEmpty = "Helpdesk:Attachment:Empty";
    public const string AttachmentTooLarge = "Helpdesk:Attachment:TooLarge";
    public const string AttachmentBadType = "Helpdesk:Attachment:BadType";
    public const string AttachmentLimit = "Helpdesk:Attachment:Limit";

    public const string CategoryDuplicateName = "Helpdesk:Category:DuplicateName";
    public const string CategoryInUse = "Helpdesk:Category:InUse";
}

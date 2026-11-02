namespace Helpdesk.Tickets;

public static class TicketConsts
{
    public const string CodePrefix = "HD";
    public const int MaxCodeLength = 20;
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 4000;
    public const int MaxCommentLength = 4000;
    public const int MaxReasonLength = 1000;
    public const int MaxCategoryNameLength = 64;
    public const int MaxCategoryDescriptionLength = 500;
    public const int MaxAttachmentBytes = 5 * 1024 * 1024;
    public const int MaxAttachmentsPerTicket = 5;
    public const int MaxFileNameLength = 120;
}

namespace Helpdesk.Tickets;

public enum CommentVisibility
{
    /// <summary>Requester and agents can read it.</summary>
    Public = 1,

    /// <summary>Agents only. Must never reach the requester through any API.</summary>
    Internal = 2,
}

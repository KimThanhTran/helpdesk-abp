namespace Helpdesk.Permissions;

/// <summary>
/// Permission names. Who may do something is decided by these; which tickets a person may see
/// is decided separately by the ticket itself (requester) and the Work permission (agent).
/// </summary>
public static class HelpdeskPermissions
{
    public const string GroupName = "Helpdesk";

    public static class Tickets
    {
        /// <summary>See and comment on your own tickets.</summary>
        public const string Default = GroupName + ".Tickets";

        public const string Create = Default + ".Create";

        /// <summary>Agent: see the queue, take tickets and work them.</summary>
        public const string Work = Default + ".Work";
    }

    public static class Categories
    {
        public const string Default = GroupName + ".Categories";

        public const string Manage = Default + ".Manage";
    }

    public static class Reports
    {
        public const string Default = GroupName + ".Reports";
    }

    public static class Roles
    {
        public const string Requester = "requester";

        public const string Agent = "agent";
    }
}

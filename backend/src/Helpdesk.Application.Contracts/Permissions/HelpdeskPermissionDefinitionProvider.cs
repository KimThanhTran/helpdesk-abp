using Helpdesk.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Helpdesk.Permissions;

public class HelpdeskPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(HelpdeskPermissions.GroupName, L("Helpdesk"));

        var tickets = group.AddPermission(HelpdeskPermissions.Tickets.Default, L("Permission:Tickets"));
        tickets.AddChild(HelpdeskPermissions.Tickets.Create, L("Permission:Tickets.Create"));
        tickets.AddChild(HelpdeskPermissions.Tickets.Work, L("Permission:Tickets.Work"));

        var categories = group.AddPermission(HelpdeskPermissions.Categories.Default, L("Permission:Categories"));
        categories.AddChild(HelpdeskPermissions.Categories.Manage, L("Permission:Categories.Manage"));

        group.AddPermission(HelpdeskPermissions.Reports.Default, L("Permission:Reports"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<HelpdeskResource>(name);
    }
}

using System.Threading.Tasks;
using Helpdesk.Permissions;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Security;

/// <summary>The roles the seed creates must carry exactly the permissions the design gives them.</summary>
public abstract class RolePermissionTests<TStartupModule> : HelpdeskApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private async Task<bool> Granted(string user, string permission)
    {
        using (As(user))
        {
            return await GetRequiredService<IAuthorizationService>()
                .IsGrantedAsync(permission);
        }
    }

    [Theory]
    [InlineData(Alice, HelpdeskPermissions.Tickets.Default, true)]
    [InlineData(Alice, HelpdeskPermissions.Tickets.Create, true)]
    [InlineData(Alice, HelpdeskPermissions.Categories.Default, true)]
    [InlineData(Alice, HelpdeskPermissions.Tickets.Work, false)]
    [InlineData(Alice, HelpdeskPermissions.Categories.Manage, false)]
    [InlineData(Alice, HelpdeskPermissions.Reports.Default, false)]
    [InlineData(Agent, HelpdeskPermissions.Tickets.Default, true)]
    [InlineData(Agent, HelpdeskPermissions.Tickets.Create, true)]
    [InlineData(Agent, HelpdeskPermissions.Tickets.Work, true)]
    [InlineData(Agent, HelpdeskPermissions.Reports.Default, true)]
    [InlineData(Agent, HelpdeskPermissions.Categories.Manage, false)]
    [InlineData(Admin, HelpdeskPermissions.Tickets.Work, true)]
    [InlineData(Admin, HelpdeskPermissions.Categories.Manage, true)]
    [InlineData(Admin, HelpdeskPermissions.Reports.Default, true)]
    public async Task Each_role_has_exactly_its_permissions(string user, string permission, bool expected)
    {
        (await Granted(user, permission)).ShouldBe(expected);
    }

    [Fact]
    public async Task New_users_get_the_requester_role_by_default()
    {
        var roles = await WithUnitOfWorkAsync(async () =>
        {
            var repository = GetRequiredService<Volo.Abp.Identity.IIdentityRoleRepository>();
            return await repository.GetDefaultOnesAsync();
        });

        roles.ShouldContain(role => role.Name == HelpdeskPermissions.Roles.Requester);
        roles.ShouldNotContain(role => role.Name == HelpdeskPermissions.Roles.Agent);
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Categories;
using Helpdesk.Permissions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.PermissionManagement;

namespace Helpdesk.Data;

/// <summary>
/// Always creates the two working roles. With Helpdesk:SeedDemoData it also creates three demo people
/// and a few categories so a fresh database is usable straight away. Demo accounts are for local use only.
/// </summary>
public class HelpdeskDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private static readonly string[] DemoCategories = { "IT support", "Facilities", "HR & payroll" };

    private readonly HelpdeskOptions _options;
    private readonly IConfiguration _configuration;
    private readonly IdentityRoleManager _roles;
    private readonly IdentityUserManager _users;
    private readonly IPermissionDataSeeder _permissions;
    private readonly IRepository<Category, Guid> _categories;
    private readonly CategoryManager _categoryManager;
    private readonly IGuidGenerator _guids;

    public HelpdeskDataSeedContributor(
        IOptions<HelpdeskOptions> options,
        IConfiguration configuration,
        IdentityRoleManager roles,
        IdentityUserManager users,
        IPermissionDataSeeder permissions,
        IRepository<Category, Guid> categories,
        CategoryManager categoryManager,
        IGuidGenerator guids)
    {
        _options = options.Value;
        _configuration = configuration;
        _roles = roles;
        _users = users;
        _permissions = permissions;
        _categories = categories;
        _categoryManager = categoryManager;
        _guids = guids;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        var tenantId = context?.TenantId;

        await EnsureRoleAsync(
            HelpdeskPermissions.Roles.Requester,
            isDefault: true,
            tenantId,
            HelpdeskPermissions.Tickets.Default,
            HelpdeskPermissions.Tickets.Create,
            HelpdeskPermissions.Categories.Default);

        await EnsureRoleAsync(
            HelpdeskPermissions.Roles.Agent,
            isDefault: false,
            tenantId,
            HelpdeskPermissions.Tickets.Default,
            HelpdeskPermissions.Tickets.Create,
            HelpdeskPermissions.Tickets.Work,
            HelpdeskPermissions.Categories.Default,
            HelpdeskPermissions.Reports.Default);

        if (!_options.SeedDemoData)
        {
            return;
        }

        foreach (var name in DemoCategories)
        {
            if (!await _categories.AnyAsync(category => category.NormalizedName == Category.Normalize(name)))
            {
                await _categories.InsertAsync(await _categoryManager.CreateAsync(name, null), autoSave: true);
            }
        }

        var password = _configuration["Helpdesk:DemoPassword"] ?? "1q2w3E*";
        await EnsureUserAsync("agent1", "agent1@example.com", HelpdeskPermissions.Roles.Agent, password, tenantId);
        await EnsureUserAsync("alice", "alice@example.com", HelpdeskPermissions.Roles.Requester, password, tenantId);
        await EnsureUserAsync("bob", "bob@example.com", HelpdeskPermissions.Roles.Requester, password, tenantId);
    }

    private async Task EnsureRoleAsync(string name, bool isDefault, Guid? tenantId, params string[] permissions)
    {
        if (await _roles.FindByNameAsync(name) is null)
        {
            var role = new IdentityRole(_guids.Create(), name, tenantId) { IsDefault = isDefault, IsPublic = true };
            Check(await _roles.CreateAsync(role));
        }

        await _permissions.SeedAsync(
            RolePermissionValueProvider.ProviderName, name, permissions, tenantId);
    }

    private async Task EnsureUserAsync(string userName, string email, string role, string password, Guid? tenantId)
    {
        if (await _users.FindByNameAsync(userName) is not null)
        {
            return;
        }

        var user = new IdentityUser(_guids.Create(), userName, email, tenantId);
        Check(await _users.CreateAsync(user, password));
        Check(await _users.AddToRoleAsync(user, role));
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Seeding failed: " + string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }
}

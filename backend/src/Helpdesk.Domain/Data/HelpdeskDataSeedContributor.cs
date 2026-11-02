using System;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Categories;
using Helpdesk.Permissions;
using Helpdesk.Tickets;
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
using Volo.Abp.Timing;

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
    private readonly TicketManager _ticketManager;
    private readonly IRepository<Ticket, Guid> _tickets;
    private readonly IClock _clock;

    public HelpdeskDataSeedContributor(
        IOptions<HelpdeskOptions> options,
        IConfiguration configuration,
        IdentityRoleManager roles,
        IdentityUserManager users,
        IPermissionDataSeeder permissions,
        IRepository<Category, Guid> categories,
        CategoryManager categoryManager,
        IGuidGenerator guids,
        TicketManager ticketManager,
        IRepository<Ticket, Guid> tickets,
        IClock clock)
    {
        _options = options.Value;
        _configuration = configuration;
        _roles = roles;
        _users = users;
        _permissions = permissions;
        _categories = categories;
        _categoryManager = categoryManager;
        _guids = guids;
        _ticketManager = ticketManager;
        _tickets = tickets;
        _clock = clock;
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

        if (_options.SeedDemoTickets && !await _tickets.AnyAsync())
        {
            await SeedDemoTicketsAsync();
        }
    }

    private enum Ending
    {
        Closed,
        Resolved,
        InProgress,
    }

    private static readonly (string Category, string Requester, string Title, string Description, string? Resolution, Ending Ending, TicketPriority Priority)[] DemoTickets =
    {
        ("IT support", "alice", "VPN disconnects every few minutes", "Since Monday the VPN drops about every five minutes when I work from home and I have to reconnect each time.", "Updated the VPN client to the latest version and disabled IPv6 on the network adapter.", Ending.Closed, TicketPriority.High),
        ("IT support", "bob", "Cannot sign in to email after password change", "I changed my password yesterday and now the mail app keeps asking for it and then says the login failed.", "Cleared the saved credentials in the mail client and reset the mailbox session. Signing in works again.", Ending.Closed, TicketPriority.Normal),
        ("IT support", "alice", "Printer on floor 2 jams and shows a paper error", "The printer near the kitchen jams on the second page and the display says there is a paper error.", "Replaced the pickup roller and cleaned the paper path. Test pages printed without jams.", Ending.Closed, TicketPriority.Normal),
        ("IT support", "bob", "Laptop is very slow after the update", "Since the last Windows update my laptop takes ten minutes to start and the fan runs all the time.", "Removed unneeded startup programs and ran a disk cleanup. A memory upgrade is booked for next month.", Ending.Resolved, TicketPriority.Low),
        ("IT support", "alice", "Wi-Fi keeps dropping in meeting room B", "During video calls in meeting room B the Wi-Fi disconnects and everybody has to rejoin the meeting.", "Updated the access point firmware and moved it to a less crowded channel.", Ending.Closed, TicketPriority.High),
        ("Facilities", "bob", "Air conditioner in room 204 is leaking water", "Water drips from the air conditioner in room 204 onto the desks near the window.", "Cleared the blocked condensate drain and replaced the drain pipe. No leaks since.", Ending.Closed, TicketPriority.Urgent),
        ("Facilities", "alice", "Broken chair in the open space", "One of the chairs at the east desks has a broken backrest and is not safe to sit on.", "Replaced the chair from stock and sent the broken one for disposal.", Ending.Closed, TicketPriority.Low),
        ("Facilities", "bob", "Lights are out in the corridor on floor 3", "Three tubes in the corridor on the third floor have gone out and it is quite dark in the evening.", "Replaced the tubes and checked the circuit. The corridor is lit again.", Ending.Resolved, TicketPriority.Normal),
        ("Facilities", "alice", "Door lock of the storage room is stuck", "The key turns but the storage room door will not open, so nobody can get to the supplies.", "Lubricated the lock and replaced the worn cylinder. Two new keys were handed to the office.", Ending.Closed, TicketPriority.Normal),
        ("HR & payroll", "bob", "Allowance missing from last month's payslip", "My transport allowance was not on last month's payslip although it was approved in the contract.", "Added the missing allowance as a supplementary payment in the next payroll run.", Ending.Closed, TicketPriority.High),
        ("HR & payroll", "alice", "Request for an employment certificate", "I need an employment certificate for a visa application, in English, with my position and start date.", "Prepared the certificate and sent the signed copy to your email.", Ending.Closed, TicketPriority.Normal),
        ("HR & payroll", "bob", "How do I update my bank account details?", "I changed banks and want my salary paid into the new account from next month.", null, Ending.InProgress, TicketPriority.Low),
    };

    /// <summary>A dozen finished tickets, so the suggestions and the reports have something to show on a fresh install.</summary>
    private async Task SeedDemoTicketsAsync()
    {
        var agent = await _users.FindByNameAsync("agent1")
            ?? throw new InvalidOperationException("The demo agent is missing.");
        var now = _clock.Now;

        foreach (var demo in DemoTickets)
        {
            var requester = await _users.FindByNameAsync(demo.Requester)
                ?? throw new InvalidOperationException($"The demo user {demo.Requester} is missing.");
            var category = await _categories.FirstAsync(candidate => candidate.NormalizedName == Category.Normalize(demo.Category));

            var ticket = await _ticketManager.CreateAsync(requester.Id, category.Id, demo.Title, demo.Description, TicketPriority.Normal);
            if (demo.Priority != TicketPriority.Normal)
            {
                _ticketManager.ChangePriority(ticket, demo.Priority, agent.Id);
            }

            ticket.Assign(agent.Id, agent.Id, now);
            if (demo.Ending != Ending.InProgress)
            {
                ticket.Resolve(demo.Resolution!, agent.Id, now);
            }

            if (demo.Ending == Ending.Closed)
            {
                ticket.Confirm(requester.Id, now);
            }

            await _tickets.InsertAsync(ticket, autoSave: true);
        }
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

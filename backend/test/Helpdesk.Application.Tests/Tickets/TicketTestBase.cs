using System;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Categories;
using Volo.Abp.Modularity;

namespace Helpdesk.Tickets;

public abstract class TicketTestBase<TStartupModule> : HelpdeskApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    protected readonly ITicketAppService Tickets;
    protected readonly ICategoryAppService Categories;

    protected TicketTestBase()
    {
        Tickets = GetRequiredService<ITicketAppService>();
        Categories = GetRequiredService<ICategoryAppService>();
    }

    protected Guid CategoryId(string name = "IT support")
    {
        using (As(Admin))
        {
            return Categories.GetListAsync(new GetCategoryListInput()).GetAwaiter().GetResult()
                .Items.Single(category => category.Name == name).Id;
        }
    }

    protected async Task<TicketDto> CreateAs(string user, string title = "Printer jams", string? category = null)
    {
        using (As(user))
        {
            return await Tickets.CreateAsync(new CreateTicketDto
            {
                CategoryId = category is null ? CategoryId() : CategoryId(category),
                Title = title,
                Description = "Page two always jams in tray 1.",
            });
        }
    }

    /// <summary>Alice's ticket, taken by the agent.</summary>
    protected async Task<TicketDto> OpenTicket(string title = "Printer jams")
    {
        var created = await CreateAs(Alice, title);
        using (As(Agent))
        {
            return await Tickets.ClaimAsync(created.Id);
        }
    }

    /// <summary>Alice's ticket, taken and then resolved by the agent.</summary>
    protected async Task<TicketDto> ResolvedTicket()
    {
        var open = await OpenTicket();
        using (As(Agent))
        {
            return await Tickets.ResolveAsync(open.Id, new ResolveTicketDto { Resolution = "Replaced the roller" });
        }
    }

    /// <summary>Alice's ticket, resolved and confirmed: closed.</summary>
    protected async Task<TicketDto> ClosedTicket()
    {
        var resolved = await ResolvedTicket();
        using (As(Alice))
        {
            return await Tickets.ConfirmAsync(resolved.Id);
        }
    }

    protected async Task<TicketDto> Reload(Guid id, string user = Admin)
    {
        using (As(user))
        {
            return await Tickets.GetAsync(id);
        }
    }
}

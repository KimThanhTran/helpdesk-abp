using System;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Tickets;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Helpdesk.Categories;

/// <summary>Rules that need to look at other records: unique names and "not while tickets use it".</summary>
public class CategoryManager : DomainService
{
    private readonly IRepository<Category, Guid> _categories;
    private readonly IRepository<Ticket, Guid> _tickets;

    public CategoryManager(IRepository<Category, Guid> categories, IRepository<Ticket, Guid> tickets)
    {
        _categories = categories;
        _tickets = tickets;
    }

    public async Task<Category> CreateAsync(string name, string? description)
    {
        await EnsureNameFreeAsync(name, exceptId: null);
        return new Category(GuidGenerator.Create(), name, description);
    }

    public async Task UpdateAsync(Category category, string name, string? description)
    {
        await EnsureNameFreeAsync(name, category.Id);
        category.SetDetails(name, description);
    }

    public async Task EnsureDeletableAsync(Category category)
    {
        if (await _tickets.AnyAsync(ticket => ticket.CategoryId == category.Id))
        {
            throw HelpdeskErrors.CategoryInUse();
        }
    }

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId)
    {
        var normalized = Category.Normalize(name);
        var taken = await _categories.AnyAsync(category =>
            category.NormalizedName == normalized && category.Id != exceptId);
        if (taken)
        {
            throw HelpdeskErrors.CategoryDuplicateName(name.Trim());
        }
    }
}

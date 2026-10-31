using System;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Helpdesk.Categories;

[Authorize(HelpdeskPermissions.Categories.Default)]
public class CategoryAppService : HelpdeskAppService, ICategoryAppService
{
    private readonly IRepository<Category, Guid> _categories;
    private readonly CategoryManager _manager;

    public CategoryAppService(IRepository<Category, Guid> categories, CategoryManager manager)
    {
        _categories = categories;
        _manager = manager;
    }

    public async Task<ListResultDto<CategoryDto>> GetListAsync(GetCategoryListInput input)
    {
        // people who cannot manage categories never see the disabled ones
        var activeOnly = input.ActiveOnly || !await AuthorizationService.IsGrantedAsync(HelpdeskPermissions.Categories.Manage);

        var query = await _categories.GetQueryableAsync();
        if (activeOnly)
        {
            query = query.Where(category => category.IsActive);
        }

        var items = await AsyncExecuter.ToListAsync(query.OrderBy(category => category.Name));
        return new ListResultDto<CategoryDto>(items.Select(ToDto).ToList());
    }

    [Authorize(HelpdeskPermissions.Categories.Manage)]
    public async Task<CategoryDto> CreateAsync(SaveCategoryDto input)
    {
        var category = await _manager.CreateAsync(input.Name, input.Description);
        await _categories.InsertAsync(category, autoSave: true);
        return ToDto(category);
    }

    [Authorize(HelpdeskPermissions.Categories.Manage)]
    public async Task<CategoryDto> UpdateAsync(Guid id, SaveCategoryDto input)
    {
        var category = await _categories.GetAsync(id);
        await _manager.UpdateAsync(category, input.Name, input.Description);
        await _categories.UpdateAsync(category, autoSave: true);
        return ToDto(category);
    }

    [Authorize(HelpdeskPermissions.Categories.Manage)]
    public async Task<CategoryDto> SetActiveAsync(Guid id, SetCategoryActiveDto input)
    {
        var category = await _categories.GetAsync(id);
        category.SetActive(input.IsActive);
        await _categories.UpdateAsync(category, autoSave: true);
        return ToDto(category);
    }

    [Authorize(HelpdeskPermissions.Categories.Manage)]
    public async Task DeleteAsync(Guid id)
    {
        var category = await _categories.GetAsync(id);
        await _manager.EnsureDeletableAsync(category);
        await _categories.DeleteAsync(category, autoSave: true);
    }

    private static CategoryDto ToDto(Category category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Description = category.Description,
        IsActive = category.IsActive,
    };
}

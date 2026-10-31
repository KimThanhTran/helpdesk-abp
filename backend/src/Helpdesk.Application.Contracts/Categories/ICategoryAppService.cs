using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Helpdesk.Categories;

public interface ICategoryAppService : IApplicationService
{
    Task<ListResultDto<CategoryDto>> GetListAsync(GetCategoryListInput input);

    Task<CategoryDto> CreateAsync(SaveCategoryDto input);

    Task<CategoryDto> UpdateAsync(Guid id, SaveCategoryDto input);

    Task<CategoryDto> SetActiveAsync(Guid id, SetCategoryActiveDto input);

    /// <summary>Soft-deletes a category. Refused while tickets use it: disable it instead.</summary>
    Task DeleteAsync(Guid id);
}

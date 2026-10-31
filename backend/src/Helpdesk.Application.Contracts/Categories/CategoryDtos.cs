using System;
using System.ComponentModel.DataAnnotations;
using Helpdesk.Tickets;
using Volo.Abp.Application.Dtos;

namespace Helpdesk.Categories;

public class CategoryDto : EntityDto<Guid>
{
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }
}

public class SaveCategoryDto
{
    [Required]
    [StringLength(TicketConsts.MaxCategoryNameLength)]
    public string Name { get; set; } = null!;

    [StringLength(TicketConsts.MaxCategoryDescriptionLength)]
    public string? Description { get; set; }
}

public class GetCategoryListInput
{
    /// <summary>When true, disabled categories are left out (what a requester's "new ticket" form needs).</summary>
    public bool ActiveOnly { get; set; }
}

public class SetCategoryActiveDto
{
    public bool IsActive { get; set; }
}

using System;
using Helpdesk.Tickets;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Helpdesk.Categories;

/// <summary>
/// A kind of request (for example IT, Facilities, HR). Categories are data, not code:
/// adding one needs no deployment and no branch in the ticket rules.
/// </summary>
public class Category : FullAuditedAggregateRoot<Guid>
{
    public string Name { get; private set; } = null!;

    /// <summary>Upper-case name used for the case-insensitive unique index.</summary>
    public string NormalizedName { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>A disabled category keeps its tickets but cannot receive new ones.</summary>
    public bool IsActive { get; private set; } = true;

    protected Category()
    {
    }

    internal Category(Guid id, string name, string? description)
        : base(id)
    {
        SetDetails(name, description);
    }

    internal void SetDetails(string name, string? description)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), TicketConsts.MaxCategoryNameLength).Trim();
        NormalizedName = Normalize(Name);
        Description = Check.Length(description?.Trim(), nameof(description), TicketConsts.MaxCategoryDescriptionLength);
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
}

using Helpdesk.Categories;
using Helpdesk.Tickets;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;

namespace Helpdesk.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public class HelpdeskDbContext :
    AbpDbContext<HelpdeskDbContext>,
    IIdentityDbContext,
    ITenantManagementDbContext
{
    public DbSet<Category> Categories { get; set; }
    public DbSet<Ticket> Tickets { get; set; }
    public DbSet<TicketComment> TicketComments { get; set; }
    public DbSet<TicketEvent> TicketEvents { get; set; }
    public DbSet<TicketCodeCounter> TicketCodeCounters { get; set; }

    #region Entities from the modules

    /* Notice: We only implemented IIdentityDbContext and ITenantManagementDbContext
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityDbContext and ITenantManagementDbContext.
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    //Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }
    // Tenant Management
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }

    #endregion

    public HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureFeatureManagement();
        builder.ConfigureTenantManagement();

        /* Configure your own tables/entities inside here */

        builder.Entity<Category>(b =>
        {
            b.ToTable(HelpdeskConsts.DbTablePrefix + "Categories", HelpdeskConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(TicketConsts.MaxCategoryNameLength);
            b.Property(x => x.NormalizedName).IsRequired().HasMaxLength(TicketConsts.MaxCategoryNameLength);
            b.Property(x => x.Description).HasMaxLength(TicketConsts.MaxCategoryDescriptionLength);
            // unique among the categories that are not soft-deleted, so a deleted name can be reused
            b.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("\"IsDeleted\" = false");
        });

        builder.Entity<Ticket>(b =>
        {
            b.ToTable(HelpdeskConsts.DbTablePrefix + "Tickets", HelpdeskConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(TicketConsts.MaxCodeLength);
            b.Property(x => x.Title).IsRequired().HasMaxLength(TicketConsts.MaxTitleLength);
            b.Property(x => x.Description).IsRequired().HasMaxLength(TicketConsts.MaxDescriptionLength);
            b.Property(x => x.WaitingNote).HasMaxLength(TicketConsts.MaxReasonLength);
            b.Property(x => x.Resolution).HasMaxLength(TicketConsts.MaxCommentLength);

            // Tickets reference people and categories by id only, with no navigation: a navigation would make
            // ABP bump the category's concurrency stamp on every new ticket and break concurrent creation.
            b.HasIndex(x => x.Code).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(x => new { x.RequesterId, x.Status });
            b.HasIndex(x => new { x.Status, x.AssigneeId });
            b.HasIndex(x => x.CategoryId);

            b.HasMany(x => x.Comments).WithOne().HasForeignKey(x => x.TicketId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.TicketId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TicketComment>(b =>
        {
            b.ToTable(HelpdeskConsts.DbTablePrefix + "TicketComments", HelpdeskConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Body).IsRequired().HasMaxLength(TicketConsts.MaxCommentLength);
            b.HasIndex(x => new { x.TicketId, x.CreationTime });
        });

        builder.Entity<TicketEvent>(b =>
        {
            b.ToTable(HelpdeskConsts.DbTablePrefix + "TicketEvents", HelpdeskConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Note).HasMaxLength(TicketConsts.MaxReasonLength);
            b.HasIndex(x => new { x.TicketId, x.CreationTime });
        });

        builder.Entity<TicketCodeCounter>(b =>
        {
            b.ToTable(HelpdeskConsts.DbTablePrefix + "TicketCodeCounters", HelpdeskConsts.DbSchema);
            b.HasKey(x => x.Year);
            b.Property(x => x.Year).ValueGeneratedNever();
        });
    }
}

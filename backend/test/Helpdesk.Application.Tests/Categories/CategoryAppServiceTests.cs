using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Tickets;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Categories;

public abstract class CategoryAppServiceTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Fact]
    public async Task An_admin_creates_and_lists_categories()
    {
        using (As(Admin))
        {
            var created = await Categories.CreateAsync(new SaveCategoryDto { Name = "Legal", Description = "Contracts" });

            created.IsActive.ShouldBeTrue();
            (await Categories.GetListAsync(new GetCategoryListInput())).Items.Select(c => c.Name).ShouldContain("Legal");
        }
    }

    [Fact]
    public async Task Names_must_be_unique_ignoring_case_and_spaces()
    {
        using (As(Admin))
        {
            var rejected = await Should.ThrowAsync<BusinessException>(
                () => Categories.CreateAsync(new SaveCategoryDto { Name = "  it SUPPORT " }));

            rejected.Code.ShouldBe(HelpdeskDomainErrorCodes.CategoryDuplicateName);
        }
    }

    [Fact]
    public async Task Renaming_to_another_categorys_name_is_refused_but_keeping_your_own_is_fine()
    {
        var facilities = CategoryId("Facilities");

        using (As(Admin))
        {
            (await Should.ThrowAsync<BusinessException>(
                () => Categories.UpdateAsync(facilities, new SaveCategoryDto { Name = "IT support" })))
                .Code.ShouldBe(HelpdeskDomainErrorCodes.CategoryDuplicateName);

            var renamed = await Categories.UpdateAsync(facilities, new SaveCategoryDto { Name = "facilities", Description = "Buildings" });
            renamed.Name.ShouldBe("facilities");
            renamed.Description.ShouldBe("Buildings");
        }
    }

    [Fact]
    public async Task Only_managers_can_change_categories()
    {
        var it = CategoryId();

        foreach (var user in new[] { Alice, Agent })
        {
            using (As(user))
            {
                await Should.ThrowAsync<AbpAuthorizationException>(
                    () => Categories.CreateAsync(new SaveCategoryDto { Name = "Sneaky" }));
                await Should.ThrowAsync<AbpAuthorizationException>(
                    () => Categories.SetActiveAsync(it, new SetCategoryActiveDto { IsActive = false }));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Categories.DeleteAsync(it));
            }
        }
    }

    [Fact]
    public async Task People_who_cannot_manage_never_see_disabled_categories()
    {
        var facilities = CategoryId("Facilities");
        using (As(Admin))
        {
            await Categories.SetActiveAsync(facilities, new SetCategoryActiveDto { IsActive = false });
            (await Categories.GetListAsync(new GetCategoryListInput())).Items.Select(c => c.Name).ShouldContain("Facilities");
        }

        using (As(Alice))
        {
            // even when she asks for everything, a requester only gets the active ones
            (await Categories.GetListAsync(new GetCategoryListInput { ActiveOnly = false }))
                .Items.Select(c => c.Name).ShouldNotContain("Facilities");
        }
    }

    [Fact]
    public async Task A_category_with_tickets_cannot_be_deleted_but_can_be_disabled()
    {
        await CreateAs(Alice);
        var it = CategoryId();

        using (As(Admin))
        {
            (await Should.ThrowAsync<BusinessException>(() => Categories.DeleteAsync(it)))
                .Code.ShouldBe(HelpdeskDomainErrorCodes.CategoryInUse);

            (await Categories.SetActiveAsync(it, new SetCategoryActiveDto { IsActive = false })).IsActive.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task An_unused_category_can_be_deleted_and_its_name_reused()
    {
        using (As(Admin))
        {
            var temporary = await Categories.CreateAsync(new SaveCategoryDto { Name = "Temporary" });

            await Categories.DeleteAsync(temporary.Id);
            (await Categories.GetListAsync(new GetCategoryListInput())).Items.Select(c => c.Name).ShouldNotContain("Temporary");

            // soft-deleted names do not block a new category with the same name
            (await Categories.CreateAsync(new SaveCategoryDto { Name = "Temporary" })).Name.ShouldBe("Temporary");
        }
    }

    [Fact]
    public async Task A_name_is_required_and_has_a_length_limit()
    {
        using (As(Admin))
        {
            await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(
                () => Categories.CreateAsync(new SaveCategoryDto { Name = "" }));
            await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(
                () => Categories.CreateAsync(new SaveCategoryDto { Name = new string('x', TicketConsts.MaxCategoryNameLength + 1) }));
        }
    }
}

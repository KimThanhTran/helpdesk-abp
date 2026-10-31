using Volo.Abp.Modularity;

namespace Helpdesk;

[DependsOn(
    typeof(HelpdeskDomainModule),
    typeof(HelpdeskTestBaseModule)
)]
public class HelpdeskDomainTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // roles, the demo users (agent1, alice, bob) and the demo categories
        Configure<HelpdeskOptions>(options => options.SeedDemoData = true);
    }

}

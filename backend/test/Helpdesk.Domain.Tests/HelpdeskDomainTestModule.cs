using System;
using Helpdesk.Sla;
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

        // Open around the clock, every day, in UTC: a test that moves the clock by two days then means 48 working
        // hours whatever day or hour the suite runs. The calendar itself is tested separately in Sla/BusinessCalendarTests.
        Configure<BusinessHoursOptions>(options =>
        {
            options.TimeZone = "UTC";
            options.Start = TimeSpan.Zero;
            options.End = TimeSpan.FromHours(24) - TimeSpan.FromTicks(1);
            options.WorkDays = Enum.GetValues<DayOfWeek>();
        });
    }

}

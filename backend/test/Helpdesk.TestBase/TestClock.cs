using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;

namespace Helpdesk;

/// <summary>
/// The application's clock in tests. Time only moves when a test says so, which lets a test
/// jump forward days to check the reopen window, the automatic close and the SLA targets.
/// </summary>
[Dependency(ServiceLifetime.Singleton, ReplaceServices = true)]
[ExposeServices(typeof(IClock), typeof(TestClock))]
public class TestClock : Clock
{
    private TimeSpan _offset = TimeSpan.Zero;

    public TestClock(
        IOptions<AbpClockOptions> options,
        ICurrentTimezoneProvider currentTimezoneProvider,
        ITimezoneProvider timezoneProvider)
        : base(options, currentTimezoneProvider, timezoneProvider)
    {
    }

    public override DateTime Now => base.Now + _offset;

    public void Advance(TimeSpan by) => _offset += by;
}

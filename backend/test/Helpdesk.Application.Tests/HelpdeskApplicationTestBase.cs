using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Shouldly;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Timing;

namespace Helpdesk;

public abstract class HelpdeskApplicationTestBase<TStartupModule> : HelpdeskTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    /// <summary>The seeded people: agent1 (agent), alice and bob (requesters) and admin.</summary>
    protected const string Agent = "agent1";
    protected const string Alice = "alice";
    protected const string Bob = "bob";
    protected const string Admin = "admin";

    protected TestClock Time => GetRequiredService<TestClock>();

    protected IClock Clock => GetRequiredService<IClock>();

    /// <summary>
    /// Runs the following calls as a real seeded user with their real roles, so permissions
    /// are checked exactly as in production. Dispose the result to go back to the previous user.
    /// </summary>
    protected IDisposable As(string userName)
    {
        var (id, roles) = WithUnitOfWorkAsync(async () =>
        {
            var users = GetRequiredService<IIdentityUserRepository>();
            var user = await users.FindByNormalizedUserNameAsync(userName.ToUpperInvariant());
            user.ShouldNotBeNull($"seeded user '{userName}' was not found");
            return (user.Id, (await users.GetRoleNamesAsync(user.Id)).ToList());
        }).GetAwaiter().GetResult();

        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, id.ToString()),
            new(AbpClaimTypes.UserName, userName),
        };
        claims.AddRange(roles.Select(role => new Claim(AbpClaimTypes.Role, role)));

        // an identity with an authentication type counts as signed in, like a real token would
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthentication"));
        return GetRequiredService<ICurrentPrincipalAccessor>().Change(principal);
    }

    protected Guid UserId(string userName) =>
        WithUnitOfWorkAsync(async () =>
        {
            var user = await GetRequiredService<IIdentityUserRepository>()
                .FindByNormalizedUserNameAsync(userName.ToUpperInvariant());
            return user!.Id;
        }).GetAwaiter().GetResult();
}

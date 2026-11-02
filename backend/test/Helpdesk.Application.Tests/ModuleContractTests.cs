using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Volo.Abp.Application.Services;
using Xunit;

namespace Helpdesk;

/// <summary>Rules about the shape of the code itself, so a future change cannot quietly break them.</summary>
public class ModuleContractTests
{
    private static readonly Type[] AppServices = typeof(HelpdeskApplicationModule).Assembly.GetTypes()
        .Where(type => typeof(IApplicationService).IsAssignableFrom(type)
            && !type.IsAbstract
            && type.Namespace is not null
            && type.Namespace.StartsWith("Helpdesk", StringComparison.Ordinal))
        .ToArray();

    [Fact]
    public void The_module_has_the_application_services_we_expect()
    {
        AppServices.Select(type => type.Name).ShouldBe(
            new[] { "CategoryAppService", "TicketAppService", "TicketAttachmentAppService", "TicketExportAppService", "TicketReportAppService", "TicketTriageAppService" }, ignoreOrder: true);
    }

    [Fact]
    public void Every_application_service_requires_a_signed_in_user_with_a_permission()
    {
        foreach (var service in AppServices)
        {
            var attribute = service.GetCustomAttribute<AuthorizeAttribute>();

            attribute.ShouldNotBeNull($"{service.Name} must have [Authorize] on the class");
            attribute.Policy.ShouldNotBeNullOrWhiteSpace($"{service.Name} must name a permission, not just require a login");
        }
    }

    [Fact]
    public void No_public_method_opts_out_of_authorization()
    {
        foreach (var method in AppServices.SelectMany(service => service.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
        {
            method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull($"{method.DeclaringType!.Name}.{method.Name}");
        }
    }
}

using System;
using Helpdesk.Integrations;
using Volo.Abp.Account;
using Volo.Abp.Mapperly;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk;

[DependsOn(
    typeof(HelpdeskDomainModule),
    typeof(AbpAccountApplicationModule),
    typeof(HelpdeskApplicationContractsModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpTenantManagementApplicationModule),
    typeof(AbpFeatureManagementApplicationModule),
    typeof(AbpSettingManagementApplicationModule)
    )]
public class HelpdeskApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddMapperlyObjectMapper<HelpdeskApplicationModule>();

        // the Google Apps Script web app answers through a redirect, which HttpClient follows by default
        context.Services.AddHttpClient(SheetsExporter.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
    }
}

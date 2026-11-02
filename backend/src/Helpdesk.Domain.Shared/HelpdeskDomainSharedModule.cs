using System;
using Helpdesk.Localization;
using Volo.Abp.AuditLogging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.Localization.ExceptionHandling;
using Volo.Abp.Modularity;
using Volo.Abp.Timing;
using Volo.Abp.OpenIddict;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;
using Volo.Abp.Validation.Localization;
using Volo.Abp.VirtualFileSystem;

namespace Helpdesk;

[DependsOn(
    typeof(AbpAuditLoggingDomainSharedModule),
    typeof(AbpBackgroundJobsDomainSharedModule),
    typeof(AbpFeatureManagementDomainSharedModule),
    typeof(AbpIdentityDomainSharedModule),
    typeof(AbpOpenIddictDomainSharedModule),
    typeof(AbpPermissionManagementDomainSharedModule),
    typeof(AbpSettingManagementDomainSharedModule),
    typeof(AbpTenantManagementDomainSharedModule)    
    )]
public class HelpdeskDomainSharedModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        HelpdeskGlobalFeatureConfigurator.Configure();
        HelpdeskModuleExtensionConfigurator.Configure();
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<HelpdeskDomainSharedModule>();
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            // the languages a caller may ask for with Accept-Language (or ?culture=vi); anything else answers in English
            options.Languages.Add(new LanguageInfo("en", "en", "English"));
            options.Languages.Add(new LanguageInfo("vi", "vi", "Tiếng Việt"));

            options.Resources
                .Add<HelpdeskResource>("en")
                .AddBaseTypes(typeof(AbpValidationResource))
                .AddVirtualJson("/Localization/Helpdesk");

            options.DefaultResourceType = typeof(HelpdeskResource);
        });

        // all timestamps are UTC: the SLA calendar and the database both assume it
        Configure<AbpClockOptions>(options => options.Kind = DateTimeKind.Utc);

        Configure<AbpExceptionLocalizationOptions>(options =>
        {
            options.MapCodeNamespace("Helpdesk", typeof(HelpdeskResource));
        });
    }
}

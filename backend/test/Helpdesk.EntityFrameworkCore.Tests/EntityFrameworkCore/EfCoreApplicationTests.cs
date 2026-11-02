using Helpdesk.Categories;
using Helpdesk.Export;
using Helpdesk.Integrations;
using Helpdesk.Notifications;
using Helpdesk.Reports;
using Helpdesk.Security;
using Helpdesk.Tickets;
using Helpdesk.Triage;
using Xunit;

namespace Helpdesk.EntityFrameworkCore;

/* The application tests are written once, against any database, in Helpdesk.Application.Tests.
 * These classes run them on in-memory SQLite. The ticket numbering SQL (INSERT ... ON CONFLICT ... RETURNING)
 * is the same statement PostgreSQL runs in production; Helpdesk.PostgresTests proves it under concurrency. */

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreTicketWorkflowTests : TicketWorkflowTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreTicketCommentTests : TicketCommentTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreTicketListTests : TicketListTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreCategoryAppServiceTests : CategoryAppServiceTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreTicketReportAppServiceTests : TicketReportAppServiceTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreSheetsExportTests : SheetsExportTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreRolePermissionTests : RolePermissionTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreTicketSlaPauseTests : TicketSlaPauseTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreNotificationTests : NotificationTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreTicketTriageTests : TicketTriageTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreTicketAttachmentTests : TicketAttachmentTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreTicketExportTests : TicketExportTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

[Collection(HelpdeskTestConsts.CollectionDefinitionName)]
public class EfCoreDemoDataTests : DemoDataTests<HelpdeskEntityFrameworkCoreTestModule>
{
}

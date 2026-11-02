using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Reports;
using Helpdesk.Tickets;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Export;

public abstract class TicketExportTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ITicketExportAppService _export;

    protected TicketExportTests()
    {
        _export = GetRequiredService<ITicketExportAppService>();
    }

    private static async Task<XlsxContents> Open(IRemoteStreamContent file)
    {
        using var copy = new MemoryStream();
        await file.GetStream().CopyToAsync(copy);
        return XlsxContents.Read(copy.ToArray());
    }

    private async Task<XlsxContents> Tickets(ExportTicketsInput? input = null)
    {
        using (As(Agent))
        {
            return await Open(await _export.GetTicketsAsync(input ?? new ExportTicketsInput()));
        }
    }

    [Fact]
    public async Task An_agent_gets_one_sheet_with_a_row_per_ticket_and_the_columns_a_person_wants()
    {
        await OpenTicket("VPN is down");
        await CreateAs(Bob, "Broken chair", "Facilities");

        var rows = (await Tickets()).Sheets["Tickets"];

        rows.Count.ShouldBe(3);
        rows[0].Take(8).ShouldBe(new object?[]
        {
            "Code", "Title", "Status", "Priority", "Category", "Requester", "Assignee", "Created (UTC)",
        });
        var vpn = rows.Single(r => (string?)r[1] == "VPN is down");
        vpn[0].ShouldBeOfType<string>().ShouldStartWith("HD-");
        vpn[2].ShouldBe("Open");
        vpn[4].ShouldBe("IT support");
        vpn[5].ShouldBe(Alice);
        vpn[6].ShouldBe(Agent);
        vpn[7].ShouldBeOfType<double>();

        var chair = rows.Single(r => (string?)r[1] == "Broken chair");
        chair[2].ShouldBe("New");
        chair[6].ShouldBeNull(); // nobody has taken it
        chair[5].ShouldBe(Bob);
    }

    [Fact]
    public async Task The_filters_and_the_scope_work_like_they_do_in_the_queue()
    {
        await OpenTicket("VPN is down");
        await CreateAs(Alice, "Printer jams");
        await CreateAs(Bob, "VPN token expired");

        (await Tickets(new ExportTicketsInput { Filter = "vpn" })).Sheets["Tickets"].Count.ShouldBe(3);
        (await Tickets(new ExportTicketsInput { Filter = "VPN", Status = TicketStatus.New })).Sheets["Tickets"].Count.ShouldBe(2);
        (await Tickets(new ExportTicketsInput { Scope = QueueScope.Unassigned })).Sheets["Tickets"].Count.ShouldBe(3);
        (await Tickets(new ExportTicketsInput { Scope = QueueScope.Mine })).Sheets["Tickets"].Count.ShouldBe(2);
        (await Tickets(new ExportTicketsInput { Filter = "nothing like this" })).Sheets["Tickets"].Count.ShouldBe(1); // header only
    }

    [Fact]
    public async Task A_title_that_looks_like_a_formula_is_written_as_text()
    {
        await CreateAs(Alice, "=HYPERLINK(\"http://evil.example\",\"x\")");

        var contents = await Tickets();

        contents.Sheets["Tickets"][1][1].ShouldBe("=HYPERLINK(\"http://evil.example\",\"x\")");
        contents.Parts["xl/worksheets/sheet1.xml"].ShouldNotContain("<f>");
    }

    [Fact]
    public async Task The_file_has_a_dated_name_and_the_excel_content_type()
    {
        using (As(Agent))
        {
            var file = await _export.GetTicketsAsync(new ExportTicketsInput());

            file.ContentType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            file.FileName.ShouldMatch(@"^helpdesk-tickets-\d{4}-\d{2}-\d{2}\.xlsx$");
        }
    }

    [Fact]
    public async Task Requesters_cannot_export_anything()
    {
        using (As(Alice))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _export.GetTicketsAsync(new ExportTicketsInput()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _export.GetSummaryAsync(new GetReportInput()));
        }
    }

    [Fact]
    public async Task The_report_has_a_summary_and_one_sheet_each_for_status_priority_and_category()
    {
        await OpenTicket("VPN is down");
        await CreateAs(Bob, "Broken chair", "Facilities");

        XlsxContents contents;
        using (As(Agent))
        {
            var file = await _export.GetSummaryAsync(new GetReportInput());
            file.FileName.ShouldStartWith("helpdesk-report-");
            contents = await Open(file);
        }

        contents.SheetNames.ShouldBe(new[] { "Summary", "By status", "By priority", "By category" });
        contents.Sheets["Summary"].Single(r => (string?)r[0] == "Tickets created")[1].ShouldBe(2d);
        contents.Sheets["By status"].Skip(1).Select(r => (string?)r[0]).ShouldBe(new[] { "New", "Open" }, ignoreOrder: true);
        contents.Sheets["By category"].Skip(1).Select(r => (string?)r[0]).ShouldBe(new[] { "IT support", "Facilities" }, ignoreOrder: true);
        contents.Sheets["By priority"].Skip(1).Sum(r => (double)r[1]!).ShouldBe(2d);
    }
}

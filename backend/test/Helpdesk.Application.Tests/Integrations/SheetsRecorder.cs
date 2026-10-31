using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
using Volo.Abp.DependencyInjection;

namespace Helpdesk.Integrations;

/// <summary>Collects what would have been sent to Google Sheets, so tests can check it without a network.</summary>
public class SheetsRecorder : ISingletonDependency
{
    private readonly ConcurrentQueue<TicketSheetRow> _rows = new();

    public bool IncludeTitle { get; set; }

    public bool IncludePeople { get; set; }

    public IReadOnlyList<TicketSheetRow> Rows => _rows.ToList();

    public void Add(TicketSheetRow row) => _rows.Enqueue(row);
}

/// <summary>Replaces the real exporter in tests: always "enabled", and it records instead of posting.</summary>
[Dependency(ServiceLifetime.Transient, ReplaceServices = true)]
[ExposeServices(typeof(SheetsExporter))]
public class RecordingSheetsExporter : SheetsExporter
{
    private readonly SheetsRecorder _recorder;

    public RecordingSheetsExporter(
        IHttpClientFactory httpClients,
        IOptions<HelpdeskOptions> options,
        ILogger<SheetsExporter> logger,
        SheetsRecorder recorder)
        : base(httpClients, options, logger)
    {
        _recorder = recorder;
    }

    public override bool Enabled => true;

    public override bool IncludeTitle => _recorder.IncludeTitle;

    public override bool IncludePeople => _recorder.IncludePeople;

    public override Task SendAsync(TicketSheetRow row)
    {
        _recorder.Add(row);
        return Task.CompletedTask;
    }
}

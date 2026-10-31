using System;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Helpdesk.Integrations;

/// <summary>One row of the Google Sheet: what management needs to follow a ticket, nothing more.</summary>
public class TicketSheetRow
{
    public string Code { get; set; } = null!;

    public string? Title { get; set; }

    public string Category { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string Priority { get; set; } = null!;

    public string? Requester { get; set; }

    public string? Assignee { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When the ticket last changed. The script ignores a delivery older than what it already stored.</summary>
    public DateTime UpdatedAt { get; set; }

    public DateTime FirstResponseDueAt { get; set; }

    public DateTime ResolutionDueAt { get; set; }

    public DateTime? FirstRespondedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public int ReopenCount { get; set; }

    /// <summary>What just happened, for the history sheet (Created, Assigned, StatusChanged...).</summary>
    public string Event { get; set; } = null!;

    /// <summary>Unique per delivery, so the script can ignore a row it has already stored.</summary>
    public Guid EventId { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Posts a signed summary of a ticket to a Google Apps Script web app that writes it into a Sheet.
/// Apps Script cannot read request headers, so the signature travels in the body:
/// HMAC-SHA256 over "timestamp.payload" with a shared secret, where payload is the exact JSON string sent.
/// Delivery is best effort: a failure is logged and never fails the ticket operation.
/// </summary>
public class SheetsExporter : ITransientDependency
{
    public const string HttpClientName = "sheets";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClients;
    private readonly SheetsOptions _options;
    private readonly ILogger<SheetsExporter> _logger;

    public SheetsExporter(
        IHttpClientFactory httpClients, IOptions<HelpdeskOptions> options, ILogger<SheetsExporter> logger)
    {
        _httpClients = httpClients;
        _options = options.Value.Sheets;
        _logger = logger;
    }

    public virtual bool Enabled => _options.Enabled;

    public virtual bool IncludeTitle => _options.IncludeTitle;

    public virtual bool IncludePeople => _options.IncludePeople;

    public virtual async Task SendAsync(TicketSheetRow row)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            var body = Sign(row, _options.Secret!, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var client = _httpClients.CreateClient(HttpClientName);
            using var response = await client.PostAsync(_options.WebhookUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Sheets export for {Code} failed with HTTP {Status}", row.Code, (int)response.StatusCode);
                return;
            }

            // Apps Script always answers HTTP 200: a refusal (bad signature, not configured) is only in the body.
            var rejection = ReadRejection(await response.Content.ReadAsStringAsync());
            if (rejection is not null)
            {
                _logger.LogWarning("Sheets export for {Code} was refused by the script: {Error}", row.Code, rejection);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Sheets export for {Code} failed", row.Code);
        }
    }

    /// <summary>Builds the request body. Public so tests can check it against the Apps Script side.</summary>
    public static string Sign(TicketSheetRow row, string secret, long unixSeconds)
    {
        var payload = JsonSerializer.Serialize(row, Json);
        var timestamp = unixSeconds.ToString(CultureInfo.InvariantCulture);

        return JsonSerializer.Serialize(
            new { timestamp, payload, signature = ComputeSignature(secret, timestamp, payload) }, Json);
    }

    /// <summary>HMAC-SHA256 over "timestamp.payload", lower-case hex. The Apps Script recomputes exactly this.</summary>
    public static string ComputeSignature(string secret, string timestamp, string payload)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return Convert.ToHexStringLower(mac);
    }

    /// <summary>Returns the script's error code when it answered { "ok": false }, otherwise null.</summary>
    public static string? ReadRejection(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("ok", out var ok)
                && ok.ValueKind == JsonValueKind.False)
            {
                return root.TryGetProperty("error", out var error) ? error.GetString() : "unknown";
            }

            return null;
        }
        catch (JsonException)
        {
            return "unreadable response"; // for example an HTML page from a wrong URL
        }
    }
}

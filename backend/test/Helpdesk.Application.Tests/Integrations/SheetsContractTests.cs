using System.Linq;
using Shouldly;
using Xunit;

namespace Helpdesk.Integrations;

/// <summary>
/// The agreement between the API and the Google Apps Script. The same vector is checked on the script side in
/// integrations/google-sheets/Code.test.js, so a change on one side that breaks the other fails a test.
/// </summary>
public class SheetsContractTests
{
    // computed independently with Python's hmac module
    private const string GoldenSecret = "golden-secret";
    private const string GoldenTimestamp = "1700000000";
    private const string GoldenPayload = "{\"code\":\"HD-2026-000001\",\"status\":\"New\"}";
    private const string GoldenSignature = "7087aaccacb78cdb6231d9ea02fbec5d60f5c29c1b6671719578e2e35a51ece8";

    [Fact]
    public void The_signature_matches_the_golden_vector_the_script_also_checks()
    {
        SheetsExporter.ComputeSignature(GoldenSecret, GoldenTimestamp, GoldenPayload).ShouldBe(GoldenSignature);
    }

    [Fact]
    public void The_timestamp_is_part_of_what_is_signed_so_a_captured_request_cannot_be_replayed_later()
    {
        SheetsExporter.ComputeSignature(GoldenSecret, "1700000001", GoldenPayload).ShouldNotBe(GoldenSignature);
    }

    /// <summary>The exact field names the script reads. Code.test.js checks the same list.</summary>
    private static readonly string[] Fields =
    {
        "code", "title", "category", "status", "priority", "requester", "assignee", "createdAt", "updatedAt",
        "firstResponseDueAt", "resolutionDueAt", "firstRespondedAt", "resolvedAt", "closedAt", "reopenCount",
        "event", "eventId",
    };

    [Fact]
    public void The_payload_has_exactly_the_fields_the_script_reads()
    {
        var row = new TicketSheetRow { Code = "HD-2026-000001", Category = "IT", Status = "New", Priority = "Normal", Event = "Created" };

        var body = System.Text.Json.JsonDocument.Parse(SheetsExporter.Sign(row, "secret", 1));
        var payload = System.Text.Json.JsonDocument.Parse(body.RootElement.GetProperty("payload").GetString()!);

        payload.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(Fields, ignoreOrder: true);
    }

    [Fact]
    public void The_envelope_has_exactly_timestamp_payload_and_signature()
    {
        var body = System.Text.Json.JsonDocument.Parse(
            SheetsExporter.Sign(new TicketSheetRow { Code = "x", Category = "c", Status = "s", Priority = "p", Event = "e" }, "secret", 1));

        body.RootElement.EnumerateObject().Select(property => property.Name)
            .ShouldBe(new[] { "timestamp", "payload", "signature" }, ignoreOrder: true);
    }

    [Theory]
    [InlineData("{\"ok\":true}", null)]
    [InlineData("{\"ok\":false,\"error\":\"bad_signature\"}", "bad_signature")]
    [InlineData("{\"ok\":false}", "unknown")]
    [InlineData("<html>Sorry, unable to open the file</html>", "unreadable response")]
    [InlineData("", "unreadable response")]
    [InlineData("[]", null)]
    public void A_refusal_from_the_script_is_recognised_even_though_the_http_status_is_200(string body, string? expected)
    {
        SheetsExporter.ReadRejection(body).ShouldBe(expected);
    }
}

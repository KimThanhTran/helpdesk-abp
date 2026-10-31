using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Helpdesk.Integrations;

/// <summary>The sending side of the Google Sheets export, with no database and no network.</summary>
public class SheetsExporterTests
{
    private const string Secret = "unit-test-secret";

    private static TicketSheetRow Row() => new()
    {
        Code = "HD-2026-000001",
        Category = "IT support",
        Status = "New",
        Priority = "Normal",
        Event = "Created",
        CreatedAt = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc),
        FirstResponseDueAt = new DateTime(2026, 11, 2, 17, 0, 0, DateTimeKind.Utc),
        ResolutionDueAt = new DateTime(2026, 11, 9, 17, 0, 0, DateTimeKind.Utc),
    };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public List<(Uri? Uri, string Body)> Requests { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add((request.RequestUri, request.Content is null ? "" : await request.Content.ReadAsStringAsync(token)));
            return _respond(request);
        }
    }

    private static SheetsExporter Exporter(FakeHandler handler, string? url = "https://script.example/exec", string? secret = Secret)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(SheetsExporter.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var options = Options.Create(new HelpdeskOptions { Sheets = new SheetsOptions { WebhookUrl = url, Secret = secret } });
        return new SheetsExporter(factory, options, NullLogger<SheetsExporter>.Instance);
    }

    // ------------------------------------------------------------------ the signature

    [Fact]
    public void The_body_carries_a_timestamp_the_exact_payload_and_an_hmac_the_script_can_recompute()
    {
        var body = SheetsExporter.Sign(Row(), Secret, 1_793_000_000);

        using var document = JsonDocument.Parse(body);
        var timestamp = document.RootElement.GetProperty("timestamp").GetString()!;
        var payload = document.RootElement.GetProperty("payload").GetString()!;
        var signature = document.RootElement.GetProperty("signature").GetString()!;

        timestamp.ShouldBe("1793000000");

        // exactly what the Apps Script does: HMAC-SHA256(secret, timestamp + "." + payload), hex encoded
        var expected = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        signature.ShouldBe(expected);

        using var inner = JsonDocument.Parse(payload);
        inner.RootElement.GetProperty("code").GetString().ShouldBe("HD-2026-000001");
        inner.RootElement.GetProperty("event").GetString().ShouldBe("Created");
    }

    [Fact]
    public void A_different_secret_or_a_changed_payload_gives_a_different_signature()
    {
        var one = JsonDocument.Parse(SheetsExporter.Sign(Row(), Secret, 1)).RootElement.GetProperty("signature").GetString();
        var otherSecret = JsonDocument.Parse(SheetsExporter.Sign(Row(), "other", 1)).RootElement.GetProperty("signature").GetString();
        var otherTime = JsonDocument.Parse(SheetsExporter.Sign(Row(), Secret, 2)).RootElement.GetProperty("signature").GetString();

        otherSecret.ShouldNotBe(one);
        otherTime.ShouldNotBe(one);
    }

    [Fact]
    public void The_secret_never_appears_in_the_body()
    {
        SheetsExporter.Sign(Row(), Secret, 1).ShouldNotContain(Secret);
    }

    // ------------------------------------------------------------------ sending

    [Fact]
    public async Task A_row_is_posted_to_the_configured_url_as_json()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Exporter(handler).SendAsync(Row());

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri!.ToString().ShouldBe("https://script.example/exec");
        JsonDocument.Parse(request.Body).RootElement.TryGetProperty("signature", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "secret")]
    [InlineData("https://script.example/exec", null)]
    [InlineData("", "secret")]
    [InlineData("https://script.example/exec", " ")]
    public async Task Nothing_is_sent_unless_both_the_url_and_the_secret_are_configured(string? url, string? secret)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var exporter = Exporter(handler, url, secret);

        exporter.Enabled.ShouldBeFalse();
        await exporter.SendAsync(Row());

        handler.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task A_failing_sheet_never_breaks_the_ticket_operation(HttpStatusCode status)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status));

        await Should.NotThrowAsync(() => Exporter(handler).SendAsync(Row()));
    }

    [Fact]
    public async Task A_network_error_or_timeout_never_breaks_the_ticket_operation()
    {
        var down = new FakeHandler(_ => throw new HttpRequestException("no route to host"));
        var slow = new FakeHandler(_ => throw new TaskCanceledException("timeout"));

        await Should.NotThrowAsync(() => Exporter(down).SendAsync(Row()));
        await Should.NotThrowAsync(() => Exporter(slow).SendAsync(Row()));
    }
}

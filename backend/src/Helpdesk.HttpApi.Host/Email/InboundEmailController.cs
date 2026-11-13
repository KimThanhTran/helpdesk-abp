using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Helpdesk.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Helpdesk.Email;

/// <summary>The mail provider's "inbound parse" webhook: a reply to a ticket mail becomes a comment.</summary>
[ApiController]
[Route("api/inbound-email")]
[AllowAnonymous] // the signature is the credential; there is no signed-in user
public class InboundEmailController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public sealed record Message(string? From, string? Subject, string? Text);

    /// <summary>
    /// Body: <c>{"from": "...", "subject": "...", "text": "..."}</c>, signed as described in <see cref="InboundSignature"/>.
    /// 202 when the reply was added; 200 with the reason when it was understood but not used (so the provider does not retry it);
    /// 401 for a bad signature; 404 while the feature is off.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> Post(
        [FromServices] IOptions<HelpdeskOptions> options, [FromServices] InboundEmailService service)
    {
        var email = options.Value.Email;
        if (!email.InboundEnabled)
        {
            return NotFound();
        }

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer);
        var body = buffer.ToArray();

        if (!InboundSignature.Verify(email.InboundSecret!, Request.Headers["X-Timestamp"], Request.Headers["X-Signature"], body, DateTimeOffset.UtcNow))
        {
            return Unauthorized();
        }

        Message? message;
        try
        {
            message = JsonSerializer.Deserialize<Message>(body, Json);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        if (message is null)
        {
            return BadRequest();
        }

        var result = await service.HandleAsync(message.From, message.Subject, message.Text);
        return result.Outcome == InboundOutcome.Accepted
            ? Accepted()
            : Ok(new { ignored = result.Outcome.ToString(), detail = result.Detail });
    }
}

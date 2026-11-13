using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Helpdesk.Email;

/// <summary>
/// The signature on what the mail provider posts to /api/inbound-email: HMAC-SHA256 of "timestamp.body" with the shared
/// secret, sent as <c>X-Signature: sha256=…</c> next to <c>X-Timestamp</c> (Unix seconds). Without it anyone who finds the
/// address could write comments as any requester; the timestamp keeps a recorded request from being replayed later.
/// </summary>
public static class InboundSignature
{
    public const int ToleranceSeconds = 300;

    public static string Sign(string secret, long timestamp, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var prefix = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + ".");
        var message = new byte[prefix.Length + body.Length];
        prefix.CopyTo(message, 0);
        body.CopyTo(message, prefix.Length);
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(message)).ToLowerInvariant();
    }

    public static bool Verify(string secret, string? timestamp, string? signature, byte[] body, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        if (Math.Abs(now.ToUnixTimeSeconds() - seconds) > ToleranceSeconds)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(secret, seconds, body));
        var given = Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant());
        return expected.Length == given.Length && CryptographicOperations.FixedTimeEquals(expected, given);
    }
}

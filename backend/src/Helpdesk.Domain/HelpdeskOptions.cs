namespace Helpdesk;

/// <summary>Bound from the "Helpdesk" configuration section.</summary>
public class HelpdeskOptions
{
    public const string SectionName = "Helpdesk";

    /// <summary>A resolved ticket closes by itself after this many days without an answer.</summary>
    public int AutoCloseAfterDays { get; set; } = 5;

    /// <summary>A requester can reopen a closed ticket for this many days; after that they open a new one.</summary>
    public int ReopenWindowDays { get; set; } = 10;

    /// <summary>Create demo roles, users and categories on startup. Never enable in production.</summary>
    public bool SeedDemoData { get; set; }

    /// <summary>With the demo data, also create a dozen finished tickets so the suggestions have history to learn from.</summary>
    public bool SeedDemoTickets { get; set; }

    /// <summary>
    /// How long the ticket suggestions' search index is kept before it is rebuilt. A new ticket shows up in the
    /// suggestions after at most this long. 0 rebuilds it on every request (what the tests use).
    /// </summary>
    public int TriageCacheSeconds { get; set; } = 15;

    public SheetsOptions Sheets { get; set; } = new();

    public EmailOptions Email { get; set; } = new();
}

/// <summary>
/// Optional e-mail: the requester hears about what happens to their ticket, and can answer by replying.
/// Off until a host and a sender address are set.
/// </summary>
public class EmailOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    /// <summary>Encrypt the connection (STARTTLS on 587, TLS on 465). Turn off only for a local test server.</summary>
    public bool UseSsl { get; set; } = true;

    public string? User { get; set; }

    /// <summary>Keep it in an environment variable, not in a file.</summary>
    public string? Password { get; set; }

    /// <summary>The address the mails come from, and where replies go.</summary>
    public string? From { get; set; }

    /// <summary>The address of the web UI, for the link in each mail. Left out when empty.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>The language of the mails: "en" or "vi". A mail is written once, not per recipient.</summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Shared secret that signs what the mail provider posts to /api/inbound-email (a reply to a ticket).
    /// Replies are not accepted at all while it is empty.
    /// </summary>
    public string? InboundSecret { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);

    public bool InboundEnabled => !string.IsNullOrWhiteSpace(InboundSecret);
}

/// <summary>Optional export of ticket summaries to a Google Sheet through an Apps Script web app.</summary>
public class SheetsOptions
{
    public string? WebhookUrl { get; set; }

    /// <summary>Shared secret used to sign each request. Keep it in an environment variable, not in a file.</summary>
    public string? Secret { get; set; }

    /// <summary>Ticket titles can contain personal data, so they are left out unless you turn this on.</summary>
    public bool IncludeTitle { get; set; }

    /// <summary>Requester and assignee names are left out unless you turn this on.</summary>
    public bool IncludePeople { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(WebhookUrl) && !string.IsNullOrWhiteSpace(Secret);
}

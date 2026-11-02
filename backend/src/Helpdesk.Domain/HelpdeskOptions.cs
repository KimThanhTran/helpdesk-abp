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

    public SheetsOptions Sheets { get; set; } = new();
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

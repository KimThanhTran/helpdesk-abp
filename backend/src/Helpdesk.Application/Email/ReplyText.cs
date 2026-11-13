using System;
using System.Linq;
using System.Text.RegularExpressions;
using Helpdesk.Tickets;

namespace Helpdesk.Email;

/// <summary>Takes what the person wrote out of an e-mail reply: not the quoted message below it, nor the signature.</summary>
public static partial class ReplyText
{
    // where a mail client starts quoting the earlier message, in English and Vietnamese
    [GeneratedRegex(@"^\s*(On .+wrote:|Vào .+(đã )?viết:|-{2,}\s*Original Message\s*-{2,}|_{5,}|From:\s.+|Từ:\s.+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex QuoteStart();

    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var kept = new System.Collections.Generic.List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            if (QuoteStart().IsMatch(line) || line == "--" || line == "-- ")
            {
                break; // everything after this is the quoted message or the signature
            }

            if (!line.TrimStart().StartsWith('>'))
            {
                kept.Add(line);
            }
        }

        var cleaned = string.Join("\n", kept).Trim();
        return cleaned.Length > TicketConsts.MaxCommentLength ? cleaned[..TicketConsts.MaxCommentLength] : cleaned;
    }
}

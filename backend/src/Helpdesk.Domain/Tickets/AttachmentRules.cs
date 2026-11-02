using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Helpdesk.Tickets;

/// <summary>What may be attached to a ticket, decided from the bytes themselves.</summary>
public static class AttachmentRules
{
    public readonly record struct ImageKind(string ContentType, string Extension);

    private static readonly ImageKind Png = new("image/png", ".png");
    private static readonly ImageKind Jpeg = new("image/jpeg", ".jpg");
    private static readonly ImageKind Webp = new("image/webp", ".webp");

    /// <summary>
    /// Recognises PNG, JPEG and WebP by their first bytes. The file name and the content type the caller sent are
    /// never trusted: a script renamed to photo.png is not an image and is refused.
    /// </summary>
    public static ImageKind? Sniff(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return Png;
        }

        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return Jpeg;
        }

        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
        {
            return Webp;
        }

        return null;
    }

    /// <summary>
    /// A name that is safe to show and to put in a download header: no folders, no control or reserved characters,
    /// a bounded length, and the extension that matches the real type.
    /// </summary>
    public static string SafeFileName(string? name, ImageKind kind)
    {
        var leaf = (name ?? string.Empty).Replace('\\', '/').Split('/').Last();
        var stem = Path.GetFileNameWithoutExtension(leaf);

        var clean = new StringBuilder();
        foreach (var c in stem)
        {
            if (!char.IsControl(c) && "\"<>|:*?".IndexOf(c) < 0)
            {
                clean.Append(c);
            }
        }

        var result = clean.ToString().Trim().TrimEnd('.');
        if (result.Length == 0)
        {
            result = "image";
        }

        if (result.Length > TicketConsts.MaxFileNameLength)
        {
            result = result[..TicketConsts.MaxFileNameLength];
        }

        return result + kind.Extension;
    }
}

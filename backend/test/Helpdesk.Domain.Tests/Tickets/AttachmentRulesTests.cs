using System.Text;
using Shouldly;
using Xunit;

namespace Helpdesk.Tickets;

public class AttachmentRulesTests
{
    public static byte[] Png() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };

    public static byte[] Jpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46 };

    public static byte[] Webp() => Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");

    [Fact]
    public void Recognises_png_jpeg_and_webp_by_their_first_bytes()
    {
        AttachmentRules.Sniff(Png())!.Value.ContentType.ShouldBe("image/png");
        AttachmentRules.Sniff(Jpeg())!.Value.ContentType.ShouldBe("image/jpeg");
        AttachmentRules.Sniff(Webp())!.Value.ContentType.ShouldBe("image/webp");
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("GIF89a....")]
    [InlineData("MZ this is an executable")]
    [InlineData("%PDF-1.7")]
    [InlineData("")]
    [InlineData("RIFF....WAVE")]
    public void Refuses_everything_else_whatever_it_is_called(string text)
    {
        AttachmentRules.Sniff(Encoding.ASCII.GetBytes(text)).ShouldBeNull();
    }

    [Fact]
    public void Refuses_a_file_that_is_too_short_to_be_what_it_claims()
    {
        AttachmentRules.Sniff(new byte[] { 0x89, 0x50, 0x4E }).ShouldBeNull();
        AttachmentRules.Sniff(new byte[] { 0xFF, 0xD8 }).ShouldBeNull();
        AttachmentRules.Sniff(Encoding.ASCII.GetBytes("RIFF....WEB")).ShouldBeNull();
    }

    [Theory]
    [InlineData("photo.png", "photo.png")]
    [InlineData("holiday.jpeg", "holiday.png")] // the extension always follows the real type
    [InlineData("run-me.exe", "run-me.png")]
    [InlineData("..\\..\\windows\\system32\\evil.png", "evil.png")]
    [InlineData("../../etc/passwd", "passwd.png")]
    [InlineData("a<script>b|c:d*e?f\"g.png", "ascriptbcdefg.png")]
    [InlineData("  spaced name  .png", "spaced name.png")]
    [InlineData("", "image.png")]
    [InlineData(null, "image.png")]
    [InlineData("....png", "image.png")]
    public void A_file_name_is_made_safe_to_show_and_to_put_in_a_header(string? given, string expected)
    {
        AttachmentRules.SafeFileName(given, AttachmentRules.Sniff(Png())!.Value).ShouldBe(expected);
    }

    [Fact]
    public void Control_characters_and_line_breaks_never_survive_in_a_name()
    {
        var name = AttachmentRules.SafeFileName("a\r\nSet-Cookie: x\0.png", AttachmentRules.Sniff(Png())!.Value);

        name.ShouldNotContain("\r");
        name.ShouldNotContain("\n");
        name.ShouldNotContain("\0");
    }

    [Fact]
    public void A_very_long_name_is_cut_and_keeps_the_extension()
    {
        var name = AttachmentRules.SafeFileName(new string('x', 500) + ".png", AttachmentRules.Sniff(Jpeg())!.Value);

        name.Length.ShouldBe(TicketConsts.MaxFileNameLength + 4);
        name.ShouldEndWith(".jpg");
    }
}

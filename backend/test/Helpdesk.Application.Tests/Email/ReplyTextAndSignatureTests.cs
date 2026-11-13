using System;
using System.Text;
using Helpdesk.Tickets;
using Shouldly;
using Xunit;

namespace Helpdesk.Email;

public class ReplyTextTests
{
    [Theory]
    [InlineData("Thanks, that worked.", "Thanks, that worked.")]
    [InlineData("It is a LaserJet.\n\nOn Mon, 2 Nov 2026 at 09:00, Helpdesk <helpdesk@example.test> wrote:\n> Please send the model", "It is a LaserJet.")]
    [InlineData("Đã gửi.\n\nVào Th 2, 2 thg 11, 2026 Helpdesk đã viết:\n> Vui lòng gửi mã máy", "Đã gửi.")]
    [InlineData("Fine.\n\n-----Original Message-----\nFrom: Helpdesk\nSent: Monday", "Fine.")]
    [InlineData("Fine.\n\n________________________________\nFrom: Helpdesk", "Fine.")]
    [InlineData("Fine.\nFrom: Helpdesk <h@x.test>\nSent: Monday\nSubject: Re: x", "Fine.")]
    [InlineData("Line one\n> quoted\nLine two", "Line one\nLine two")]
    [InlineData("Answer\n-- \nAlice Example\nExample Ltd", "Answer")]
    [InlineData("Answer\r\nsecond line\r\n", "Answer\nsecond line")]
    [InlineData("> only a quote", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("   \n \n", "")]
    public void Keeps_what_the_person_wrote_and_drops_the_quote_and_the_signature(string? mail, string expected)
    {
        ReplyText.Clean(mail).ShouldBe(expected);
    }

    [Fact]
    public void A_very_long_reply_is_cut_to_what_a_comment_may_hold()
    {
        ReplyText.Clean(new string('x', TicketConsts.MaxCommentLength + 500)).Length.ShouldBe(TicketConsts.MaxCommentLength);
    }

    [Fact]
    public void The_word_wrote_in_the_middle_of_a_sentence_is_not_a_quote_marker()
    {
        ReplyText.Clean("My colleague wrote: it still fails.").ShouldBe("My colleague wrote: it still fails.");
    }
}

public class InboundSignatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"from\":\"a@x.test\",\"subject\":\"Re: [HD-2026-000001]\",\"text\":\"hi\"}");

    private static string Sign(DateTimeOffset at, byte[]? body = null) => InboundSignature.Sign("s3cret", at.ToUnixTimeSeconds(), body ?? Body);

    [Fact]
    public void A_correct_signature_from_a_moment_ago_is_accepted()
    {
        InboundSignature.Verify("s3cret", Now.AddSeconds(-30).ToUnixTimeSeconds().ToString(), Sign(Now.AddSeconds(-30)), Body, Now).ShouldBeTrue();
    }

    [Fact]
    public void A_changed_body_a_wrong_secret_or_a_changed_time_is_refused()
    {
        var signature = Sign(Now);
        var stamp = Now.ToUnixTimeSeconds().ToString();

        InboundSignature.Verify("s3cret", stamp, signature, Encoding.UTF8.GetBytes("{\"from\":\"b@x.test\"}"), Now).ShouldBeFalse();
        InboundSignature.Verify("other", stamp, signature, Body, Now).ShouldBeFalse();
        InboundSignature.Verify("s3cret", (Now.ToUnixTimeSeconds() + 1).ToString(), signature, Body, Now).ShouldBeFalse();
    }

    [Fact]
    public void A_recorded_request_cannot_be_replayed_later_or_from_the_future()
    {
        var old = Now.AddSeconds(-(InboundSignature.ToleranceSeconds + 1));
        var future = Now.AddSeconds(InboundSignature.ToleranceSeconds + 1);

        InboundSignature.Verify("s3cret", old.ToUnixTimeSeconds().ToString(), Sign(old), Body, Now).ShouldBeFalse();
        InboundSignature.Verify("s3cret", future.ToUnixTimeSeconds().ToString(), Sign(future), Body, Now).ShouldBeFalse();
        InboundSignature.Verify("s3cret", old.AddSeconds(2).ToUnixTimeSeconds().ToString(), Sign(old.AddSeconds(2)), Body, Now).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, "sha256=00")]
    [InlineData("", "sha256=00")]
    [InlineData("not a number", "sha256=00")]
    [InlineData("-5", "sha256=00")]
    [InlineData("1793521200", null)]
    [InlineData("1793521200", "")]
    [InlineData("1793521200", "nonsense")]
    public void Missing_or_malformed_headers_are_refused_without_an_error(string? timestamp, string? signature)
    {
        InboundSignature.Verify("s3cret", timestamp, signature, Body, Now).ShouldBeFalse();
    }

    [Fact]
    public void Without_a_secret_nothing_is_ever_accepted()
    {
        InboundSignature.Verify(string.Empty, Now.ToUnixTimeSeconds().ToString(), InboundSignature.Sign(string.Empty, Now.ToUnixTimeSeconds(), Body), Body, Now).ShouldBeFalse();
    }

    [Fact]
    public void The_signature_may_be_written_in_upper_case()
    {
        InboundSignature.Verify("s3cret", Now.ToUnixTimeSeconds().ToString(), Sign(Now).ToUpperInvariant(), Body, Now).ShouldBeTrue();
    }
}

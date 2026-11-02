using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Tickets;

public abstract class TicketAttachmentTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ITicketAttachmentAppService _attachments;

    protected TicketAttachmentTests()
    {
        _attachments = GetRequiredService<ITicketAttachmentAppService>();
    }

    private static byte[] Png(int length = 64)
    {
        var bytes = new byte[length];
        AttachmentRulesTests.Png().CopyTo(bytes, 0);
        return bytes;
    }

    private static IRemoteStreamContent File(byte[] bytes, string name = "screen.png") =>
        new RemoteStreamContent(new MemoryStream(bytes), name, "image/png");

    private async Task<AttachmentDto> UploadAs(string user, Guid ticketId, byte[]? bytes = null, string name = "screen.png",
        CommentVisibility visibility = CommentVisibility.Public)
    {
        using (As(user))
        {
            return await _attachments.UploadAsync(ticketId, visibility, File(bytes ?? Png(), name));
        }
    }

    private async Task<string> ErrorCode(Func<Task> action) => (await Should.ThrowAsync<BusinessException>(action)).Code!;

    [Fact]
    public async Task The_requester_attaches_an_image_and_it_comes_back_byte_for_byte()
    {
        var ticket = await CreateAs(Alice);
        var bytes = Png(300);
        bytes[200] = 42;

        var saved = await UploadAs(Alice, ticket.Id, bytes, "screen.png");

        saved.ContentType.ShouldBe("image/png");
        saved.Size.ShouldBe(300);
        saved.FileName.ShouldBe("screen.png");
        saved.Uploader.Name.ShouldBe(Alice);

        using (As(Alice))
        {
            var list = await _attachments.GetListAsync(ticket.Id);
            list.Items.Select(a => a.Id).ShouldBe(new[] { saved.Id });

            var content = await _attachments.GetContentAsync(saved.Id);
            content.ContentType.ShouldBe("image/png");
            using var copy = new MemoryStream();
            await content.GetStream().CopyToAsync(copy);
            copy.ToArray().ShouldBe(bytes);
        }
    }

    [Fact]
    public async Task An_agent_can_see_and_open_what_the_requester_attached()
    {
        var ticket = await CreateAs(Alice);
        var saved = await UploadAs(Alice, ticket.Id);

        using (As(Agent))
        {
            (await _attachments.GetListAsync(ticket.Id)).Items.Single().Id.ShouldBe(saved.Id);
            (await _attachments.GetContentAsync(saved.Id)).ContentType.ShouldBe("image/png");
        }
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused_even_if_it_is_named_and_labelled_like_one()
    {
        var ticket = await CreateAs(Alice);
        var script = Encoding.UTF8.GetBytes("<html><script>alert(document.cookie)</script></html>");

        var code = await ErrorCode(() => UploadAs(Alice, ticket.Id, script, "photo.png"));

        code.ShouldBe(HelpdeskDomainErrorCodes.AttachmentBadType);
        using (As(Alice))
        {
            (await _attachments.GetListAsync(ticket.Id)).Items.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task The_name_is_cleaned_and_the_extension_follows_the_real_type()
    {
        var ticket = await CreateAs(Alice);

        var saved = await UploadAs(Alice, ticket.Id, AttachmentRulesTests.Jpeg(), "..\\..\\x<script>.exe");

        saved.FileName.ShouldBe("xscript.jpg");
        saved.ContentType.ShouldBe("image/jpeg");
    }

    [Fact]
    public async Task An_empty_file_is_refused()
    {
        var ticket = await CreateAs(Alice);

        (await ErrorCode(() => UploadAs(Alice, ticket.Id, Array.Empty<byte>()))).ShouldBe(HelpdeskDomainErrorCodes.AttachmentEmpty);
    }

    [Fact]
    public async Task A_file_over_the_size_limit_is_refused_and_exactly_the_limit_is_accepted()
    {
        var ticket = await CreateAs(Alice);

        (await ErrorCode(() => UploadAs(Alice, ticket.Id, Png(TicketConsts.MaxAttachmentBytes + 1))))
            .ShouldBe(HelpdeskDomainErrorCodes.AttachmentTooLarge);

        var saved = await UploadAs(Alice, ticket.Id, Png(TicketConsts.MaxAttachmentBytes));
        saved.Size.ShouldBe(TicketConsts.MaxAttachmentBytes);
    }

    [Fact]
    public async Task A_ticket_holds_at_most_five_attachments()
    {
        var ticket = await CreateAs(Alice);
        for (var i = 0; i < TicketConsts.MaxAttachmentsPerTicket; i++)
        {
            await UploadAs(Alice, ticket.Id);
        }

        (await ErrorCode(() => UploadAs(Alice, ticket.Id))).ShouldBe(HelpdeskDomainErrorCodes.AttachmentLimit);
    }

    [Fact]
    public async Task Someone_elses_ticket_and_its_files_are_not_found()
    {
        var ticket = await CreateAs(Alice);
        var saved = await UploadAs(Alice, ticket.Id);

        using (As(Bob))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => _attachments.GetListAsync(ticket.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _attachments.GetContentAsync(saved.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _attachments.UploadAsync(ticket.Id, CommentVisibility.Public, File(Png())));
        }

        await Should.ThrowAsync<EntityNotFoundException>(() => UploadAs(Alice, Guid.NewGuid()));
        using (As(Alice))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => _attachments.GetContentAsync(Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task An_internal_attachment_is_for_agents_and_the_requester_cannot_tell_it_exists()
    {
        var ticket = await OpenTicket();
        var secret = await UploadAs(Agent, ticket.Id, Png(), "internal-screenshot.png", CommentVisibility.Internal);
        var visible = await UploadAs(Agent, ticket.Id, Png(), "public-screenshot.png");

        using (As(Alice))
        {
            var list = await _attachments.GetListAsync(ticket.Id);
            list.Items.Select(a => a.Id).ShouldBe(new[] { visible.Id });
            JsonSerializer.Serialize(list).ShouldNotContain("internal-screenshot");

            await Should.ThrowAsync<EntityNotFoundException>(() => _attachments.GetContentAsync(secret.Id));
        }

        using (As(Agent))
        {
            (await _attachments.GetListAsync(ticket.Id)).Items.Count.ShouldBe(2);
            (await _attachments.GetContentAsync(secret.Id)).FileName.ShouldBe("internal-screenshot.png");
        }
    }

    [Fact]
    public async Task A_requester_cannot_add_an_internal_attachment()
    {
        var ticket = await OpenTicket();

        (await ErrorCode(() => UploadAs(Alice, ticket.Id, visibility: CommentVisibility.Internal)))
            .ShouldBe(HelpdeskDomainErrorCodes.NotAllowed);
    }

    [Fact]
    public async Task Files_follow_the_comment_rules_so_a_closed_ticket_takes_none()
    {
        var closed = await ClosedTicket();

        (await ErrorCode(() => UploadAs(Alice, closed.Id))).ShouldBe(HelpdeskDomainErrorCodes.InvalidTransition);
    }

    [Fact]
    public async Task A_list_never_carries_the_bytes()
    {
        var ticket = await CreateAs(Alice);
        await UploadAs(Alice, ticket.Id, Png(2000));

        using (As(Alice))
        {
            var json = JsonSerializer.Serialize(await _attachments.GetListAsync(ticket.Id));
            json.ShouldNotContain("Content\"");
            json.Length.ShouldBeLessThan(1000);
        }
    }
}

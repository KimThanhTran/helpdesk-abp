using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Tickets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Xunit;

namespace Helpdesk.Email;

/// <summary>Collects what would have been mailed, so tests can read it without a mail server.</summary>
public class MailRecorder : ISingletonDependency
{
    private readonly ConcurrentQueue<Mail> _mails = new();

    public IReadOnlyList<Mail> Mails => _mails.ToList();

    public Func<Task>? Fail { get; set; }

    public void Add(Mail mail) => _mails.Enqueue(mail);

    public void Reset() => _mails.Clear();

    public record Mail(string To, string Subject, string Body);
}

[Dependency(ServiceLifetime.Transient, ReplaceServices = true)]
[ExposeServices(typeof(IMailSender))]
public class RecordingMailSender : IMailSender
{
    private readonly MailRecorder _recorder;

    public RecordingMailSender(MailRecorder recorder)
    {
        _recorder = recorder;
    }

    public async Task SendAsync(string to, string subject, string body)
    {
        if (_recorder.Fail is { } fail)
        {
            await fail();
        }

        _recorder.Add(new MailRecorder.Mail(to, subject, body));
    }
}

public abstract class MailTests<TStartupModule> : TicketTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private IReadOnlyList<MailRecorder.Mail> Mails => GetRequiredService<MailRecorder>().Mails;

    /// <summary>Forgets what was recorded so far, so a test reads only what its own steps produced.</summary>
    private void ClearMails() => GetRequiredService<MailRecorder>().Reset();

    private EmailOptions Options => GetRequiredService<IOptions<HelpdeskOptions>>().Value.Email;

    protected MailTests()
    {
        // the tests switch it on for themselves; a fresh application starts with it off, like a fresh install
        Options.Host = "smtp.test";
        Options.From = "helpdesk@example.test";
        Options.PublicUrl = "http://localhost:3000";
        Options.Language = "en";
        Options.InboundSecret = null;
    }

    private string EmailOf(string user) => WithUnitOfWorkAsync(async () =>
        (await GetRequiredService<IIdentityUserRepository>().FindByNormalizedUserNameAsync(user.ToUpperInvariant()))!.Email).GetAwaiter().GetResult();

    [Fact]
    public async Task A_new_ticket_is_confirmed_to_its_requester_with_the_code_in_the_subject_and_a_link()
    {
        var ticket = await CreateAs(Alice, "Printer jams");

        var mail = Mails.Single();
        mail.To.ShouldBe(EmailOf(Alice));
        mail.Subject.ShouldBe($"[{ticket.Code}] We received your ticket");
        mail.Body.ShouldContain("Hello alice,");
        mail.Body.ShouldContain($"{ticket.Code}: Printer jams");
        mail.Body.ShouldContain($"http://localhost:3000/#/tickets/{ticket.Id}");
        mail.Body.ShouldNotContain("Reply to this email"); // replies are not accepted until the inbound secret is set
    }

    [Fact]
    public async Task The_mail_offers_a_reply_once_replies_are_accepted()
    {
        Options.InboundSecret = "secret";

        await CreateAs(Alice);

        Mails.Single().Body.ShouldContain("Reply to this email to add a comment to the ticket.");
    }

    [Fact]
    public async Task Waiting_and_resolving_tell_the_requester_what_the_agent_wrote()
    {
        var ticket = await OpenTicket();
        ClearMails();
        using (As(Agent))
        {
            await Tickets.WaitAsync(ticket.Id, new TicketReasonDto { Reason = "Please send the model number" });
            await Tickets.ResumeAsync(ticket.Id);
            await Tickets.ResolveAsync(ticket.Id, new ResolveTicketDto { Resolution = "Replaced the roller" });
        }

        Mails.Select(m => m.Subject).ShouldBe(new[] { $"[{ticket.Code}] We need your reply", $"[{ticket.Code}] Your ticket was resolved" });
        Mails[0].Body.ShouldContain("Please send the model number");
        Mails[1].Body.ShouldContain("Replaced the roller");
        Mails.ShouldAllBe(m => m.To == EmailOf(Alice));
    }

    [Fact]
    public async Task What_the_requester_does_is_not_mailed_back_to_them()
    {
        var resolved = await ResolvedTicket();
        ClearMails();

        using (As(Alice))
        {
            await Tickets.ConfirmAsync(resolved.Id);
            await Should.ThrowAsync<Volo.Abp.BusinessException>(() => Tickets.AddCommentAsync(resolved.Id, new CreateCommentDto { Body = "Thanks!" })); // closed: refused
        }

        Mails.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_agents_public_reply_is_mailed_but_an_internal_note_never_is()
    {
        var ticket = await OpenTicket();
        ClearMails();
        using (As(Agent))
        {
            await Tickets.AddCommentAsync(ticket.Id, new CreateCommentDto { Body = "Customer is on the old driver. INTERNAL-7f3a", Visibility = CommentVisibility.Internal });
            await Tickets.AddCommentAsync(ticket.Id, new CreateCommentDto { Body = "Try restarting the spooler." });
        }

        var mail = Mails.Single();
        mail.Subject.ShouldBe($"[{ticket.Code}] New reply on your ticket");
        mail.Body.ShouldContain("agent1 wrote");
        mail.Body.ShouldContain("Try restarting the spooler.");
        Mails.ShouldAllBe(m => !m.Body.Contains("INTERNAL-7f3a"));
    }

    [Fact]
    public async Task The_requesters_own_comment_is_not_mailed_to_them()
    {
        var ticket = await CreateAs(Alice);
        ClearMails();

        using (As(Alice))
        {
            await Tickets.AddCommentAsync(ticket.Id, new CreateCommentDto { Body = "One more detail" });
        }

        Mails.ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_is_sent_while_mail_is_off_and_a_broken_mail_server_never_breaks_the_ticket()
    {
        Options.Host = null;
        var quiet = await CreateAs(Alice, "No mail");
        Mails.ShouldBeEmpty();

        Options.Host = "smtp.test";
        GetRequiredService<MailRecorder>().Fail = () => throw new InvalidOperationException("the mail server is down");
        var saved = await CreateAs(Alice, "Mail server down");

        (await Reload(saved.Id, Alice)).Code.ShouldBe(saved.Code);
        Mails.ShouldBeEmpty();
        quiet.Code.ShouldNotBe(saved.Code);
    }

    [Fact]
    public async Task The_mail_is_in_the_configured_language()
    {
        Options.Language = "vi";

        var ticket = await CreateAs(Alice);

        Mails.Single().Subject.ShouldBe($"[{ticket.Code}] Chúng tôi đã nhận được phiếu của bạn");
        Mails.Single().Body.ShouldContain("Xin chào alice,");
    }

    [Fact]
    public async Task A_requester_with_no_address_is_skipped_quietly()
    {
        // Identity refuses to save an empty address, so the test clears it behind its back, as an import from another system could
        await WithUnitOfWorkAsync(async () =>
        {
            var users = GetRequiredService<IIdentityUserRepository>();
            var alice = await users.FindByNormalizedUserNameAsync(Alice.ToUpperInvariant());
            typeof(IdentityUser).GetProperty(nameof(IdentityUser.Email))!.SetValue(alice, string.Empty);
            await users.UpdateAsync(alice!, autoSave: true);
        });

        await CreateAs(Alice);

        Mails.ShouldBeEmpty();
    }
}

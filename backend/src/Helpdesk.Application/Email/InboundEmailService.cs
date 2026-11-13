using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Helpdesk.Tickets;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;

namespace Helpdesk.Email;

public enum InboundOutcome
{
    /// <summary>The reply is now a comment on the ticket.</summary>
    Accepted,

    /// <summary>No ticket code in the subject, or no such ticket.</summary>
    NoTicket,

    /// <summary>The sender is not the person who raised the ticket.</summary>
    WrongSender,

    /// <summary>Nothing was left once the quoted message and the signature were removed.</summary>
    Empty,

    /// <summary>The ticket's rules refused the comment (for example, it is closed).</summary>
    Refused,
}

public sealed record InboundResult(InboundOutcome Outcome, string? Detail = null);

/// <summary>
/// Turns a reply e-mail into a comment from the requester. It does not trust the mail: the sender's address must be
/// the requester's, and the comment goes through the ordinary ticket service as that person, so every rule that applies
/// to them (the status, the role) applies here too, and a reply on a waiting ticket resumes its SLA clock.
/// A From address can be forged; the provider that posts the mail is expected to have checked SPF/DKIM.
/// </summary>
public class InboundEmailService : ITransientDependency
{
    private static readonly Regex Code = new(@"HD-\d{4}-\d{6}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IRepository<Ticket, Guid> _tickets;
    private readonly IIdentityUserRepository _users;
    private readonly IdentityUserManager _userManager;
    private readonly ICurrentPrincipalAccessor _principal;
    private readonly ITicketAppService _ticketService;

    public InboundEmailService(
        IRepository<Ticket, Guid> tickets,
        IIdentityUserRepository users,
        IdentityUserManager userManager,
        ICurrentPrincipalAccessor principal,
        ITicketAppService ticketService)
    {
        _tickets = tickets;
        _users = users;
        _userManager = userManager;
        _principal = principal;
        _ticketService = ticketService;
    }

    public async Task<InboundResult> HandleAsync(string? from, string? subject, string? text)
    {
        var match = Code.Match(subject ?? string.Empty);
        if (!match.Success)
        {
            return new InboundResult(InboundOutcome.NoTicket);
        }

        var code = match.Value.ToUpperInvariant();
        var ticket = await _tickets.FindAsync(candidate => candidate.Code == code);
        if (ticket is null)
        {
            return new InboundResult(InboundOutcome.NoTicket);
        }

        var requester = await _users.FindAsync(ticket.RequesterId);
        if (requester is null || !SameAddress(from, requester.Email))
        {
            return new InboundResult(InboundOutcome.WrongSender);
        }

        var body = ReplyText.Clean(text);
        if (body.Length == 0)
        {
            return new InboundResult(InboundOutcome.Empty);
        }

        using (_principal.Change(await PrincipalAsync(requester)))
        {
            try
            {
                await _ticketService.AddCommentAsync(
                    ticket.Id, new CreateCommentDto { Body = body, Visibility = CommentVisibility.Public });
            }
            catch (BusinessException refusal)
            {
                return new InboundResult(InboundOutcome.Refused, refusal.Code);
            }
        }

        return new InboundResult(InboundOutcome.Accepted);
    }

    /// <summary>"Alice &lt;alice@example.com&gt;" and "ALICE@example.com" are the same address.</summary>
    public static bool SameAddress(string? from, string? known) =>
        MailAddress.TryCreate(from, out var sender) && !string.IsNullOrWhiteSpace(known)
        && string.Equals(sender.Address, known.Trim(), StringComparison.OrdinalIgnoreCase);

    private async Task<ClaimsPrincipal> PrincipalAsync(IdentityUser user)
    {
        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, user.Id.ToString()),
            new(AbpClaimTypes.UserName, user.UserName),
        };
        claims.AddRange((await _userManager.GetRolesAsync(user)).Select(role => new Claim(AbpClaimTypes.Role, role)));

        // an identity with an authentication type counts as signed in, as a real token would
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "InboundEmail"));
    }
}

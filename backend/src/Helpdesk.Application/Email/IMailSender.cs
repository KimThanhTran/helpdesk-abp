using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Helpdesk.Email;

/// <summary>Sends one plain-text mail. Tests replace it with one that only records.</summary>
public interface IMailSender
{
    Task SendAsync(string to, string subject, string body);
}

/// <summary>
/// Plain SMTP with the built-in client, so there is no extra package. It supports STARTTLS and TLS and a user name;
/// that is enough for the usual providers. It waits for the server, so a very slow mail server slows the call that
/// caused the mail: the timeout keeps that to ten seconds (a queue with retries is the next step).
/// </summary>
public class SmtpMailSender : IMailSender, ITransientDependency
{
    private readonly EmailOptions _options;

    public SmtpMailSender(IOptions<HelpdeskOptions> options)
    {
        _options = options.Value.Email;
    }

    public async Task SendAsync(string to, string subject, string body)
    {
        using var message = new MailMessage(new MailAddress(_options.From!), new MailAddress(to))
        {
            Subject = subject,
            Body = body,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 10_000,
        };
        if (!string.IsNullOrEmpty(_options.User))
        {
            client.Credentials = new NetworkCredential(_options.User, _options.Password);
        }

        await client.SendMailAsync(message);
    }
}

using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace TenantPlatform.Web.Email;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(
        IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(
        string toAddress,
        string? replyToAddress,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        using var message =
            new MailMessage
            {
                From =
                    new MailAddress(
                        _options.FromAddress,
                        _options.FromName),

                Subject =
                    subject,

                Body =
                    body,

                IsBodyHtml =
                    false
            };

        message.To.Add(
            new MailAddress(toAddress));

        if (!string.IsNullOrWhiteSpace(
            replyToAddress))
        {
            message.ReplyToList.Add(
                new MailAddress(replyToAddress));
        }

        using var client =
            new SmtpClient(
                _options.Host,
                _options.Port)
            {
                EnableSsl =
                    _options.EnableSsl,

                Credentials =
                    new NetworkCredential(
                        _options.Username,
                        _options.Password)
            };

        cancellationToken
            .ThrowIfCancellationRequested();

        await client.SendMailAsync(message);
    }
}


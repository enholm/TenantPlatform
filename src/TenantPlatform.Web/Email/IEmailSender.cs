namespace TenantPlatform.Web.Email;

public interface IEmailSender
{
    Task SendAsync(
        string toAddress,
        string? replyToAddress,
        string subject,
        string body,
        string? inReplyToMessageId = null,
        string? references = null,
        CancellationToken cancellationToken = default);
}

namespace TenantPlatform.Web.Email;

public class InboundEmailMessage
{
    public string ExternalMessageId { get; init; } = string.Empty;

    public string FromAddress { get; init; } = string.Empty;

    public List<string> ToAddresses { get; init; } = [];

    public string Subject { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; init; }
}


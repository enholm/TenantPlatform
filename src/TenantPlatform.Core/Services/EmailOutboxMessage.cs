namespace TenantPlatform.Core.Services;

public class EmailOutboxMessage
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid ServiceRequestId { get; set; }

    public string ToAddress { get; set; } = string.Empty;

    public string? ReplyToAddress { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    // Message-ID of the incoming email this message is replying to.
    public string? InReplyToMessageId { get; set; }

    // References header used to preserve the existing email thread.
    public string? References { get; set; }

    public EmailOutboxStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastError { get; set; }
}


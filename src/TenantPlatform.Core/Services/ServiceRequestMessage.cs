namespace TenantPlatform.Core.Services;

public class ServiceRequestMessage
{
    public Guid Id { get; set; }

    public Guid ServiceRequestId { get; set; }

    public ServiceRequestEventType? EventType { get; set; }

    public ServiceRequestMessageDirection Direction { get; set; }

    public ServiceRequestMessageType Type { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public string? FromAddress { get; set; }

    public string? ToAddress { get; set; }

    public string? Subject { get; set; }

    public string? Body { get; set; }

    // Message-ID for this email as assigned by the external mail system.
    public string? ExternalMessageId { get; set; }

    // Optional external conversation/thread identifier.
    public string? ExternalThreadId { get; set; }

    // Message-ID of the email this message is replying to.
    public string? InReplyToMessageId { get; set; }

    // Full References header used to preserve the email thread.
    public string? References { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public enum ServiceRequestMessageDirection
{
    Outbound = 1,
    Inbound = 2,
    Internal = 3
}

public enum ServiceRequestMessageType
{
    Email = 1,
    Comment = 2,
    System = 3
}

public enum ServiceRequestEventType
{
    Created = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Assigned = 5,
    SentToProvider = 6,
    Completed = 7,
    Cancelled = 8,
    InProgress = 9
}


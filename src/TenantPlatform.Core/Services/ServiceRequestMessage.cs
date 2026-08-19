namespace TenantPlatform.Core.Services;

public class ServiceRequestMessage
{
    public Guid Id { get; set; }

    public Guid ServiceRequestId { get; set; }

    public ServiceRequestMessageDirection Direction { get; set; }

    public ServiceRequestMessageType Type { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public string? FromAddress { get; set; }

    public string? ToAddress { get; set; }

    public string? Subject { get; set; }

    public string? Body { get; set; }

    public string? ExternalMessageId { get; set; }

    public string? ExternalThreadId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public enum ServiceRequestMessageDirection
{
    Outbound = 1,
    Inbound = 2
}

public enum ServiceRequestMessageType
{
    Email = 1,
    Comment = 2,
    System = 3
}


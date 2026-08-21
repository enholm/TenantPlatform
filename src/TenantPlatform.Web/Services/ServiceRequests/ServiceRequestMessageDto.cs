using TenantPlatform.Core.Services;

namespace TenantPlatform.Web.Services.ServiceRequests;

public class ServiceRequestMessageDto
{
    public Guid Id { get; init; }

    public ServiceRequestMessageDirection Direction { get; init; }

    public ServiceRequestMessageType Type { get; init; }

    public ServiceRequestEventType? EventType { get; init; }

    public Guid? CreatedByUserId { get; init; }

    public string? FromAddress { get; init; }

    public string? ToAddress { get; init; }

    public string? Subject { get; init; }

    public string? Body { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}


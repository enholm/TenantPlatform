namespace TenantPlatform.Core.Services;

public class ServiceRequest
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid ServiceDefinitionId { get; set; }

    public Guid RequesterUserId { get; set; }

    public Guid RequesterOrganizationId { get; set; }

    public Guid BuildingId { get; set; }

    public Guid? UnitId { get; set; }

    public Guid? AssignedServiceProviderOrganizationId { get; set; }

    public string? Title { get; set; }

    public string? Comment { get; set; }

    public string ReplyToken { get; set; } = string.Empty;

    public ServiceRequestStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}


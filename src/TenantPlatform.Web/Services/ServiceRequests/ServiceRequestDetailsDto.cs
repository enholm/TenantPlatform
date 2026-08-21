using TenantPlatform.Core.Services;

namespace TenantPlatform.Web.Services.ServiceRequests;

public class ServiceRequestDetailsDto
{
    public Guid Id { get; init; }

    public Guid ServiceDefinitionId { get; init; }

    public string ServiceName { get; init; } = string.Empty;

    public string? ServiceDescription { get; init; }

    public string? Category { get; init; }

    public Guid RequesterUserId { get; init; }

    public Guid RequesterOrganizationId { get; init; }

    public string RequesterOrganizationName { get; init; } = string.Empty;

    public Guid BuildingId { get; init; }

    public string BuildingName { get; init; } = string.Empty;

    public Guid? UnitId { get; init; }

    public string? UnitName { get; init; }

    public ServiceRequestStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SubmittedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public List<ServiceRequestFieldValueDto> Values { get; init; } = [];

    public List<ServiceRequestMessageDto> Messages { get; init; } = [];
}


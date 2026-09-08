using TenantPlatform.Core.Services;

namespace TenantPlatform.Web.Services.ServiceRequests;

public class ServiceRequestAdminListItemDto
{
    public Guid Id { get; init; }

    public Guid ServiceDefinitionId { get; init; }

    public string ServiceName { get; init; } = string.Empty;

    public string? Title { get; init; }

    public string RequesterName { get; init; } = string.Empty;

    public string RequesterEmail { get; init; } = string.Empty;

    public string RequesterOrganizationName { get; init; } = string.Empty;

    public Guid BuildingId { get; init; }

    public string BuildingName { get; init; } = string.Empty;

    public string? UnitName { get; init; }

    public Guid? AssignedProviderOrganizationId { get; init; }

    public string? AssignedProviderOrganizationName { get; init; }

    public ServiceRequestStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

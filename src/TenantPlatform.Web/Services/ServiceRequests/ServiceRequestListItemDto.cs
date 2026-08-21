using TenantPlatform.Core.Services;

namespace TenantPlatform.Web.Services.ServiceRequests;

public class ServiceRequestListItemDto
{
    public Guid Id { get; init; }

    public string ServiceName { get; init; } = string.Empty;

    public string? Category { get; init; }

    public string RequesterOrganizationName { get; init; } = string.Empty;

    public string BuildingName { get; init; } = string.Empty;

    public string? UnitName { get; init; }

    public ServiceRequestStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SubmittedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}


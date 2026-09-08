using TenantPlatform.Core.Services;

namespace TenantPlatform.Web.Services.ServiceRequests;

public class ServiceRequestAdminFilter
{
    public ServiceRequestStatus? Status { get; init; }

    public Guid? ServiceDefinitionId { get; init; }

    public Guid? BuildingId { get; init; }

    public Guid? AssignedProviderOrganizationId { get; init; }

    public string? Search { get; init; }
}

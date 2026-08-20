namespace TenantPlatform.Web.Services.ServiceRequests;

public class CreateServiceRequestRequest
{
    public Guid ServiceDefinitionId { get; init; }

    public Guid OccupancyId { get; init; }

    public List<CreateServiceRequestFieldValueRequest> Values { get; init; } = [];
}


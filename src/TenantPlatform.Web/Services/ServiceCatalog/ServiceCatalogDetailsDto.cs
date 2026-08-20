using TenantPlatform.Web.Services.ServiceDefinitions;

namespace TenantPlatform.Web.Services.ServiceCatalog;

public class ServiceCatalogDetailsDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? Category { get; init; }

    public bool RequiresApproval { get; init; }

    public bool RequiresOccupancy { get; init; }

    public List<ServiceDefinitionFieldDto> Fields { get; init; } = [];
}


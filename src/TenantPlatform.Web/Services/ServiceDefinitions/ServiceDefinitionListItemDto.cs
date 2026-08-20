namespace TenantPlatform.Web.Services.ServiceDefinitions;

public class ServiceDefinitionListItemDto
{
    public Guid Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Category { get; init; }

    public string HandlerType { get; init; } = string.Empty;

    public bool RequiresApproval { get; init; }

    public bool IsBookableByTenant { get; init; }

    public bool RequiresOccupancy { get; init; }

    public bool IsActive { get; init; }
}


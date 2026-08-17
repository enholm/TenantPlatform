using TenantPlatform.Core.Organizations;

namespace TenantPlatform.Web.Services.Organizations;

public class OrganizationDetailsDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? OrganizationNumber { get; init; }

    public OrganizationType Type { get; init; }

    public bool IsActive { get; init; }

    public int OccupancyCount { get; init; }
}


using TenantPlatform.Core.Organizations;

namespace TenantPlatform.Application.Organizations;

public class UpdateOrganizationRequest
{
    public string Name { get; init; } = string.Empty;

    public string? OrganizationNumber { get; init; }

    public OrganizationType Type { get; init; }

    public bool IsActive { get; init; }
}


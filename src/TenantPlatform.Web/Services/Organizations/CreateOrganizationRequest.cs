using TenantPlatform.Core.Organizations;

namespace TenantPlatform.Web.Services.Organizations;

public class CreateOrganizationRequest
{
    public string Name { get; init; } = string.Empty;

    public string? OrganizationNumber { get; init; }

    public OrganizationType Type { get; init; }

    public bool IsActive { get; init; } = true;
}


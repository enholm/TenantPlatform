namespace TenantPlatform.Core.Organizations;

public class Organization
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? OrganizationNumber { get; set; }

    public OrganizationType Type { get; set; }

    public bool IsActive { get; set; } = true;
}
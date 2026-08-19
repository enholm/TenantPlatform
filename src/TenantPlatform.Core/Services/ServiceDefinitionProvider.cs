namespace TenantPlatform.Core.Services;


public class ServiceDefinitionProvider
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid ServiceDefinitionId { get; set; }

    public Guid ServiceProviderOrganizationId { get; set; }

    public ServiceProviderIntegrationType IntegrationType { get; set; }

    public string? RequestEmailAddress { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
}
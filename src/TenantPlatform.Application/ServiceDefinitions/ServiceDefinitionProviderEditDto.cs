using TenantPlatform.Core.Services;

public class ServiceDefinitionProviderEditDto
{
    public Guid ServiceProviderOrganizationId { get; set; }

    public ServiceProviderIntegrationType IntegrationType { get; set; }
        = ServiceProviderIntegrationType.Email;

    public string? RequestEmailAddress { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
}


using TenantPlatform.Core.Services;

namespace TenantPlatform.Web.Services.ServiceDefinitions;

public class ServiceDefinitionProviderDto
{
    public Guid Id { get; init; }

    public Guid ServiceProviderOrganizationId { get; init; }

    public string ServiceProviderOrganizationName { get; init; }
        = string.Empty;

    public ServiceProviderIntegrationType IntegrationType { get; init; }

    public string? RequestEmailAddress { get; init; }

    public bool IsDefault { get; init; }

    public bool IsActive { get; init; }
}


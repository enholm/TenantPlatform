using TenantPlatform.Core.Services;

public class ServiceRequestProviderOptionDto
{
    public Guid OrganizationId { get; init; }

    public string OrganizationName { get; init; } = string.Empty;

    public ServiceProviderIntegrationType IntegrationType { get; init; }

    public string? RequestEmailAddress { get; init; }
}


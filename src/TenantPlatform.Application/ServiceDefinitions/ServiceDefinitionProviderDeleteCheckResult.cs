namespace TenantPlatform.Application.ServiceDefinitions;

public class ServiceDefinitionProviderDeleteCheckResult
{
    public bool CanDelete { get; init; }

    public int OpenRequestCount { get; init; }
}


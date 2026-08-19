namespace TenantPlatform.Web.Services.ServiceDefinitions;

public interface IServiceDefinitionService
{
    Task<List<ServiceDefinitionListItemDto>> GetServiceDefinitionsAsync(
        Guid accountId,
        string languageCode,
        CancellationToken cancellationToken = default);

    Task<ServiceDefinitionDetailsDto?> GetServiceDefinitionAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        string languageCode,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateServiceDefinitionAsync(
        Guid accountId,
        CreateServiceDefinitionRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateServiceDefinitionAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        UpdateServiceDefinitionRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceDefinitionDeleteCheckResult> CanDeleteServiceDefinitionAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        CancellationToken cancellationToken = default);

    Task DeleteServiceDefinitionAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        CancellationToken cancellationToken = default);
}


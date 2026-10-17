using TenantPlatform.Application.ServiceDefinitions;
namespace TenantPlatform.Web.Services.ServiceDefinitions;

public sealed class ServiceDefinitionService(TenantPlatform.Application.ServiceDefinitions.ServiceDefinitionService service) : IServiceDefinitionService
{
    public Task<List<ServiceDefinitionListItemDto>> GetServiceDefinitionsAsync(Guid accountId, string languageCode, CancellationToken cancellationToken = default) => service.GetServiceDefinitionsAsync(accountId, languageCode, cancellationToken);
    public Task<ServiceDefinitionDetailsDto?> GetServiceDefinitionAsync(Guid accountId, Guid serviceDefinitionId, string languageCode, CancellationToken cancellationToken = default) => service.GetServiceDefinitionAsync(accountId, serviceDefinitionId, languageCode, cancellationToken);
    public Task<Guid> CreateServiceDefinitionAsync(Guid accountId, CreateServiceDefinitionRequest request, CancellationToken cancellationToken = default) => service.CreateServiceDefinitionAsync(accountId, request, cancellationToken);
    public Task UpdateServiceDefinitionAsync(Guid accountId, Guid serviceDefinitionId, UpdateServiceDefinitionRequest request, CancellationToken cancellationToken = default) => service.UpdateServiceDefinitionAsync(accountId, serviceDefinitionId, request, cancellationToken);
    public Task<ServiceDefinitionDeleteCheckResult> CanDeleteServiceDefinitionAsync(Guid accountId, Guid serviceDefinitionId, CancellationToken cancellationToken = default) => service.CanDeleteServiceDefinitionAsync(accountId, serviceDefinitionId, cancellationToken);
    public Task DeleteServiceDefinitionAsync(Guid accountId, Guid serviceDefinitionId, CancellationToken cancellationToken = default) => service.DeleteServiceDefinitionAsync(accountId, serviceDefinitionId, cancellationToken);
    public Task<IReadOnlyList<ServiceDefinitionProviderDto>> GetProvidersAsync(Guid accountId, Guid serviceDefinitionId, CancellationToken cancellationToken = default) => service.GetProvidersAsync(accountId, serviceDefinitionId, cancellationToken);
    public Task<ServiceDefinitionProviderEditDto?> GetProviderAsync(Guid accountId, Guid serviceDefinitionId, Guid providerId, CancellationToken cancellationToken = default) => service.GetProviderAsync(accountId, serviceDefinitionId, providerId, cancellationToken);
    public Task<IReadOnlyList<ServiceProviderOrganizationOptionDto>> GetProviderOrganizationOptionsAsync(Guid accountId, CancellationToken cancellationToken = default) => service.GetProviderOrganizationOptionsAsync(accountId, cancellationToken);
    public Task<Guid> CreateProviderAsync(Guid accountId, Guid serviceDefinitionId, ServiceDefinitionProviderEditDto model, CancellationToken cancellationToken = default) => service.CreateProviderAsync(accountId, serviceDefinitionId, model, cancellationToken);
    public Task UpdateProviderAsync(Guid accountId, Guid serviceDefinitionId, Guid providerId, ServiceDefinitionProviderEditDto model, CancellationToken cancellationToken = default) => service.UpdateProviderAsync(accountId, serviceDefinitionId, providerId, model, cancellationToken);
    public Task DeleteProviderAsync(Guid accountId, Guid serviceDefinitionId, Guid providerId, CancellationToken cancellationToken = default) => service.DeleteProviderAsync(accountId, serviceDefinitionId, providerId, cancellationToken);
    public Task<ServiceDefinitionProviderDeleteCheckResult> CanDeleteProviderAsync(Guid accountId, Guid serviceDefinitionId, Guid providerId, CancellationToken cancellationToken = default) => service.CanDeleteProviderAsync(accountId, serviceDefinitionId, providerId, cancellationToken);
}

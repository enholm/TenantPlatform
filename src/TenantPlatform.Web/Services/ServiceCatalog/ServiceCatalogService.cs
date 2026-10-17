using TenantPlatform.Application.ServiceCatalog;
namespace TenantPlatform.Web.Services.ServiceCatalog;
public sealed class ServiceCatalogService(TenantPlatform.Application.ServiceCatalog.ServiceCatalogService service) : IServiceCatalogService
{
    public Task<List<ServiceCatalogItemDto>> GetAvailableServicesAsync(Guid accountId, string languageCode, CancellationToken cancellationToken = default) => service.GetAvailableServicesAsync(accountId, languageCode, cancellationToken);
    public Task<ServiceCatalogDetailsDto?> GetServiceAsync(Guid accountId, Guid serviceDefinitionId, string languageCode, CancellationToken cancellationToken = default) => service.GetServiceAsync(accountId, serviceDefinitionId, languageCode, cancellationToken);
    public Task<List<ServiceRequestLocationDto>> GetAvailableRequestLocationsAsync(Guid accountId, CancellationToken cancellationToken = default) => service.GetAvailableRequestLocationsAsync(accountId, cancellationToken);
}

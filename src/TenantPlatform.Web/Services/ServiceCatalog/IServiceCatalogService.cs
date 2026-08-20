namespace TenantPlatform.Web.Services.ServiceCatalog;

public interface IServiceCatalogService
{
    Task<List<ServiceCatalogItemDto>> GetAvailableServicesAsync(
        Guid accountId,
        string languageCode,
        CancellationToken cancellationToken = default);

    Task<ServiceCatalogDetailsDto?> GetServiceAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        string languageCode,
        CancellationToken cancellationToken = default);

    Task<List<ServiceRequestLocationDto>>
        GetAvailableRequestLocationsAsync(
            Guid accountId,
            Guid userId,
            CancellationToken cancellationToken = default);
}


namespace TenantPlatform.Web.Services.Occupancies;

public interface IOccupancyService
{
    Task<List<OccupancyListItemDto>> GetOccupanciesAsync(
        Guid accountId,
        Guid? buildingId = null,
        Guid? tenantOrganizationId = null,
        Guid? unitId = null,
        CancellationToken cancellationToken = default);

    Task<OccupancyDetailsDto?> GetOccupancyAsync(
        Guid accountId,
        Guid occupancyId,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateOccupancyAsync(
        Guid accountId,
        CreateOccupancyRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateOccupancyAsync(
        Guid accountId,
        Guid occupancyId,
        UpdateOccupancyRequest request,
        CancellationToken cancellationToken = default);

    Task EndOccupancyAsync(
        Guid accountId,
        Guid occupancyId,
        DateOnly validTo,
        CancellationToken cancellationToken = default);
}


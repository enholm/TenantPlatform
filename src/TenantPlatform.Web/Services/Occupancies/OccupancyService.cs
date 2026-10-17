using TenantPlatform.Application.Occupancies;
namespace TenantPlatform.Web.Services.Occupancies;

public sealed class OccupancyService(TenantPlatform.Application.Occupancies.OccupancyService service) : IOccupancyService
{
    public Task<List<OccupancyListItemDto>> GetOccupanciesAsync(Guid accountId, Guid? buildingId = null, Guid? tenantOrganizationId = null, Guid? unitId = null, CancellationToken cancellationToken = default) => service.GetOccupanciesAsync(accountId, buildingId, tenantOrganizationId, unitId, cancellationToken);
    public Task<OccupancyDetailsDto?> GetOccupancyAsync(Guid accountId, Guid occupancyId, CancellationToken cancellationToken = default) => service.GetOccupancyAsync(accountId, occupancyId, cancellationToken);
    public Task<Guid> CreateOccupancyAsync(Guid accountId, CreateOccupancyRequest request, CancellationToken cancellationToken = default) => service.CreateOccupancyAsync(accountId, request, cancellationToken);
    public Task UpdateOccupancyAsync(Guid accountId, Guid occupancyId, UpdateOccupancyRequest request, CancellationToken cancellationToken = default) => service.UpdateOccupancyAsync(accountId, occupancyId, request, cancellationToken);
    public Task EndOccupancyAsync(Guid accountId, Guid occupancyId, DateOnly validTo, CancellationToken cancellationToken = default) => service.EndOccupancyAsync(accountId, occupancyId, validTo, cancellationToken);
    public Task<List<OccupancyHierarchyConflictDto>> GetHierarchyConflictsAsync(Guid accountId, Guid unitId, DateOnly validFrom, DateOnly? validTo, Guid? excludeOccupancyId = null, CancellationToken cancellationToken = default) => service.GetHierarchyConflictsAsync(accountId, unitId, validFrom, validTo, excludeOccupancyId, cancellationToken);
    public Task<OccupancyDeleteCheckResult> CanDeleteOccupancyAsync(Guid accountId, Guid occupancyId, CancellationToken cancellationToken = default) => service.CanDeleteOccupancyAsync(accountId, occupancyId, cancellationToken);
    public Task DeleteOccupancyAsync(Guid accountId, Guid occupancyId, CancellationToken cancellationToken = default) => service.DeleteOccupancyAsync(accountId, occupancyId, cancellationToken);
}

using TenantPlatform.Application.Units;
namespace TenantPlatform.Web.Services.Units;

public sealed class UnitService(TenantPlatform.Application.Units.UnitService service) : IUnitService
{
    public Task<List<UnitListItemDto>> GetUnitsAsync(Guid accountId, Guid? buildingId = null, CancellationToken cancellationToken = default) => service.GetUnitsAsync(accountId, buildingId, cancellationToken);
    public Task<UnitDetailsDto?> GetUnitAsync(Guid accountId, Guid unitId, CancellationToken cancellationToken = default) => service.GetUnitAsync(accountId, unitId, cancellationToken);
    public Task<Guid> CreateUnitAsync(Guid accountId, CreateUnitRequest request, CancellationToken cancellationToken = default) => service.CreateUnitAsync(accountId, request, cancellationToken);
    public Task UpdateUnitAsync(Guid accountId, Guid unitId, UpdateUnitRequest request, CancellationToken cancellationToken = default) => service.UpdateUnitAsync(accountId, unitId, request, cancellationToken);
    public Task<UnitDeleteCheckResult> CanDeleteUnitAsync(Guid accountId, Guid unitId, CancellationToken cancellationToken = default) => service.CanDeleteUnitAsync(accountId, unitId, cancellationToken);
    public Task DeleteUnitAsync(Guid accountId, Guid unitId, CancellationToken cancellationToken = default) => service.DeleteUnitAsync(accountId, unitId, cancellationToken);
    public Task<List<UnitListItemDto>> GetParentCandidatesAsync(Guid accountId, Guid buildingId, Guid? excludeUnitId = null, CancellationToken cancellationToken = default) => service.GetParentCandidatesAsync(accountId, buildingId, excludeUnitId, cancellationToken);
}

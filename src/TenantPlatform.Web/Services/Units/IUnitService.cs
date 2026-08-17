namespace TenantPlatform.Web.Services.Units;

public interface IUnitService
{
    Task<List<UnitListItemDto>> GetUnitsAsync(
        Guid accountId,
        Guid? buildingId = null,
        CancellationToken cancellationToken = default);

    Task<UnitDetailsDto?> GetUnitAsync(
        Guid accountId,
        Guid unitId,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateUnitAsync(
        Guid accountId,
        CreateUnitRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateUnitAsync(
        Guid accountId,
        Guid unitId,
        UpdateUnitRequest request,
        CancellationToken cancellationToken = default);

    Task<UnitDeleteCheckResult> CanDeleteUnitAsync(
        Guid accountId,
        Guid unitId,
        CancellationToken cancellationToken = default);

    Task DeleteUnitAsync(
        Guid accountId,
        Guid unitId,
        CancellationToken cancellationToken = default);

    Task<List<UnitListItemDto>> GetParentCandidatesAsync(
        Guid accountId,
        Guid buildingId,
        Guid? excludeUnitId = null,
        CancellationToken cancellationToken = default);
}


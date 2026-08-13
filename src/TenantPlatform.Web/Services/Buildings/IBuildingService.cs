namespace TenantPlatform.Web.Services.Buildings;

public interface IBuildingService
{
    Task<List<BuildingListItemDto>> GetBuildingsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<BuildingDetailsDto?> GetBuildingAsync(
        Guid accountId,
        Guid buildingId,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateBuildingAsync(
    Guid accountId,
    CreateBuildingRequest request,
    CancellationToken cancellationToken = default);

    Task UpdateBuildingAsync(
    Guid accountId,
    Guid buildingId,
    UpdateBuildingRequest request,
    CancellationToken cancellationToken = default);

    Task DeleteBuildingAsync(
        Guid accountId,
        Guid buildingId,
        CancellationToken cancellationToken = default);    

    Task<BuildingDeleteCheckResult> CanDeleteBuildingAsync(
        Guid accountId,
        Guid buildingId,
        CancellationToken cancellationToken = default);
}

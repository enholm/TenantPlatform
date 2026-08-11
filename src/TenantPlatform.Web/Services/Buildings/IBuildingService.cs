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
}

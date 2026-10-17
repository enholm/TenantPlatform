using TenantPlatform.Application.Buildings;

namespace TenantPlatform.Web.Services.Buildings;

public sealed class BuildingService(TenantPlatform.Application.Buildings.BuildingService service) : IBuildingService
{
    public Task<List<BuildingListItemDto>> GetBuildingsAsync(Guid accountId, CancellationToken cancellationToken = default) => service.GetBuildingsAsync(accountId, cancellationToken);
    public Task<BuildingDetailsDto?> GetBuildingAsync(Guid accountId, Guid buildingId, CancellationToken cancellationToken = default) => service.GetBuildingAsync(accountId, buildingId, cancellationToken);
    public Task<Guid> CreateBuildingAsync(Guid accountId, CreateBuildingRequest request, CancellationToken cancellationToken = default) => service.CreateBuildingAsync(accountId, request, cancellationToken);
    public Task UpdateBuildingAsync(Guid accountId, Guid buildingId, UpdateBuildingRequest request, CancellationToken cancellationToken = default) => service.UpdateBuildingAsync(accountId, buildingId, request, cancellationToken);
    public Task DeleteBuildingAsync(Guid accountId, Guid buildingId, CancellationToken cancellationToken = default) => service.DeleteBuildingAsync(accountId, buildingId, cancellationToken);
    public Task<BuildingDeleteCheckResult> CanDeleteBuildingAsync(Guid accountId, Guid buildingId, CancellationToken cancellationToken = default) => service.CanDeleteBuildingAsync(accountId, buildingId, cancellationToken);
}

using TenantPlatform.Core.Identity;

namespace TenantPlatform.Web.Security.Authorization;

public interface ITenantAuthorizationService
{
    Task<bool> HasRoleAsync(
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<bool> CanManageBuildingAsync(
        Guid buildingId,
        CancellationToken cancellationToken = default);
    Task<bool> CanCreateBuildingAsync(
        CancellationToken cancellationToken = default);

    Task<bool> CanEditBuildingAsync(
        Guid buildingId,
        CancellationToken cancellationToken = default);

    Task<bool> CanDeleteBuildingAsync(
        Guid buildingId,
        CancellationToken cancellationToken = default);
    Task<bool> CanManageOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<bool> CanCreateOrganizationAsync(
        CancellationToken cancellationToken = default);

    Task<bool> CanEditOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<bool> CanDeleteOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
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

    Task<bool> CanManageOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
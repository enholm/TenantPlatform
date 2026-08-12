using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Security.Authorization;

public class TenantAuthorizationService
    : ITenantAuthorizationService
{
    private readonly TenantPlatformDbContext _dbContext;
    private readonly ICurrentUserContextService _currentUserService;

    public TenantAuthorizationService(
        TenantPlatformDbContext dbContext,
        ICurrentUserContextService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<bool> HasRoleAsync(
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        var currentUser = _currentUserService.Current;

        if (!currentUser.IsAuthenticated ||
            !currentUser.CurrentAccountId.HasValue)
        {
            return false;
        }

        return await _dbContext.UserAccountRoles
            .AnyAsync(
                x =>
                    x.UserAccount.UserId == currentUser.UserId &&
                    x.UserAccount.AccountId == currentUser.CurrentAccountId.Value &&
                    x.Role == role &&
                    x.OrganizationId == null &&
                    x.BuildingId == null,
                cancellationToken);
    }

    public async Task<bool> CanManageBuildingAsync(
        Guid buildingId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = _currentUserService.Current;

        if (!currentUser.IsAuthenticated ||
            !currentUser.CurrentAccountId.HasValue)
        {
            return false;
        }

        var accountId = currentUser.CurrentAccountId.Value;

        return await _dbContext.UserAccountRoles
            .AnyAsync(
                x =>
                    x.UserAccount.UserId == currentUser.UserId &&
                    x.UserAccount.AccountId == accountId &&
                    (
                        x.Role == UserRole.AccountAdmin ||
                        (
                            x.Role == UserRole.PropertyAdmin &&
                            x.BuildingId == buildingId
                        )
                    ),
                cancellationToken);
    }

    public async Task<bool> CanCreateBuildingAsync(
        CancellationToken cancellationToken = default)
    {
        return await HasRoleAsync(
            UserRole.AccountAdmin,
            cancellationToken);
    }

    public async Task<bool> CanEditBuildingAsync(
        Guid buildingId,
        CancellationToken cancellationToken = default)
    {
        return await CanManageBuildingAsync(
            buildingId,
            cancellationToken);
    }

    public async Task<bool> CanDeleteBuildingAsync(
        Guid buildingId,
        CancellationToken cancellationToken = default)
    {
        return await CanManageBuildingAsync(
            buildingId,
            cancellationToken);
    }
    public async Task<bool> CanManageOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = _currentUserService.Current;

        if (!currentUser.IsAuthenticated ||
            !currentUser.CurrentAccountId.HasValue)
        {
            return false;
        }

        var accountId = currentUser.CurrentAccountId.Value;

        return await _dbContext.UserAccountRoles
            .AnyAsync(
                x =>
                    x.UserAccount.UserId == currentUser.UserId &&
                    x.UserAccount.AccountId == accountId &&
                    (
                        x.Role == UserRole.AccountAdmin ||
                        (
                            x.Role == UserRole.TenantAdmin &&
                            x.OrganizationId == organizationId
                        )
                    ),
                cancellationToken);
    }
}
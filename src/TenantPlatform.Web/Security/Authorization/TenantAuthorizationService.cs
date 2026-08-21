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

    /***************************************************************
     **                         Buildings                         **
     ***************************************************************/
     
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

    public async Task<bool> CanViewBuildingAsync(
        Guid buildingId,
        CancellationToken cancellationToken = default)
    {
        if (await CanManageBuildingAsync(
            buildingId,
            cancellationToken))
        {
            return true;
        }

        return await HasTenantAccessToBuildingAsync(
            buildingId,
            cancellationToken);
    }

    /***************************************************************
     **                         Organizations                     **
     ***************************************************************/

    public async Task<bool> CanCreateOrganizationAsync(
        CancellationToken cancellationToken = default)
    {
        return await HasRoleAsync(
            UserRole.AccountAdmin,
            cancellationToken);
    }

    public async Task<bool> CanEditOrganizationAsync(
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

    public async Task<bool> CanDeleteOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await HasRoleAsync(
            UserRole.AccountAdmin,
            cancellationToken);
    }

    public async Task<bool> CanViewOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        if (await HasRoleAsync(
            UserRole.AccountAdmin,
            cancellationToken))
        {
            return true;
        }

        if (await HasRoleAsync(
            UserRole.PropertyAdmin,
            cancellationToken))
        {
            // Kan eventuelt strammes ytterligere senere,
            // slik at PropertyAdmin bare ser tenants i egne bygg.
            return true;
        }

        return await HasTenantAccessAsync(
            organizationId,
            cancellationToken);
    }


    /***************************************************************
     **                           Units                           **
     ***************************************************************/
    public async Task<bool> CanCreateUnitAsync(
        Guid buildingId,
        CancellationToken cancellationToken = default)
    {
        return await CanManageBuildingAsync(
            buildingId,
            cancellationToken);
    }

    public async Task<bool> CanEditUnitAsync(
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = _currentUserService.Current;

        if (!currentUser.IsAuthenticated ||
            !currentUser.CurrentAccountId.HasValue)
        {
            return false;
        }

        var accountId = currentUser.CurrentAccountId.Value;

        var buildingId = await _dbContext.Units
            .Where(x =>
                x.Id == unitId &&
                x.AccountId == accountId)
            .Select(x => (Guid?)x.BuildingId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!buildingId.HasValue)
        {
            return false;
        }

        return await CanManageBuildingAsync(
            buildingId.Value,
            cancellationToken);
    }

    public async Task<bool> CanDeleteUnitAsync(
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        return await CanEditUnitAsync(
            unitId,
            cancellationToken);
    }

    public async Task<bool> CanViewUnitAsync(
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        if (await CanEditUnitAsync(
            unitId,
            cancellationToken))
        {
            return true;
        }

        return await HasTenantAccessToUnitAsync(
            unitId,
            cancellationToken);
    }

    /***************************************************************
     **                        Occupancies                        **
     ***************************************************************/
    public async Task<bool> CanCreateOccupancyAsync(
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = _currentUserService.Current;

        if (!currentUser.IsAuthenticated ||
            !currentUser.CurrentAccountId.HasValue)
        {
            return false;
        }

        var accountId = currentUser.CurrentAccountId.Value;

        var buildingId = await _dbContext.Units
            .Where(x =>
                x.Id == unitId &&
                x.AccountId == accountId)
            .Select(x => (Guid?)x.BuildingId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!buildingId.HasValue)
        {
            return false;
        }

        return await CanManageBuildingAsync(
            buildingId.Value,
            cancellationToken);
    }

    public async Task<bool> CanEditOccupancyAsync(
        Guid occupancyId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = _currentUserService.Current;

        if (!currentUser.IsAuthenticated ||
            !currentUser.CurrentAccountId.HasValue)
        {
            return false;
        }

        var accountId = currentUser.CurrentAccountId.Value;

        var buildingId = await (
            from occupancy in _dbContext.Occupancies
            join unit in _dbContext.Units
                on occupancy.UnitId equals unit.Id
            where occupancy.Id == occupancyId
                && occupancy.AccountId == accountId
            select (Guid?)unit.BuildingId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!buildingId.HasValue)
        {
            return false;
        }

        return await CanManageBuildingAsync(
            buildingId.Value,
            cancellationToken);
    }

    public async Task<bool> CanEndOccupancyAsync(
        Guid occupancyId,
        CancellationToken cancellationToken = default)
    {
        return await CanEditOccupancyAsync(
            occupancyId,
            cancellationToken);
    }

    public async Task<bool> CanDeleteOccupancyAsync(
        Guid occupancyId,
        CancellationToken cancellationToken = default)
    {
        return await CanEditOccupancyAsync(
            occupancyId,
            cancellationToken);
    }    


    /***************************************************************
     **                        Tenant Helpers                     **
     ***************************************************************/
    private async Task<bool> HasTenantRoleAsync(
        Guid organizationId,
        UserRole role,
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
                    x.OrganizationId == organizationId &&
                    x.Role == role,
                cancellationToken);
    }

    private async Task<bool> HasTenantAccessAsync(
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
                    x.OrganizationId == organizationId &&
                    (
                        x.Role == UserRole.TenantAdmin ||
                        x.Role == UserRole.TenantUser
                    ),
                cancellationToken);
    }

    private async Task<bool> HasTenantAccessToUnitAsync(
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = _currentUserService.Current;

        if (!currentUser.IsAuthenticated ||
            !currentUser.CurrentAccountId.HasValue)
        {
            return false;
        }

        var accountId = currentUser.CurrentAccountId.Value;

        var today = DateOnly.FromDateTime(DateTime.Today);

        return await (
            from role in _dbContext.UserAccountRoles
            join occupancy in _dbContext.Occupancies
                on role.OrganizationId equals occupancy.TenantOrganizationId
            where
                role.UserAccount.UserId == currentUser.UserId &&
                role.UserAccount.AccountId == accountId &&
                occupancy.AccountId == accountId &&
                occupancy.UnitId == unitId &&
                (
                    role.Role == UserRole.TenantAdmin ||
                    role.Role == UserRole.TenantUser
                ) &&
                occupancy.ValidFrom <= today &&
                (
                    occupancy.ValidTo == null ||
                    occupancy.ValidTo >= today
                )
            select occupancy.Id)
            .AnyAsync(cancellationToken);
    }

    private async Task<bool> HasTenantAccessToBuildingAsync(
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

        var today = DateOnly.FromDateTime(DateTime.Today);

        return await (
            from role in _dbContext.UserAccountRoles
            join occupancy in _dbContext.Occupancies
                on role.OrganizationId equals occupancy.TenantOrganizationId
            join unit in _dbContext.Units
                on occupancy.UnitId equals unit.Id
            where
                role.UserAccount.UserId == currentUser.UserId &&
                role.UserAccount.AccountId == accountId &&
                occupancy.AccountId == accountId &&
                unit.AccountId == accountId &&
                unit.BuildingId == buildingId &&
                (
                    role.Role == UserRole.TenantAdmin ||
                    role.Role == UserRole.TenantUser
                ) &&
                occupancy.ValidFrom <= today &&
                (
                    occupancy.ValidTo == null ||
                    occupancy.ValidTo >= today
                )
            select occupancy.Id)
            .AnyAsync(cancellationToken);
    }

    /***************************************************************
    **                     Servicedefinitions.                   **
    ***************************************************************/
    public async Task<bool> CanCreateServiceDefinitionAsync(
        CancellationToken cancellationToken = default)
    {
        return await HasRoleAsync(
            UserRole.AccountAdmin,
            cancellationToken);
    }
    public async Task<bool> CanEditServiceDefinitionAsync(
        Guid serviceDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = _currentUserService.Current;

        if (!currentUser.IsAuthenticated ||
            !currentUser.CurrentAccountId.HasValue)
        {
            return false;
        }

        var accountId = currentUser.CurrentAccountId.Value;

        var exists = await _dbContext.ServiceDefinitions
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.Id == serviceDefinitionId &&
                    x.AccountId == accountId,
                cancellationToken);

        if (!exists)
        {
            return false;
        }

        return await HasRoleAsync(
            UserRole.AccountAdmin,
            cancellationToken);
    }
    public async Task<bool> CanDeleteServiceDefinitionAsync(
        Guid serviceDefinitionId,
        CancellationToken cancellationToken = default)
    {
        return await CanEditServiceDefinitionAsync(
            serviceDefinitionId,
            cancellationToken);
    }
    public async Task<bool> CanViewServiceDefinitionAsync(
        Guid serviceDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = _currentUserService.Current;

        if (!currentUser.IsAuthenticated ||
            !currentUser.CurrentAccountId.HasValue)
        {
            return false;
        }

        var accountId = currentUser.CurrentAccountId.Value;

        var definition = await _dbContext.ServiceDefinitions
            .AsNoTracking()
            .Where(x =>
                x.Id == serviceDefinitionId &&
                x.AccountId == accountId)
            .Select(x => new
            {
                x.IsActive,
                x.IsBookableByTenant
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (definition is null)
        {
            return false;
        }

        if (await HasRoleAsync(
            UserRole.AccountAdmin,
            cancellationToken))
        {
            return true;
        }

        if (await HasRoleAsync(
            UserRole.PropertyAdmin,
            cancellationToken))
        {
            return true;
        }

        if (definition.IsActive &&
            definition.IsBookableByTenant)
        {
            if (await HasRoleAsync(
                UserRole.TenantAdmin,
                cancellationToken))
            {
                return true;
            }

            if (await HasRoleAsync(
                UserRole.TenantUser,
                cancellationToken))
            {
                return true;
            }
        }

        return false;
    }



    /***************************************************************
     **                     ServiceRequests.                      **
     ***************************************************************/
    public async Task<bool> CanApproveServiceRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = _currentUserService.Current;

        if (!currentUser.IsAuthenticated ||
            !currentUser.CurrentAccountId.HasValue)
        {
            return false;
        }

        var accountId = currentUser.CurrentAccountId.Value;

        var requestExists =
            await _dbContext.ServiceRequests
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Id == requestId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (!requestExists)
        {
            return false;
        }

        return await HasRoleAsync(
                UserRole.AccountAdmin,
                cancellationToken)
            ||
            await HasRoleAsync(
                UserRole.PropertyAdmin,
                cancellationToken);
    }

    public async Task<bool> CanCompleteServiceRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        return await CanApproveServiceRequestAsync(
            requestId,
            cancellationToken);
    }
}
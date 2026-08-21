using TenantPlatform.Core.Identity;

namespace TenantPlatform.Web.Security.Authorization;

public interface ITenantAuthorizationService
{
    Task<bool> HasRoleAsync(
        UserRole role,
        CancellationToken cancellationToken = default);

    /***************************************************************
     **                         Buildings                         **
     ***************************************************************/

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
    
    Task<bool> CanViewBuildingAsync(
        Guid buildingId,
        CancellationToken cancellationToken = default);
    
    /***************************************************************
     **                         Organizations                     **
     ***************************************************************/   
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

    Task<bool> CanViewOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);


    /***************************************************************
     **                           Units                           **
     ***************************************************************/
    Task<bool> CanCreateUnitAsync(
        Guid buildingId,
        CancellationToken cancellationToken = default);

    Task<bool> CanEditUnitAsync(
        Guid unitId,
        CancellationToken cancellationToken = default);

    Task<bool> CanDeleteUnitAsync(
        Guid unitId,
        CancellationToken cancellationToken = default);

    Task<bool> CanViewUnitAsync(
        Guid unitId,
        CancellationToken cancellationToken = default);

    /***************************************************************
     **                        Occupancies                        **
     ***************************************************************/
    Task<bool> CanCreateOccupancyAsync(
        Guid unitId,
        CancellationToken cancellationToken = default);

    Task<bool> CanEditOccupancyAsync(
        Guid occupancyId,
        CancellationToken cancellationToken = default);

    Task<bool> CanEndOccupancyAsync(
        Guid occupancyId,
        CancellationToken cancellationToken = default);

    Task<bool> CanDeleteOccupancyAsync(
        Guid occupancyId,
        CancellationToken cancellationToken = default);


    /***************************************************************
     **                     Servicedefinitions.                   **
     ***************************************************************/
    Task<bool> CanCreateServiceDefinitionAsync(
        CancellationToken cancellationToken = default);

    Task<bool> CanEditServiceDefinitionAsync(
        Guid serviceDefinitionId,
        CancellationToken cancellationToken = default);

    Task<bool> CanDeleteServiceDefinitionAsync(
        Guid serviceDefinitionId,
        CancellationToken cancellationToken = default);

    Task<bool> CanViewServiceDefinitionAsync(
        Guid serviceDefinitionId,
        CancellationToken cancellationToken = default);

    /***************************************************************
     **                     ServiceRequests.                      **
     ***************************************************************/
    Task<bool> CanApproveServiceRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<bool> CanCompleteServiceRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

}
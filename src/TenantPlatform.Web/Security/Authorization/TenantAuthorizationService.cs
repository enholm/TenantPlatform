using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Application;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Security.Authorization;

public class TenantAuthorizationService : ITenantAuthorizationService
{
    private readonly NavigationReader _navigation;
    private readonly IAuthorizationService _authorization;

    public TenantAuthorizationService(IDbContextFactory<TenantPlatformDbContext> factory, ICurrentUserContextService current, IAuthorizationService authorization, NavigationReader navigation)
    {
        _navigation = navigation;
        _authorization = authorization;
    }

    // Explicit composition for existing test/non-DI callers; all decisions remain in Application.
    public TenantAuthorizationService(IDbContextFactory<TenantPlatformDbContext> factory, ICurrentUserContextService current, IAuthorizationService authorization)
        : this(factory, current, authorization, new NavigationReader(new CurrentActorAdapter(current), new AuthorizationData(factory))) { }

    // Compatibility for existing non-DI callers; the same Application evaluator owns all rules.
    public TenantAuthorizationService(IDbContextFactory<TenantPlatformDbContext> factory, ICurrentUserContextService current)
        : this(factory, current, new AuthorizationService(new CurrentActorAdapter(current), new AuthorizationData(factory), TimeProvider.System)) { }

    public Task<bool> CanUseLeasingAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Leasing.Open, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanMaintainLeasingEquipmentAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Leasing.MaintainEquipment, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanManageLeasingLifecycleAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Leasing.ManageLifecycle, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanApproveLeasingLifecycleAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Leasing.ApproveLifecycle, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanReadLeasingReportsAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Leasing.ReadReports, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanExportLeasingReportsAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Leasing.ExportReports, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanActivateLeasingPlansAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Leasing.ActivatePlans, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanApproveLeasingInvoicesAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Leasing.ApproveInvoices, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanAcceptLeasingVariancesAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Leasing.AcceptVariances, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanApproveLeasingOrdersAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Leasing.ApproveOrders, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanChangeLeasingLimitAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Leasing.ApproveLimit, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanCreateAgreementAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Agreements.Create, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanUseAgreementsAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Agreements.Open, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanUseMeetingRoomsAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.MeetingRooms.Open, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanManageMeetingRoomsAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.MeetingRooms.Manage, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanManageAccountStructureAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.AccountStructure.Manage, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanManageOrganizationsAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Organizations.Open, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanManageBuildingsAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Buildings.Open, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanManageUnitsAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Units.Open, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanManageOccupanciesAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Occupancies.Open, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanUseTenantPortalAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Navigation.TenantPortal, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanUseProviderPortalAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Navigation.ProviderPortal, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanManageServiceDefinitionsAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.ServiceDefinitions.Open, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanUseHomeAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Navigation.Home, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanManageServiceRequestsAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.ServiceRequests.Open, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanCreateBuildingAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Buildings.Create, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanCreateOrganizationAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Organizations.Create, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanCreateServiceDefinitionAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.ServiceDefinitions.Create, new(ResourceKind.Account), cancellationToken);
    public Task<bool> CanCreateAccountAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Platform.CreateAccount, new(ResourceKind.Platform), cancellationToken);
    public Task<bool> CanManageAccountsAsync(CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Platform.OpenAccounts, new(ResourceKind.Platform), cancellationToken);
    public Task<bool> CanViewAccountAsync(Guid accountId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Platform.ReadAccount, new(ResourceKind.PlatformAccount, accountId), cancellationToken);
    public Task<bool> CanEditAccountAsync(Guid accountId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Platform.UpdateAccount, new(ResourceKind.PlatformAccount, accountId), cancellationToken);
    public Task<bool> CanDeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Platform.DeleteAccount, new(ResourceKind.PlatformAccount, accountId), cancellationToken);
    public Task<bool> CanManageBuildingAsync(Guid buildingId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Buildings.Update, new(ResourceKind.Building, buildingId), cancellationToken);
    public Task<bool> CanViewBuildingAsync(Guid buildingId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Buildings.Read, new(ResourceKind.Building, buildingId), cancellationToken);
    public Task<bool> CanEditBuildingAsync(Guid buildingId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Buildings.Update, new(ResourceKind.Building, buildingId), cancellationToken);
    public Task<bool> CanDeleteBuildingAsync(Guid buildingId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Buildings.Delete, new(ResourceKind.Building, buildingId), cancellationToken);
    public Task<bool> CanManageOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Organizations.Update, new(ResourceKind.Organization, organizationId), cancellationToken);
    public Task<bool> CanViewOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Organizations.Read, new(ResourceKind.Organization, organizationId), cancellationToken);
    public Task<bool> CanEditOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Organizations.Update, new(ResourceKind.Organization, organizationId), cancellationToken);
    public Task<bool> CanDeleteOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Organizations.Delete, new(ResourceKind.Organization, organizationId), cancellationToken);
    public Task<bool> CanCreateUnitAsync(Guid buildingId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Units.Create, new(ResourceKind.Building, buildingId), cancellationToken);
    public Task<bool> CanViewUnitAsync(Guid unitId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Units.Read, new(ResourceKind.Unit, unitId), cancellationToken);
    public Task<bool> CanEditUnitAsync(Guid unitId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Units.Update, new(ResourceKind.Unit, unitId), cancellationToken);
    public Task<bool> CanDeleteUnitAsync(Guid unitId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Units.Delete, new(ResourceKind.Unit, unitId), cancellationToken);
    public Task<bool> CanCreateOccupancyAsync(Guid unitId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Occupancies.Create, new(ResourceKind.Unit, unitId), cancellationToken);
    public Task<bool> CanEditOccupancyAsync(Guid occupancyId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Occupancies.Update, new(ResourceKind.Occupancy, occupancyId), cancellationToken);
    public Task<bool> CanEndOccupancyAsync(Guid occupancyId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Occupancies.End, new(ResourceKind.Occupancy, occupancyId), cancellationToken);
    public Task<bool> CanDeleteOccupancyAsync(Guid occupancyId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.Occupancies.Delete, new(ResourceKind.Occupancy, occupancyId), cancellationToken);
    public Task<bool> CanViewServiceDefinitionAsync(Guid serviceDefinitionId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.ServiceDefinitions.Read, new(ResourceKind.ServiceDefinition, serviceDefinitionId), cancellationToken);
    public Task<bool> CanEditServiceDefinitionAsync(Guid serviceDefinitionId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.ServiceDefinitions.Update, new(ResourceKind.ServiceDefinition, serviceDefinitionId), cancellationToken);
    public Task<bool> CanDeleteServiceDefinitionAsync(Guid serviceDefinitionId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.ServiceDefinitions.Delete, new(ResourceKind.ServiceDefinition, serviceDefinitionId), cancellationToken);
    public Task<bool> CanApproveServiceRequestAsync(Guid requestId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.ServiceRequests.Approve, new(ResourceKind.ServiceRequest, requestId), cancellationToken);
    public Task<bool> CanCompleteServiceRequestAsync(Guid requestId, CancellationToken cancellationToken = default) => _authorization.CanAsync(Permissions.ServiceRequests.Complete, new(ResourceKind.ServiceRequest, requestId), cancellationToken);

    public async Task<NavigationPermissions> GetNavigationPermissionsAsync(CancellationToken cancellationToken = default) =>
        (await _navigation.ReadAsync(cancellationToken)).Permissions;

    public async Task<string> GetStartPageAsync(CancellationToken cancellationToken = default)
    {
        var state = await _navigation.ReadAsync(cancellationToken);
        if (!state.IsAuthenticated) return "/login";
        if (!state.AccountId.HasValue) return state.IsPlatformAdmin ? "/accounts" : "/access-denied";
        return GetStartPage(state.IsPlatformAdmin, state.Roles);
    }

    public string GetStartPage(
        bool isPlatformAdmin,
        IReadOnlyCollection<UserRole> roles)
    {
        // Administrativ rolle i Account.
        if (roles.Contains(UserRole.AccountAdmin) ||
            roles.Contains(UserRole.PropertyAdmin))
        {
            return "/";
        }

        // Tenant-rolle i Account.
        if (roles.Contains(UserRole.TenantAdmin) ||
            roles.Contains(UserRole.TenantUser))
        {
            return "/portal/services";
        }

        // Provider-rolle i Account.
        if (roles.Contains(UserRole.ServiceProviderUser))
        {
            return "/provider/requests";
        }

        // Global PlatformAdmin uten en relevant Account-rolle.
        if (isPlatformAdmin)
        {
            return "/accounts";
        }

        return "/access-denied";
    }

}

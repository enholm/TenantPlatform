using TenantPlatform.Core.Identity;
namespace TenantPlatform.Application.Security;

public sealed record NavigationState(bool IsAuthenticated, bool IsPlatformAdmin, Guid? AccountId, IReadOnlyCollection<UserRole> Roles, NavigationPermissions Permissions);
public sealed class NavigationReader(ICurrentActor actor, IAuthorizationData data)
{
    public async Task<NavigationState> ReadAsync(CancellationToken ct = default)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated) return new(false, false, null, [], new());
        var subject = await data.GetSubjectAsync(current.UserId, current.AccountId, ct);
        if (!subject.IsActiveUser) return new(false, false, null, [], new());
        var platform = subject.IsPlatformAdmin;
        if (!current.AccountId.HasValue || !subject.IsMember)
            return new(true, platform, null, [], new() { CanSeePlatformAdministration = platform, CanSeeAccounts = platform, CanSeeUsers = platform });
        var assignments = subject.Assignments.Where(x => x.AccountId == current.AccountId).ToArray();
        var roles = assignments.Select(x => x.Role).Distinct().ToArray();
        bool Has(string permission) => assignments.Any(x => LegacyRolePermissions.Grants(x, permission));
        var admin = roles.Contains(UserRole.AccountAdmin);
        var property = admin || roles.Contains(UserRole.PropertyAdmin);
        var tenant = Has(Permissions.Navigation.TenantPortal);
        var provider = Has(Permissions.Navigation.ProviderPortal);
        var agreements = assignments.Any(x => LegacyRolePermissions.Grants(x, Permissions.Agreements.Open) && x.BuildingId is null && x.OrganizationId is null) ||
            await data.HasAgreementRelationshipAsync(current.AccountId.Value, current.UserId, ct);
        var leasing = Has(Permissions.Leasing.Open) || await data.HasLeasingOwnershipAsync(current.AccountId.Value, current.UserId, ct);
        return new(true, platform, current.AccountId, roles, new()
        {
            CanSeeMeetingRooms = true, CanSeeLeasing = leasing, CanSeeAgreements = agreements, CanManageMeetingRooms = admin,
            CanSeeTenantPortal = tenant, CanSeeProviderPortal = provider, CanSeePropertyManagement = property, CanSeeAdministration = admin,
            CanSeePlatformAdministration = platform, CanSeeMyRequests = tenant, CanSeeAssignedRequests = provider, CanSeeAllRequests = property,
            CanSeeBuildings = property, CanSeeUnits = property, CanSeeOccupancies = property, CanManageAccountStructure = admin,
            CanSeeOrganizations = admin, CanSeeUsers = platform || admin, CanSeeServiceDefinitions = admin, CanSeeAccounts = platform
        });
    }
}

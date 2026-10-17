using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Services;
namespace TenantPlatform.Application.ServiceRequests;

public enum ServiceRequestListKind { Mine, Assigned, Administration }
public sealed record ServiceRequestReadScope(Guid AccountId, Guid UserId, IReadOnlyList<RoleAssignment> Administrators,
    IReadOnlyList<RoleAssignment> Tenants, IReadOnlyList<RoleAssignment> Providers);
public static class ServiceRequestAccess
{
    public static ServiceRequestReadScope ReadScope(AccountActorScope actor) => new(actor.AccountId, actor.UserId,
        actor.Assignments.Where(x => LegacyRolePermissions.Grants(x, Permissions.ServiceRequests.Approve) &&
            (x.Role != UserRole.PropertyAdmin || x.BuildingId.HasValue)).ToArray(),
        actor.Assignments.Where(x => x.Role is UserRole.TenantAdmin or UserRole.TenantUser && x.OrganizationId.HasValue).ToArray(),
        actor.Assignments.Where(x => x.Role == UserRole.ServiceProviderUser && x.OrganizationId.HasValue).ToArray());
    public static bool Admin(AccountActorScope actor, ServiceRequest request) => actor.Assignments.Any(x =>
        LegacyRolePermissions.Grants(x, Permissions.ServiceRequests.Approve) && AuthorizationService.ResourceMatches(x, Permissions.ServiceRequests.Approve,
            ResourceKind.ServiceRequest, new(request.AccountId, request.BuildingId, request.RequesterOrganizationId)));
    private static bool Matches(RoleAssignment assignment, Guid organization, Guid building) => assignment.OrganizationId == organization &&
        (!assignment.BuildingId.HasValue || assignment.BuildingId == building);
    public static bool Tenant(AccountActorScope actor, ServiceRequest request) => actor.Assignments.Any(x =>
        x.Role is UserRole.TenantAdmin or UserRole.TenantUser && Matches(x, request.RequesterOrganizationId, request.BuildingId));
    public static bool Provider(AccountActorScope actor, ServiceRequest request) => request.AssignedServiceProviderOrganizationId is Guid provider &&
        actor.Assignments.Any(x => x.Role == UserRole.ServiceProviderUser && Matches(x, provider, request.BuildingId));
    public static bool Read(AccountActorScope actor, ServiceRequest request) => request.RequesterUserId == actor.UserId || Admin(actor, request) || Tenant(actor, request) || Provider(actor, request);
    public static bool Reply(AccountActorScope actor, ServiceRequest request) => request.RequesterUserId == actor.UserId || Admin(actor, request) || Tenant(actor, request);
}

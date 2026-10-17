using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.Security;

public enum ResourceKind { Account, Platform, PlatformAccount, Building, Organization, Unit, Occupancy, ServiceDefinition, ServiceRequest, Agreement }
public sealed record AuthorizationResource(ResourceKind Kind, Guid? Id = null, Guid? AccountId = null);
public sealed record RoleAssignment(UserRole Role, Guid AccountId, Guid? OrganizationId = null, Guid? BuildingId = null);
public sealed record AuthorizationSubject(bool IsActiveUser, bool IsMember, bool IsPlatformAdmin, IReadOnlyList<RoleAssignment> Assignments);
public sealed record ResourceAccessFacts(Guid AccountId, Guid? BuildingId = null, Guid? OrganizationId = null,
    Guid? OwnerUserId = null, AgreementAccessLevel? AgreementGrant = null, bool IsArchived = false,
    bool IsActive = true, bool IsBookable = false, IReadOnlyList<Guid>? TenantOrganizationIds = null);

public interface IAuthorizationData
{
    Task<AuthorizationSubject> GetSubjectAsync(Guid userId, Guid? accountId, CancellationToken ct);
    Task<ResourceAccessFacts?> GetResourceAsync(Guid accountId, Guid userId, ResourceKind kind, Guid id, DateOnly today, CancellationToken ct);
    Task<bool> AccountExistsAsync(Guid accountId, CancellationToken ct);
    Task<bool> HasAgreementRelationshipAsync(Guid accountId, Guid userId, CancellationToken ct);
    Task<bool> HasLeasingOwnershipAsync(Guid accountId, Guid userId, CancellationToken ct);
}

public sealed record AgreementReadScope(AgreementListScope ListScope, Guid AgreementId, bool CanEdit, bool CanManageAccess, bool IsArchived = false, bool CanEditBeforeArchive = false);
public sealed record HomeReadScope(Guid AccountId, bool CanReadAllBuildings, IReadOnlyList<Guid> BuildingIds);
public sealed record UserAdministrationScope(Guid ActorUserId, Guid? AccountId, bool IsPlatformAdmin);
public sealed record AccountActorScope(Guid AccountId, Guid UserId, IReadOnlyList<RoleAssignment> Assignments);

public interface IAuthorizationService
{
    Task<AccountActorScope> GetAccountActorScopeAsync(Guid accountId, CancellationToken ct = default);
    Task<UserAdministrationScope> GetUserAdministrationScopeAsync(CancellationToken ct = default);
    Task GetOrganizationListAccessAsync(Guid accountId, CancellationToken ct = default);
    Task<HomeReadScope> GetHomeReadScopeAsync(Guid accountId, CancellationToken ct = default);
    Task<AgreementReadScope> GetAgreementReadScopeAsync(Guid accountId, Guid agreementId, CancellationToken ct = default);
    Task<bool> CanAsync(string permission, AuthorizationResource resource, CancellationToken ct = default);
    Task<AgreementListScope> GetAgreementListScopeAsync(Guid accountId, CancellationToken ct = default);
}

using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.Security;

public sealed class AuthorizationService(ICurrentActor actor, IAuthorizationData data, TimeProvider clock) : IAuthorizationService
{
    public async Task<AccountActorScope> GetAccountActorScopeAsync(Guid accountId, CancellationToken ct = default)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated || current.AccountId != accountId) throw new UnauthorizedAccessException();
        var subject = await data.GetSubjectAsync(current.UserId, accountId, ct);
        if (!subject.IsActiveUser || !subject.IsMember) throw new UnauthorizedAccessException();
        return new(accountId, current.UserId, subject.Assignments.Where(x => x.AccountId == accountId).ToArray());
    }

    public async Task<UserAdministrationScope> GetUserAdministrationScopeAsync(CancellationToken ct = default)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated) throw new UnauthorizedAccessException();
        var subject = await data.GetSubjectAsync(current.UserId, current.AccountId, ct);
        if (!subject.IsActiveUser) throw new UnauthorizedAccessException();
        if (subject.IsPlatformAdmin) return new(current.UserId, current.AccountId, true);
        if (current.AccountId is not Guid account || !subject.IsMember || !subject.Assignments.Any(x =>
            x.AccountId == account && AccountWide(x) && LegacyRolePermissions.Grants(x, Permissions.Users.Manage)))
            throw new UnauthorizedAccessException();
        return new(current.UserId, account, false);
    }

    public async Task GetOrganizationListAccessAsync(Guid accountId, CancellationToken ct = default)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated || current.AccountId != accountId) throw new UnauthorizedAccessException();
        var subject = await data.GetSubjectAsync(current.UserId, accountId, ct);
        if (!subject.IsActiveUser || !subject.IsMember || !subject.Assignments.Any(x => x.AccountId == accountId &&
            LegacyRolePermissions.Grants(x, Permissions.Organizations.Read) &&
            (x.Role == UserRole.PropertyAdmin || x.Role == UserRole.AccountAdmin && AccountWide(x))))
            throw new UnauthorizedAccessException();
    }

    public async Task<HomeReadScope> GetHomeReadScopeAsync(Guid accountId, CancellationToken ct = default)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated || current.AccountId != accountId) throw new UnauthorizedAccessException();
        var subject = await data.GetSubjectAsync(current.UserId, accountId, ct);
        if (!subject.IsActiveUser || !subject.IsMember) throw new UnauthorizedAccessException();
        var assignments = subject.Assignments.Where(x => x.AccountId == accountId &&
            LegacyRolePermissions.Grants(x, Permissions.Navigation.Home)).ToArray();
        if (assignments.Length == 0) throw new UnauthorizedAccessException();
        return new(accountId, assignments.Any(x => x.Role == UserRole.AccountAdmin && AccountWide(x)),
            assignments.Where(x => x.Role == UserRole.PropertyAdmin && x.BuildingId.HasValue)
                .Select(x => x.BuildingId!.Value).Distinct().ToArray());
    }

    public async Task<AgreementListScope> GetAgreementListScopeAsync(Guid accountId, CancellationToken ct = default)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated || current.AccountId != accountId) throw new UnauthorizedAccessException();
        var subject = await data.GetSubjectAsync(current.UserId, accountId, ct);
        if (!subject.IsActiveUser || !subject.IsMember) throw new UnauthorizedAccessException();
        // Every active member can request a restricted list; rows still require ownership/grants.
        return new(accountId, current.UserId, subject.Assignments.Any(a => a.AccountId == accountId &&
            LegacyRolePermissions.Grants(a, Permissions.Agreements.ReadAll) && AccountWide(a)));
    }

    public async Task<AgreementReadScope> GetAgreementReadScopeAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated || current.AccountId != accountId) throw new UnauthorizedAccessException();
        var subject = await data.GetSubjectAsync(current.UserId, accountId, ct);
        if (!subject.IsActiveUser || !subject.IsMember) throw new UnauthorizedAccessException();
        // Agreement facts are date-independent; the reader owns its one reference timestamp.
        var facts = await data.GetResourceAsync(accountId, current.UserId, ResourceKind.Agreement, agreementId,
            default, ct);
        if (facts is null || facts.AccountId != accountId) throw new UnauthorizedAccessException();
        var assignments = subject.Assignments.Where(x => x.AccountId == accountId).ToArray();
        if (!AgreementAllowed(Permissions.Agreements.Read, current.UserId, assignments, facts)) throw new UnauthorizedAccessException();
        var list = new AgreementListScope(accountId, current.UserId, assignments.Any(a =>
            LegacyRolePermissions.Grants(a, Permissions.Agreements.ReadAll) && AccountWide(a)));
        return new(list, agreementId,
            AgreementAllowed(Permissions.Agreements.Update, current.UserId, assignments, facts),
            AgreementAllowed(Permissions.Agreements.ManageSharing, current.UserId, assignments, facts), facts.IsArchived,
            AgreementAllowed(Permissions.Agreements.Update, current.UserId, assignments, facts with { IsArchived = false }));
    }

    public async Task<bool> CanAsync(string permission, AuthorizationResource resource, CancellationToken ct = default)
    {
        if (!Permissions.All.Contains(permission)) return false;
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated) return false;
        var subject = await data.GetSubjectAsync(current.UserId, current.AccountId, ct);
        if (!subject.IsActiveUser) return false;
        if (permission.StartsWith("platform.", StringComparison.Ordinal))
        {
            if (!subject.IsPlatformAdmin) return false;
            return permission is Permissions.Platform.OpenAccounts or Permissions.Platform.CreateAccount
                ? resource.Kind == ResourceKind.Platform
                : resource.Kind == ResourceKind.PlatformAccount && resource.Id.HasValue && await data.AccountExistsAsync(resource.Id.Value, ct);
        }
        if (current.AccountId is not Guid account || resource.AccountId.HasValue && resource.AccountId != account || !subject.IsMember) return false;
        if (!Accepts(permission, resource.Kind)) return false;
        var assignments = subject.Assignments.Where(a => a.AccountId == account).ToArray();
        if (permission == Permissions.Agreements.List) return resource.Kind == ResourceKind.Account;
        if (permission == Permissions.Agreements.Open)
            return assignments.Any(a => LegacyRolePermissions.Grants(a, permission) && AccountWide(a)) ||
                await data.HasAgreementRelationshipAsync(account, current.UserId, ct);
        if (permission == Permissions.Leasing.Open)
            return assignments.Any(a => LegacyRolePermissions.Grants(a, permission)) || await data.HasLeasingOwnershipAsync(account, current.UserId, ct);
        if (permission == Permissions.McpConnections.Use) return resource.Kind == ResourceKind.Account && assignments.Any(a => LegacyRolePermissions.Grants(a, permission));
        if (permission == Permissions.MeetingRooms.Open) return true; // Existing use rule: account member.
        if (resource.Kind == ResourceKind.Account)
        {
            // Legacy module/list entry checks deliberately ignore narrower role scope. They authorize
            // opening the existing area, never an arbitrary resource action. Product rule remains open.
            if (LegacyAreaAccess(permission)) return assignments.Any(a => LegacyRolePermissions.Grants(a, permission));
            return assignments.Any(a => LegacyRolePermissions.Grants(a, permission) && AccountWide(a));
        }
        if (resource.Id is not Guid id) return false;
        var facts = await data.GetResourceAsync(account, current.UserId, resource.Kind, id,
            DateOnly.FromDateTime(clock.GetLocalNow().DateTime), ct);
        if (facts is null || facts.AccountId != account) return false;
        if (resource.Kind == ResourceKind.Agreement)
            return AgreementAllowed(permission, current.UserId, assignments, facts);
        return assignments.Any(a => LegacyRolePermissions.Grants(a, permission) && ResourceMatches(a, permission, resource.Kind, facts));
    }

    private static bool AgreementAllowed(string permission, Guid userId, IReadOnlyList<RoleAssignment> assignments, ResourceAccessFacts facts)
    {
        var admin = assignments.Any(a => LegacyRolePermissions.Grants(a, permission) && AccountWide(a));
        var owner = facts.OwnerUserId == userId;
        return permission switch
        {
            Permissions.Agreements.Read => admin || owner || facts.AgreementGrant.HasValue,
            Permissions.Agreements.Update => !facts.IsArchived && (admin || owner || facts.AgreementGrant == AgreementAccessLevel.Edit),
            Permissions.Agreements.ManageSharing => admin || owner,
            _ => false
        };
    }

    private static bool AccountWide(RoleAssignment a) => a.OrganizationId is null && a.BuildingId is null;
    internal static bool ResourceMatches(RoleAssignment a, string permission, ResourceKind kind, ResourceAccessFacts facts)
    {
        if (a.Role == UserRole.AccountAdmin) return ScopeMatches(a, facts);
        if (a.Role == UserRole.PropertyAdmin)
        {
            // Preserve the pre-existing broad organization-read exception explicitly.
            if (permission == Permissions.Organizations.Read) return true;
            if (permission == Permissions.ServiceDefinitions.Read) return true;
            return a.BuildingId.HasValue && a.BuildingId == facts.BuildingId &&
                (!a.OrganizationId.HasValue || a.OrganizationId == facts.OrganizationId);
        }
        if (a.Role is UserRole.TenantAdmin or UserRole.TenantUser)
        {
            if (kind == ResourceKind.ServiceDefinition) return facts.IsActive && facts.IsBookable;
            if (kind is ResourceKind.Building or ResourceKind.Unit)
                return a.OrganizationId.HasValue && (facts.TenantOrganizationIds?.Contains(a.OrganizationId.Value) ?? false) &&
                    (!a.BuildingId.HasValue || a.BuildingId == facts.BuildingId);
            return a.OrganizationId.HasValue && a.OrganizationId == facts.OrganizationId &&
                (!a.BuildingId.HasValue || a.BuildingId == facts.BuildingId);
        }
        return ScopeMatches(a, facts);
    }
    private static bool ScopeMatches(RoleAssignment a, ResourceAccessFacts f) =>
        (!a.BuildingId.HasValue || a.BuildingId == f.BuildingId) && (!a.OrganizationId.HasValue || a.OrganizationId == f.OrganizationId);

    // This compatibility exception must not be reused as an account-wide data scope.
    private static bool LegacyAreaAccess(string p) => p is Permissions.Buildings.Open or Permissions.Units.Open or
        Permissions.Occupancies.Open or Permissions.ServiceRequests.Open or Permissions.Navigation.Home or
        Permissions.Navigation.TenantPortal or Permissions.Navigation.ProviderPortal;

    private static bool Accepts(string p, ResourceKind k) => p switch
    {
        Permissions.Agreements.Read or Permissions.Agreements.Update or Permissions.Agreements.ManageSharing => k == ResourceKind.Agreement,
        Permissions.Buildings.Read or Permissions.Buildings.Update or Permissions.Buildings.Delete => k == ResourceKind.Building,
        Permissions.Organizations.Read or Permissions.Organizations.Update or Permissions.Organizations.Delete => k == ResourceKind.Organization,
        Permissions.Units.Create => k == ResourceKind.Building,
        Permissions.Units.Read or Permissions.Units.Update or Permissions.Units.Delete => k == ResourceKind.Unit,
        Permissions.Occupancies.Create => k == ResourceKind.Unit,
        Permissions.Occupancies.Read or Permissions.Occupancies.Update or Permissions.Occupancies.End or Permissions.Occupancies.Delete => k == ResourceKind.Occupancy,
        Permissions.ServiceDefinitions.Read or Permissions.ServiceDefinitions.Update or Permissions.ServiceDefinitions.Delete => k == ResourceKind.ServiceDefinition,
        Permissions.ServiceRequests.Approve or Permissions.ServiceRequests.Complete => k == ResourceKind.ServiceRequest,
        _ => k == ResourceKind.Account
    };
}

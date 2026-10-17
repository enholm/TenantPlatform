using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Authorization;

public sealed class AuthorizationData(IDbContextFactory<TenantPlatformDbContext> factory) : IAuthorizationData
{
    public async Task<AuthorizationSubject> GetSubjectAsync(Guid userId, Guid? accountId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.IsActive, x.IsPlatformAdmin }).SingleOrDefaultAsync(ct);
        if (user is null) return new(false, false, false, []);
        var member = accountId.HasValue && await db.UserAccounts.AnyAsync(x => x.UserId == userId && x.AccountId == accountId, ct);
        var roles = await db.UserAccountRoles.AsNoTracking()
            .Where(x => x.UserAccount.UserId == userId && x.UserAccount.AccountId == accountId)
            .Select(x => new RoleAssignment(x.Role, x.UserAccount.AccountId, x.OrganizationId, x.BuildingId)).ToListAsync(ct);
        return new(user.IsActive, member, user.IsPlatformAdmin, roles);
    }

    public async Task<ResourceAccessFacts?> GetResourceAsync(Guid accountId, Guid userId, ResourceKind kind, Guid id, DateOnly today, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        switch (kind)
        {
            case ResourceKind.Building:
            case ResourceKind.Unit:
                Guid building;
                if (kind == ResourceKind.Building)
                {
                    if (!await db.Buildings.AnyAsync(x => x.AccountId == accountId && x.Id == id, ct)) return null;
                    building = id;
                }
                else
                {
                    var unit = await db.Units.AsNoTracking().Where(x => x.AccountId == accountId && x.Id == id)
                        .Select(x => new { x.BuildingId }).SingleOrDefaultAsync(ct);
                    if (unit is null) return null;
                    building = unit.BuildingId;
                }
                var tenants = await (from o in db.Occupancies.AsNoTracking() join u in db.Units on o.UnitId equals u.Id
                    where o.AccountId == accountId && u.AccountId == accountId && u.BuildingId == building &&
                        (kind == ResourceKind.Building || u.Id == id) && o.ValidFrom <= today && (o.ValidTo == null || o.ValidTo >= today)
                    select o.TenantOrganizationId).Distinct().ToListAsync(ct);
                return new(accountId, BuildingId: building, TenantOrganizationIds: tenants);
            case ResourceKind.Organization:
                return await db.Organizations.AnyAsync(x => x.AccountId == accountId && x.Id == id, ct)
                    ? new(accountId, OrganizationId: id) : null;
            case ResourceKind.Occupancy:
                return await (from o in db.Occupancies.AsNoTracking() join u in db.Units on o.UnitId equals u.Id
                    where o.AccountId == accountId && u.AccountId == accountId && o.Id == id
                    select new ResourceAccessFacts(accountId, u.BuildingId, o.TenantOrganizationId, null, null, false, true, false, null)).SingleOrDefaultAsync(ct);
            case ResourceKind.ServiceRequest:
                return await db.ServiceRequests.AsNoTracking().Where(x => x.AccountId == accountId && x.Id == id)
                    .Select(x => new ResourceAccessFacts(accountId, x.BuildingId, x.RequesterOrganizationId, null, null, false, true, false, null)).SingleOrDefaultAsync(ct);
            case ResourceKind.ServiceDefinition:
                return await db.ServiceDefinitions.AsNoTracking().Where(x => x.AccountId == accountId && x.Id == id)
                    .Select(x => new ResourceAccessFacts(accountId, null, null, null, null, false, x.IsActive, x.IsBookableByTenant, null)).SingleOrDefaultAsync(ct);
            case ResourceKind.Agreement:
                var agreement = await db.Agreements.AsNoTracking().Where(x => x.AccountId == accountId && x.Id == id)
                    .Select(x => new { x.OwnerUserId, x.IsArchived }).SingleOrDefaultAsync(ct);
                if (agreement is null) return null;
                var grant = await db.AgreementAccess.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == id && x.UserId == userId)
                    .Select(x => (AgreementAccessLevel?)x.Level).SingleOrDefaultAsync(ct);
                return new(accountId, OwnerUserId: agreement.OwnerUserId, AgreementGrant: grant, IsArchived: agreement.IsArchived);
            default: return null;
        }
    }

    public async Task<bool> AccountExistsAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Accounts.AnyAsync(x => x.Id == accountId, ct);
    }
    public async Task<bool> HasAgreementRelationshipAsync(Guid accountId, Guid userId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Agreements.AnyAsync(x => x.AccountId == accountId && x.OwnerUserId == userId, ct) ||
            await db.AgreementAccess.AnyAsync(x => x.AccountId == accountId && x.UserId == userId, ct);
    }
    public async Task<bool> HasLeasingOwnershipAsync(Guid accountId, Guid userId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LeasingOrders.AnyAsync(x => x.AccountId == accountId && x.OwnerUserId == userId, ct) ||
            await db.LeasingFrameworks.AnyAsync(x => x.AccountId == accountId && x.OwnerUserId == userId, ct) ||
            await db.LeasingAcquisitions.AnyAsync(x => x.AccountId == accountId && x.OwnerUserId == userId, ct);
    }
}

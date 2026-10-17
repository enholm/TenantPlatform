using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Authentication;

public sealed class McpAccessDirectoryData(IDbContextFactory<TenantPlatformDbContext> factory) : IMcpAccessDirectory
{
    public async Task<McpAccessDirectory> ReadAsync(Guid account, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var members = await (from membership in db.UserAccounts.AsNoTracking() join user in db.Users on membership.UserId equals user.Id
            where membership.AccountId == account
            select new McpAccessMember(user.Id, user.FirstName + " " + user.LastName)).Distinct().ToArrayAsync(ct);
        var roles = await db.UserAccountRoles.AsNoTracking().Where(x => x.UserAccount.AccountId == account &&
                (x.OrganizationId == null || db.Organizations.Any(o => o.AccountId == account && o.Id == x.OrganizationId)) &&
                (x.BuildingId == null || db.Buildings.Any(b => b.AccountId == account && b.Id == x.BuildingId)))
            .Select(x => new RoleAssignment(x.Role, account, x.OrganizationId, x.BuildingId)).Distinct().ToArrayAsync(ct);
        var organizations = await db.Organizations.AsNoTracking().Where(x => x.AccountId == account).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var buildings = await db.Buildings.AsNoTracking().Where(x => x.AccountId == account).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var scopes = roles.Where(x => x.OrganizationId.HasValue || x.BuildingId.HasValue).Select(x => new McpScopeLabel(x.OrganizationId, x.BuildingId,
            string.Join(", ", new[] { x.OrganizationId is Guid org ? organizations.GetValueOrDefault(org) : null,
                x.BuildingId is Guid building ? buildings.GetValueOrDefault(building) : null }.Where(n => !string.IsNullOrEmpty(n))))).Distinct().ToArray();
        return new(members.OrderBy(x => x.Name).ThenBy(x => x.UserId).ToArray(), roles, scopes);
    }
}

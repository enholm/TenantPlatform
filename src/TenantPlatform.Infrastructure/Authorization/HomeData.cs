using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Dashboard;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Authorization;

public sealed class HomeData(IDbContextFactory<TenantPlatformDbContext> factory) : IHomeData
{
    public async Task<HomeOverview> ReadAsync(HomeReadScope scope, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var account = scope.AccountId; var buildings = scope.BuildingIds.ToArray();
        var name = await db.Accounts.AsNoTracking().Where(x => x.Id == account).Select(x => x.Name).SingleOrDefaultAsync(ct);
        var buildingCount = await db.Buildings.CountAsync(x => x.AccountId == account && (scope.CanReadAllBuildings || buildings.Contains(x.Id)), ct);
        // Preserve Home's legacy account-wide organization count explicitly; this is not a detail/read scope.
        var organizations = await db.Organizations.CountAsync(x => x.AccountId == account, ct);
        var units = await db.Units.CountAsync(x => x.AccountId == account && (scope.CanReadAllBuildings || buildings.Contains(x.BuildingId)), ct);
        var requests = db.ServiceRequests.AsNoTracking().Where(x => x.AccountId == account && (scope.CanReadAllBuildings || buildings.Contains(x.BuildingId)));
        var open = await requests.CountAsync(x => x.Status != ServiceRequestStatus.Completed && x.Status != ServiceRequestStatus.Cancelled &&
            x.Status != ServiceRequestStatus.Rejected && x.Status != ServiceRequestStatus.Failed, ct);
        var recent = await (from request in requests.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(10)
            join userRow in db.Users.Where(u => db.UserAccounts.Any(m => m.AccountId == account && m.UserId == u.Id))
                on request.RequesterUserId equals userRow.Id into users
            from user in users.DefaultIfEmpty()
            select new RecentServiceRequest(request.CreatedAt, request.Status.ToString(), user == null ? "" : user.FirstName + " " + user.LastName)).ToListAsync(ct);
        return new(name, buildingCount, organizations, units, open, recent);
    }
}

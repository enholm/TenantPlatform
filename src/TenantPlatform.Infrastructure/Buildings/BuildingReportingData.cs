using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Buildings;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Buildings;

public sealed class BuildingReportingData(IDbContextFactory<TenantPlatformDbContext> factory) : IBuildingReportingData
{
    private static IQueryable<Building> Filter(TenantPlatformDbContext db, BuildingReportScope scope, BuildingReportFilter filter)
    {
        var query = db.Buildings.AsNoTracking().Where(b => b.AccountId == scope.AccountId);
        if (!string.IsNullOrEmpty(filter.Search))
        {
            var search = filter.Search.ToLowerInvariant();
            query = query.Where(b => b.Name.ToLower().Contains(search));
        }
        return query;
    }
    public async Task<BuildingReportPage> ListAsync(BuildingReportScope scope, BuildingReportFilter filter, int page, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Filter(db, scope, filter);
        var count = await query.CountAsync(ct);
        var actual = Math.Clamp(page, 1, Math.Max(1, (int)(((long)count + 24) / 25)));
        var items = await query.OrderBy(b => b.Name).ThenBy(b => b.Id).Skip((actual - 1) * 25).Take(25)
            .Select(b => new BuildingReportRow(b.Id, b.Name, b.AddressLine1, b.PostalCode, b.City)).ToArrayAsync(ct);
        return new(actual, count, items);
    }
    public async Task<BuildingReportCounts> CountAsync(BuildingReportScope scope, BuildingReportFilter filter, bool city, int maxGroups, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Filter(db, scope, filter);
        if (!city) return new(await query.CountAsync(ct), []);
        // C collation makes grouping exact/case-sensitive regardless of the database default collation.
        // Only null/empty collapse: whitespace is a real, distinct stored value.
        var groups = await query.GroupBy(b => EF.Functions.Collate(b.City == null || EF.Functions.Collate(b.City, "C") == "" ? null : b.City, "C"))
            .OrderBy(g => g.Key).Select(g => new { City = g.Key, Count = g.Count() }).Take(maxGroups + 1).ToArrayAsync(ct);
        if (groups.Length > maxGroups) throw new BuildingReportLimitException();
        return new(groups.Sum(g => g.Count), groups.Select(g => new BuildingReportGroup(g.City is null ? "missing" : "city:" + g.City, g.City, g.Count)).ToArray());
    }
}

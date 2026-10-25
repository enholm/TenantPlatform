using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.ServiceRequests;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.ServiceRequests;

public sealed class ServiceRequestReportingData(IDbContextFactory<TenantPlatformDbContext> factory) : IServiceRequestReportingData
{
    private static IQueryable<ServiceRequest> Filter(TenantPlatformDbContext db, ServiceRequestReadScope scope, ServiceRequestReportFilter filter)
    {
        var query = ServiceRequestData.Visible(db, scope, null);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.ToLowerInvariant();
            query = query.Where(r => r.Title != null && r.Title.ToLower().Contains(search));
        }
        if (filter.Status.HasValue) query = query.Where(r => r.Status == filter.Status);
        if (filter.BuildingId.HasValue) query = query.Where(r => r.BuildingId == filter.BuildingId && db.Buildings.Any(b => b.AccountId == scope.AccountId && b.Id == r.BuildingId));
        if (filter.MissingBuilding) query = query.Where(r => !db.Buildings.Any(b => b.AccountId == scope.AccountId && b.Id == r.BuildingId));
        return query;
    }
    public async Task<ServiceRequestReportPage> ListAsync(ServiceRequestReadScope scope, ServiceRequestReportFilter filter, int page, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Filter(db, scope, filter);
        var count = await query.CountAsync(ct);
        var actual = Math.Clamp(page, 1, Math.Max(1, (int)(((long)count + 24) / 25)));
        var slice = query.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).Skip((actual - 1) * 25).Take(25);
        var rows = await (from r in slice
            join b in db.Buildings.Where(b => b.AccountId == scope.AccountId) on r.BuildingId equals b.Id into buildings
            from b in buildings.DefaultIfEmpty()
            join u in db.Units.Where(u => u.AccountId == scope.AccountId) on r.UnitId equals u.Id into units
            from u in units.DefaultIfEmpty()
            orderby r.CreatedAt descending, r.Id
            select new ServiceRequestReportRow(r.Id, r.Title, r.Status.ToString(), r.CreatedAt, r.SubmittedAt, r.CompletedAt,
                b == null ? null : new ServiceRequestReportReference(b.Id, b.Name), u == null ? null : new ServiceRequestReportReference(u.Id, u.Name))).ToArrayAsync(ct);
        return new(page, actual, count, rows);
    }
    public async Task<ServiceRequestReportCounts> CountAsync(ServiceRequestReadScope scope, ServiceRequestReportFilter filter, ServiceRequestReportGrouping grouping, int maxGroups, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Filter(db, scope, filter);
        ServiceRequestReportGroup[] groups;
        if (grouping == ServiceRequestReportGrouping.Status)
        {
            var values = await query.GroupBy(r => r.Status).OrderBy(g => g.Key).Select(g => new { Status = g.Key, Count = g.Count() }).Take(maxGroups + 1).ToArrayAsync(ct);
            groups = values.Select(g => new ServiceRequestReportGroup(g.Status.ToString(), g.Status.ToString(), g.Count)).ToArray();
        }
        else
        {
            var values = await (from r in query
                join b in db.Buildings.Where(b => b.AccountId == scope.AccountId) on r.BuildingId equals b.Id into buildings
                from b in buildings.DefaultIfEmpty()
                group r by new { Id = b == null ? (Guid?)null : b.Id, Name = b == null ? null : b.Name } into g
                orderby g.Key.Id
                select new { g.Key.Id, g.Key.Name, Count = g.Count() }).Take(maxGroups + 1).ToArrayAsync(ct);
            groups = values.Select(g => new ServiceRequestReportGroup(g.Id?.ToString("D") ?? "missing", g.Name, g.Count)).ToArray();
        }
        if (groups.Length > maxGroups) throw new ServiceRequestReportLimitException();
        return new(groups.Sum(g => g.Count), groups);
    }
}

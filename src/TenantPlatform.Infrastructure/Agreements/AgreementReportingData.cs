using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementReportingData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementReportingData
{
    public async Task<string> TimeZoneAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await AgreementReadQueries.ReminderTimeZoneAsync(db, accountId, ct);
    }
    private static IQueryable<Agreement> Filter(TenantPlatformDbContext db, AgreementListScope scope, AgreementReportSelection selection)
    {
        var f = selection.Filter;
        var query = AgreementReadQueries.Visible(db, scope);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var search = f.Search.ToLowerInvariant();
            query = query.Where(a => a.Title.ToLower().Contains(search) || db.Organizations.Any(o => o.AccountId == scope.AccountId && o.Id == a.CounterpartyOrganizationId && o.Name.ToLower().Contains(search)));
        }
        if (f.Status.HasValue) query = query.Where(a => a.Status == f.Status);
        if (f.Type.HasValue) query = query.Where(a => a.Type == f.Type);
        if (f.Archive != AgreementArchiveSelection.Include) query = query.Where(a => a.IsArchived == (f.Archive == AgreementArchiveSelection.Only));
        if (f.EndDateFrom.HasValue) query = query.Where(a => a.EndDate >= f.EndDateFrom);
        if (f.EndDateTo.HasValue) query = query.Where(a => a.EndDate <= f.EndDateTo);
        if (f.BuildingId.HasValue) query = query.Where(a => a.BuildingId == f.BuildingId && db.Buildings.Any(b => b.AccountId == scope.AccountId && b.Id == a.BuildingId));
        if (f.MissingBuilding) query = query.Where(a => !db.Buildings.Any(b => b.AccountId == scope.AccountId && b.Id == a.BuildingId));
        if (f.UnitId.HasValue) query = query.Where(a => a.UnitId == f.UnitId && db.Units.Any(b => b.AccountId == scope.AccountId && b.Id == a.UnitId));
        if (f.OrganizationElementId.HasValue) query = query.Where(a => a.OrganizationElementId == f.OrganizationElementId && db.OrganizationElements.Any(b => b.AccountId == scope.AccountId && b.Id == a.OrganizationElementId));
        if (f.GeographicAreaId.HasValue) query = query.Where(a => a.GeographicAreaId == f.GeographicAreaId && db.GeographicAreas.Any(b => b.AccountId == scope.AccountId && b.Id == a.GeographicAreaId));
        return query;
    }
    // Every metadata join is tenant restricted and left-joined, preserving rows with invalid legacy references.
    private static IQueryable<AgreementReportRow> Rows(TenantPlatformDbContext db, AgreementListScope scope, IQueryable<Agreement> query) =>
        from a in query
        join counterpart in db.Organizations.Where(x => x.AccountId == scope.AccountId) on a.CounterpartyOrganizationId equals counterpart.Id into counterpartRows
        from counterpart in counterpartRows.DefaultIfEmpty()
        join building in db.Buildings.Where(x => x.AccountId == scope.AccountId) on a.BuildingId equals building.Id into buildings
        from building in buildings.DefaultIfEmpty()
        join unit in db.Units.Where(x => x.AccountId == scope.AccountId) on a.UnitId equals unit.Id into units
        from unit in units.DefaultIfEmpty()
        join element in db.OrganizationElements.Where(x => x.AccountId == scope.AccountId) on a.OrganizationElementId equals element.Id into elements
        from element in elements.DefaultIfEmpty()
        join area in db.GeographicAreas.Where(x => x.AccountId == scope.AccountId) on a.GeographicAreaId equals area.Id into areas
        from area in areas.DefaultIfEmpty()
        orderby a.Title, a.Id
        select new AgreementReportRow(a.Id, a.Title,
            counterpart == null ? null : new AgreementReference(counterpart.Id, counterpart.Name), a.Type.ToString(), a.Status.ToString(),
            a.StartDate, a.EndDate, a.NoticeDeadline, a.RenewalDate, a.IsArchived,
            building == null ? null : new AgreementReference(building.Id, building.Name), unit == null ? null : new AgreementReference(unit.Id, unit.Name),
            element == null ? null : new AgreementReference(element.Id, element.Name), area == null ? null : new AgreementReference(area.Id, area.Name));

    public async Task<AgreementReportPage> ListAsync(AgreementListScope scope, AgreementReportSelection selection, int requestedPage, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Filter(db, scope, selection);
        var count = await query.CountAsync(ct);
        const int size = 25;
        var page = Math.Clamp(requestedPage, 1, Math.Max(1, (int)(((long)count + size - 1) / size)));
        var slice = query.OrderBy(a => a.Title).ThenBy(a => a.Id).Skip((page - 1) * size).Take(size);
        return new(requestedPage, page, size, count, await Rows(db, scope, slice).ToArrayAsync(ct));
    }
    public async Task<AgreementReportCounts> CountAsync(AgreementListScope scope, AgreementReportSelection selection, AgreementReportGrouping grouping, int maxGroups, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Filter(db, scope, selection);
        // Group all authorized matches in SQL; take one extra group solely to detect overflow.
        AgreementReportGroup[] groups;
        if (grouping == AgreementReportGrouping.Building)
        {
            var grouped = await (from a in query
                join b in db.Buildings.Where(b => b.AccountId == scope.AccountId) on a.BuildingId equals b.Id into buildings
                from b in buildings.DefaultIfEmpty()
                group a by new { Id = b == null ? (Guid?)null : b.Id, Name = b == null ? null : b.Name } into g
                orderby g.Key.Id
                select new { g.Key.Id, g.Key.Name, Count = g.Count() }).Take(maxGroups + 1).ToArrayAsync(ct);
            groups = grouped.Select(g => new AgreementReportGroup(g.Id?.ToString("D") ?? "missing", g.Name, g.Count)).ToArray();
        }
        else
        {
            var grouped = await query.GroupBy(a => a.Status).OrderBy(g => g.Key).Select(g => new { Status = g.Key, Count = g.Count() }).Take(maxGroups + 1).ToArrayAsync(ct);
            groups = grouped.Select(g => new AgreementReportGroup(g.Status.ToString(), g.Status.ToString(), g.Count)).ToArray();
        }
        if (groups.Length > maxGroups) throw new AgreementReportLimitException();
        // Single-valued dimensions partition the filtered set. This avoids a second, racing total query.
        return new(groups.Sum(g => g.Count), groups);
    }
    public async Task<AgreementReportRow> DetailsAsync(AgreementReadScope scope, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Rows(db, scope.ListScope, AgreementReadQueries.Visible(db, scope.ListScope).Where(a => a.Id == scope.AgreementId))
            .SingleOrDefaultAsync(ct) ?? throw new UnauthorizedAccessException();
    }
}

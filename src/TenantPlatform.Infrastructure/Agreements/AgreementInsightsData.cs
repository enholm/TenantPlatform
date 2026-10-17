using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementInsightsData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementInsightsData
{
    public async Task<IReadOnlyList<AgreementInsightGroup>> GetGroupsAsync(AgreementListScope scope, AgreementInsightDimension dimension, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var visible = AgreementReadQueries.Visible(db, scope).Where(x => !x.IsArchived);
        if (dimension == AgreementInsightDimension.Type)
        {
            var counts = await visible.GroupBy(x => x.Type).Select(g => new { Type = g.Key, Count = g.Count() }).ToListAsync(ct);
            return counts.OrderBy(x => x.Type).Select(x => new AgreementInsightGroup(
                x.Type.ToString(), null, Enum.IsDefined(x.Type) ? x.Type : null, x.Count)).ToList();
        }
        if (dimension == AgreementInsightDimension.Status)
        {
            var counts = await visible.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
            return counts.OrderBy(x => x.Status).Select(x => new AgreementInsightGroup(
                $"status-{x.Status}", null, null, x.Count, Status: Enum.IsDefined(x.Status) ? x.Status : null)).ToList();
        }
        if (dimension == AgreementInsightDimension.Owner)
        {
            var members = db.Users.Where(u => db.UserAccounts.Any(m => m.AccountId == scope.AccountId && m.UserId == u.Id));
            var counts = await (from a in visible
                join u in members on a.OwnerUserId equals u.Id into owners
                from u in owners.DefaultIfEmpty()
                group a by new { Id = u == null ? (Guid?)null : u.Id,
                    Name = u == null ? null : (u.FirstName + " " + u.LastName).Trim() } into g
                select new { g.Key.Id, g.Key.Name, Count = g.Count() }).ToListAsync(ct);
            return counts.OrderByDescending(x => x.Count).ThenBy(x => x.Name).ThenBy(x => x.Id).Select(x =>
                new AgreementInsightGroup(x.Id?.ToString() ?? "missing", x.Name, null, x.Count)).ToList();
        }
        if (dimension == AgreementInsightDimension.Counterparty)
        {
            var counts = await (from a in visible
                join p in db.Organizations.Where(x => x.AccountId == scope.AccountId) on a.CounterpartyOrganizationId equals p.Id into parties
                from p in parties.DefaultIfEmpty()
                group a by new { Id = p == null ? (Guid?)null : p.Id, Name = p == null ? null : p.Name } into g
                select new { g.Key.Id, g.Key.Name, Count = g.Count() }).ToListAsync(ct);
            return counts.OrderByDescending(x => x.Count).ThenBy(x => x.Name).ThenBy(x => x.Id).Select(x =>
                new AgreementInsightGroup(x.Id?.ToString() ?? "missing", x.Name, null, x.Count)).ToList();
        }
        throw new ArgumentOutOfRangeException(nameof(dimension));
    }

    public async Task<AgreementHierarchyData> GetHierarchyAsync(AgreementListScope scope, AgreementInsightDimension dimension, CancellationToken ct)
    {
        if (dimension is not (AgreementInsightDimension.Geography or AgreementInsightDimension.Organization)) throw new ArgumentOutOfRangeException(nameof(dimension));
        await using var db = await factory.CreateDbContextAsync(ct);
        var geography = dimension == AgreementInsightDimension.Geography;
        var nodes = geography
            ? await db.GeographicAreas.AsNoTracking().Where(x => x.AccountId == scope.AccountId)
                .Select(x => new AgreementHierarchyNode(x.Id, x.ParentId, x.Name)).ToListAsync(ct)
            : await db.OrganizationElements.AsNoTracking().Where(x => x.AccountId == scope.AccountId)
                .Select(x => new AgreementHierarchyNode(x.Id, x.ParentId, x.Name)).ToListAsync(ct);
        var counts = await AgreementReadQueries.Visible(db, scope).Where(x => !x.IsArchived)
            .GroupBy(x => geography ? x.GeographicAreaId : x.OrganizationElementId)
            .Select(g => new AgreementHierarchyCount(g.Key, g.Count())).ToListAsync(ct);
        return new(nodes, counts);
    }
}

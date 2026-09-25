using Microsoft.EntityFrameworkCore;
using TenantPlatform.Web.Services.AccountSettings;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService
{
    public async Task<AgreementAttentionDto> GetAttentionAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (userId, admin) = await RequireMemberAsync(db, accountId, ct);
        var today = AgreementReminderSchedule.Today(Clock.GetUtcNow(), (await SettingsAsync(db, accountId, ct)).TimeZoneId);
        var visible = Accessible(db, accountId, userId, admin).AsNoTracking();
        return new(today,
            await AgreementDashboardQueries.Attention(visible, AgreementAttention.Notice, today).CountAsync(ct),
            await AgreementDashboardQueries.Attention(visible, AgreementAttention.Renewal, today).CountAsync(ct),
            await AgreementDashboardQueries.Attention(visible, AgreementAttention.Expired, today).CountAsync(ct));
    }

    public async Task<AgreementDashboardList> GetDashboardContractsAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (userId, admin) = await RequireMemberAsync(db, accountId, ct);
        var today = AgreementReminderSchedule.Today(Clock.GetUtcNow(), (await SettingsAsync(db, accountId, ct)).TimeZoneId);
        var items = await AgreementDashboardQueries.Prioritized(Accessible(db, accountId, userId, admin).AsNoTracking(), today)
            .Select(x => new AgreementDashboardItem(x.Id, x.Title, x.Type, x.EndDate)).ToListAsync(ct);
        return new(today, items);
    }

    public async Task<AgreementInsightsDto> GetInsightsAsync(Guid accountId, AgreementInsightDimension dimension,
        Guid? selected = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(dimension)) throw new ArgumentOutOfRangeException(nameof(dimension));
        await using var db = await factory.CreateDbContextAsync(ct);
        var (userId, admin) = await RequireMemberAsync(db, accountId, ct);
        var visible = Accessible(db, accountId, userId, admin).AsNoTracking().Where(x => !x.IsArchived);
        if (dimension == AgreementInsightDimension.Type)
        {
            var counts = await visible.GroupBy(x => x.Type).Select(g => new { Type = g.Key, Count = g.Count() }).ToListAsync(ct);
            return new(counts.OrderBy(x => x.Type).Select(x => new AgreementInsightGroup(
                x.Type.ToString(), null, Enum.IsDefined(x.Type) ? x.Type : null, x.Count)).ToList(), []);
        }
        if (dimension == AgreementInsightDimension.Status)
        {
            var counts = await visible.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
            return new(counts.OrderBy(x => x.Status).Select(x => new AgreementInsightGroup(
                $"status-{x.Status}", null, null, x.Count, Status: Enum.IsDefined(x.Status) ? x.Status : null)).ToList(), []);
        }
        if (dimension == AgreementInsightDimension.Owner)
        {
            var members = db.Users.Where(u => db.UserAccounts.Any(m => m.AccountId == accountId && m.UserId == u.Id));
            var counts = await (from a in visible
                join u in members on a.OwnerUserId equals u.Id into owners
                from u in owners.DefaultIfEmpty()
                group a by new { Id = u == null ? (Guid?)null : u.Id,
                    Name = u == null ? null : (u.FirstName + " " + u.LastName).Trim() } into g
                select new { g.Key.Id, g.Key.Name, Count = g.Count() }).ToListAsync(ct);
            return new(counts.OrderByDescending(x => x.Count).ThenBy(x => x.Name).Select(x =>
                new AgreementInsightGroup(x.Id?.ToString() ?? "missing", x.Name, null, x.Count)).ToList(), []);
        }
        if (dimension == AgreementInsightDimension.Counterparty)
        {
            var counts = await (from a in visible
                join p in db.Organizations.Where(x => x.AccountId == accountId) on a.CounterpartyOrganizationId equals p.Id into parties
                from p in parties.DefaultIfEmpty()
                group a by new { Id = p == null ? (Guid?)null : p.Id, Name = p == null ? null : p.Name } into g
                select new { g.Key.Id, g.Key.Name, Count = g.Count() }).ToListAsync(ct);
            return new(counts.OrderByDescending(x => x.Count).ThenBy(x => x.Name).Select(x =>
                new AgreementInsightGroup(x.Id?.ToString() ?? "missing", x.Name, null, x.Count)).ToList(), []);
        }
        var geography = dimension == AgreementInsightDimension.Geography;
        var nodes = geography
            ? await db.GeographicAreas.AsNoTracking().Where(x => x.AccountId == accountId)
                .Select(x => new { x.Id, x.ParentId, x.Name }).ToListAsync(ct)
            : await db.OrganizationElements.AsNoTracking().Where(x => x.AccountId == accountId)
                .Select(x => new { x.Id, x.ParentId, x.Name }).ToListAsync(ct);
        var assignments = await visible.Select(x => geography ? x.GeographicAreaId : x.OrganizationElementId).ToListAsync(ct);
        var rows = AccountRegisterHierarchy.Build(nodes.Select(x => (x.Id, x.ParentId, x.Name)));
        if (!admin)
        {
            // Do not expose names of branches unrelated to the caller's accessible agreements.
            var assigned = assignments.OfType<Guid>().ToHashSet();
            var allowed = rows.Where(x => assigned.Contains(x.Id)).SelectMany(x => x.Ancestors.Append(x.Id)).ToHashSet();
            rows = rows.Where(x => allowed.Contains(x.Id)).ToList();
        }
        return new(AgreementDashboardQueries.GroupHierarchy(assignments, rows, selected), rows);
    }
}

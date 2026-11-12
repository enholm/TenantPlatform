using Microsoft.EntityFrameworkCore;
using System.Globalization;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Infrastructure.Persistence;

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

    public async Task<AgreementInsightsDto> GetInsightsAsync(Guid accountId, AgreementInsightGrouping grouping,
        Guid? selected = null, CancellationToken ct = default)
    {
        grouping.Validate();
        var dimension = grouping.Fixed;
        if (dimension.HasValue && selected.HasValue) throw new UnauthorizedAccessException();
        await using var db = await factory.CreateDbContextAsync(ct);
        var (userId, admin) = await RequireMemberAsync(db, accountId, ct);
        var visible = Accessible(db, accountId, userId, admin).AsNoTracking().Where(x => !x.IsArchived);
        var total = dimension.HasValue ? await visible.CountAsync(ct) : 0;
        if (dimension == AgreementInsightDimension.Type)
        {
            var counts = await visible.GroupBy(x => x.Type).Select(g => new { Type = g.Key, Count = g.Count() }).ToListAsync(ct);
            return FixedInsights(counts.OrderBy(x => x.Type).Select(x => new AgreementInsightGroup(
                x.Type.ToString(), null, Enum.IsDefined(x.Type) ? x.Type : null, x.Count)).ToList(), total);
        }
        if (dimension == AgreementInsightDimension.Status)
        {
            var counts = await visible.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
            return FixedInsights(counts.OrderBy(x => x.Status).Select(x => new AgreementInsightGroup(
                $"status-{x.Status}", null, null, x.Count, Status: Enum.IsDefined(x.Status) ? x.Status : null)).ToList(), total);
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
            return FixedInsights(counts.OrderByDescending(x => x.Count).ThenBy(x => x.Name).Select(x =>
                new AgreementInsightGroup(x.Id?.ToString() ?? "missing", x.Name, null, x.Count)).ToList(), total);
        }
        if (dimension == AgreementInsightDimension.Counterparty)
        {
            var counts = await (from a in visible
                join p in db.Organizations.Where(x => x.AccountId == accountId) on a.CounterpartyOrganizationId equals p.Id into parties
                from p in parties.DefaultIfEmpty()
                group a by new { Id = p == null ? (Guid?)null : p.Id, Name = p == null ? null : p.Name } into g
                select new { g.Key.Id, g.Key.Name, Count = g.Count() }).ToListAsync(ct);
            return FixedInsights(counts.OrderByDescending(x => x.Count).ThenBy(x => x.Name).Select(x =>
                new AgreementInsightGroup(x.Id?.ToString() ?? "missing", x.Name, null, x.Count)).ToList(), total);
        }
        var definition = await AvailableInsightDimensions(db, accountId).Where(x => x.Id == grouping.DimensionId)
            .Select(x => new Dimension { Id = x.Id, AccountId = x.AccountId, Name = x.Name, Code = x.Code,
                IsActive = x.IsActive, LeafOnly = x.LeafOnly, AllowMultiple = x.AllowMultiple }).SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedAccessException();
        var values = await db.DimensionValues.AsNoTracking().Where(x => x.AccountId == accountId && x.DimensionId == definition.Id)
            .Select(x => new DimensionValue { Id = x.Id, AccountId = x.AccountId, DimensionId = x.DimensionId,
                ParentId = x.ParentId, Name = x.Name, Code = x.Code, SortOrder = x.SortOrder, IsActive = x.IsActive }).ToListAsync(ct);
        // One authorized left join supplies both the unique visible set and explicit assignments, including unassigned agreements.
        var projected = await (from a in visible
            join assignment in db.AgreementDimensionAssignments.AsNoTracking()
                .Where(x => x.AccountId == accountId && x.DimensionId == definition.Id)
                on a.Id equals assignment.AgreementId into selections
            from assignment in selections.DefaultIfEmpty()
            select new { AgreementId = a.Id, ValueId = assignment == null ? (Guid?)null : assignment.ValueId }).ToListAsync(ct);
        return AgreementInsightHierarchy.Aggregate(definition, values, projected.Select(x => x.AgreementId),
            projected.Where(x => x.ValueId.HasValue).Select(x => new AgreementInsightAssignment(x.AgreementId, x.ValueId!.Value)), selected, admin);
    }

    public async Task<List<AgreementInsightDimensionOption>> GetInsightDimensionsAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireMemberAsync(db, accountId, ct);
        var language = await db.Accounts.AsNoTracking().Where(x => x.Id == accountId).Select(x => x.DefaultLanguage).SingleAsync(ct);
        var definitions = await AvailableInsightDimensions(db, accountId).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new Dimension { Id = x.Id, AccountId = x.AccountId, Name = x.Name, Code = x.Code,
                Translations = x.Translations.Where(t => t.AccountId == accountId).Select(t => new DimensionTranslation
                { Id = t.Id, AccountId = t.AccountId, DimensionId = t.DimensionId, LanguageCode = t.LanguageCode, Name = t.Name }).ToList() }).ToListAsync(ct);
        return definitions.Select(x => new AgreementInsightDimensionOption(x.Id, DimensionLocalization.Name(x, CultureInfo.CurrentUICulture.Name, language))).ToList();
    }

    private static IQueryable<Dimension> AvailableInsightDimensions(TenantPlatformDbContext db, Guid accountId) =>
        db.Dimensions.AsNoTracking().Where(x => x.AccountId == accountId && x.IsActive &&
            db.AgreementDimensionRules.Any(r => r.AccountId == accountId && r.DimensionId == x.Id && r.IsEnabled));

    private static AgreementInsightsDto FixedInsights(List<AgreementInsightGroup> groups, int total) =>
        new(groups, [], total, total);
}

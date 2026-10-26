using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementReadExtensionData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementReadExtensionData
{
    private static int Actual(int requested, int total) => Math.Min(requested, Math.Max(1, (int)(((long)total + 24) / 25)));
    public async Task<AgreementBusinessDetails> DetailsAsync(AgreementReadScope scope, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var account = scope.ListScope.AccountId;
        var fields = await (from a in AgreementReadQueries.Visible(db, scope.ListScope).Where(a => a.Id == scope.AgreementId)
            join index in db.AgreementIndexRecords.AsNoTracking().Where(i => i.AccountId == account) on a.IndexId equals index.Id into indices
            from index in indices.DefaultIfEmpty()
            select new AgreementBusinessFields(a.Description, a.Terms, a.Direction, a.Currency,
                index == null ? null : new AgreementReference(index.Id, index.Name), a.IndexSetupNeedsReview,
                a.Form, a.NoticeMode, a.NoticeCount, a.NoticeUnit, a.CurrentPeriodStartDate, a.AutoRenew, a.RenewalMonths,
                a.TerminationEffectiveDate, a.TerminationNoticeCount, a.TerminationNoticeUnit, a.CessationDate,
                a.TerminationRegisteredUtc, a.DeadlinesInitialized, a.CreatedUtc, a.UpdatedUtc)).SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedAccessException();
        // Same trusted scope and tenant-restricted joins as the existing summary; no GUI DTO.
        var summary = await new AgreementReportingData(factory).DetailsAsync(scope, ct);
        return new(summary, fields);
    }
    public async Task<AgreementEventPage> HistoryAsync(AgreementReadScope scope, int page, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var visible = AgreementReadQueries.Visible(db, scope.ListScope).Where(a => a.Id == scope.AgreementId);
        if (!await visible.AnyAsync(ct)) throw new UnauthorizedAccessException();
        var query = from h in db.AgreementDeadlineHistory.AsNoTracking()
            join d in db.AgreementDeadlines.AsNoTracking() on h.DeadlineId equals d.Id
            where h.AccountId == scope.ListScope.AccountId && d.AccountId == scope.ListScope.AccountId && visible.Any(a => a.Id == d.AgreementId)
            select h;
        var total = await query.CountAsync(ct); var actual = Actual(page, total);
        var rows = await query.OrderBy(h => h.CreatedUtc).ThenBy(h => h.Id).Skip((actual - 1) * 25).Take(25)
            .Select(h => new FollowupEvent(h.Id, h.DeadlineId, h.CreatedUtc, h.Kind)).ToArrayAsync(ct);
        return new(actual, total, rows);
    }
    public async Task<AgreementRelationPage> RelationsAsync(AgreementListScope scope, AgreementRelationKind kind, string? search, int page, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var visible = AgreementReadQueries.Visible(db, scope);
        var account = scope.AccountId;
        var query = kind switch
        {
            AgreementRelationKind.Counterparty => db.Organizations.AsNoTracking().Where(r => r.AccountId == account && visible.Any(a => a.CounterpartyOrganizationId == r.Id)).Select(r => new { r.Id, r.Name }),
            AgreementRelationKind.Building => db.Buildings.AsNoTracking().Where(r => r.AccountId == account && visible.Any(a => a.BuildingId == r.Id)).Select(r => new { r.Id, r.Name }),
            AgreementRelationKind.Unit => db.Units.AsNoTracking().Where(r => r.AccountId == account && visible.Any(a => a.UnitId == r.Id)).Select(r => new { r.Id, r.Name }),
            AgreementRelationKind.OrganizationElement => db.OrganizationElements.AsNoTracking().Where(r => r.AccountId == account && visible.Any(a => a.OrganizationElementId == r.Id)).Select(r => new { r.Id, r.Name }),
            AgreementRelationKind.GeographicArea => db.GeographicAreas.AsNoTracking().Where(r => r.AccountId == account && visible.Any(a => a.GeographicAreaId == r.Id)).Select(r => new { r.Id, r.Name }),
            _ => throw new ArgumentException()
        };
        if (!string.IsNullOrEmpty(search)) { var term = search.ToLowerInvariant(); query = query.Where(r => r.Name.ToLower().Contains(term)); }
        var total = await query.CountAsync(ct); var actual = Actual(page, total);
        return new(actual, total, await query.OrderBy(r => r.Name).ThenBy(r => r.Id).Skip((actual - 1) * 25).Take(25).Select(r => new AgreementRelation(r.Id, r.Name)).ToArrayAsync(ct));
    }
}

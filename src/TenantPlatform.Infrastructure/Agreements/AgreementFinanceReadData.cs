using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementFinanceReadData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementFinanceReadData
{
    private static IQueryable<AgreementLineVersion> Versions(TenantPlatformDbContext db, AgreementReadScope s) =>
        db.AgreementLineVersions.AsNoTracking().Where(v => v.AccountId == s.ListScope.AccountId && v.AgreementId == s.AgreementId)
        .Select(v => new AgreementLineVersion { Id = v.Id, AccountId = v.AccountId, AgreementId = v.AgreementId, LineId = v.LineId,
            Sequence = v.Sequence, EffectiveFrom = v.EffectiveFrom, RecordedUtc = v.RecordedUtc, Name = v.Name, Description = v.Description,
            StartDate = v.StartDate, EndDate = v.EndDate, FirstPayableDate = v.FirstPayableDate,
            PayableSourceLineId = db.AgreementLines.Any(l => l.AccountId == s.ListScope.AccountId && l.AgreementId == s.AgreementId && l.Id == v.PayableSourceLineId) ? v.PayableSourceLineId : null,
            PayableSourceStartDate = v.PayableSourceStartDate, PayableOffsetMonths = v.PayableOffsetMonths,
            Frequency = v.Frequency, Anchor = v.Anchor, AnchorDate = v.AnchorDate, BillingTiming = v.BillingTiming, Status = v.Status, IndexRegulated = v.IndexRegulated });
    private static IQueryable<AgreementPriceVersion> Prices(TenantPlatformDbContext db, AgreementReadScope s) =>
        db.AgreementPriceVersions.AsNoTracking().Where(p => p.AccountId == s.ListScope.AccountId && p.AgreementId == s.AgreementId)
        .Select(p => new AgreementPriceVersion { Id = p.Id, AccountId = p.AccountId, AgreementId = p.AgreementId, LineId = p.LineId,
            Sequence = p.Sequence, Independent = p.Independent, EffectiveFrom = p.EffectiveFrom, IndexBaseDate = p.IndexBaseDate,
            Quantity = p.Quantity, UnitPrice = p.UnitPrice, RecordedUtc = p.RecordedUtc });
    private static int Page(int requested, int total) => Math.Min(requested, Math.Max(1, (int)(((long)total + 24) / 25)));
    public async Task<FinanceLines> LinesAsync(AgreementReadScope scope, Guid? line, int versionsPage, int pricesPage, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var a = await AgreementReadQueries.Visible(db, scope.ListScope).Where(a => a.Id == scope.AgreementId)
            .Select(a => new { a.Direction, a.Currency }).SingleOrDefaultAsync(ct) ?? throw new UnauthorizedAccessException();
        var lines = db.AgreementLines.AsNoTracking().Where(l => l.AccountId == scope.ListScope.AccountId && l.AgreementId == scope.AgreementId);
        if (line.HasValue && !await lines.AnyAsync(l => l.Id == line, ct)) throw new UnauthorizedAccessException();
        var versions = Versions(db, scope).Where(v => !line.HasValue || v.LineId == line);
        var prices = Prices(db, scope).Where(p => !line.HasValue || p.LineId == line);
        var nv = await versions.CountAsync(ct); var np = await prices.CountAsync(ct); var vp = Page(versionsPage, nv); var pp = Page(pricesPage, np);
        var vr = await versions.OrderBy(v => v.LineId).ThenBy(v => v.Sequence).ThenBy(v => v.Id).Skip((vp - 1) * 25).Take(25)
            .Select(v => new FinanceLineVersion(v.Id, v.LineId, v.Sequence, v.EffectiveFrom, v.RecordedUtc, v.Name, v.Description,
                v.StartDate, v.EndDate, v.FirstPayableDate, v.PayableSourceLineId, v.PayableSourceStartDate, v.PayableOffsetMonths,
                v.Frequency, v.Anchor, v.AnchorDate, v.BillingTiming, v.Status, v.IndexRegulated)).ToArrayAsync(ct);
        var pr = await prices.OrderBy(p => p.LineId).ThenBy(p => p.Sequence).ThenBy(p => p.Id).Skip((pp - 1) * 25).Take(25)
            .Select(p => new FinancePrice(p.Id, p.LineId, p.Sequence, p.Independent, p.EffectiveFrom, p.IndexBaseDate, p.Quantity, p.UnitPrice, p.RecordedUtc)).ToArrayAsync(ct);
        return new(a.Direction, a.Currency, await lines.CountAsync(ct), new(versionsPage, vp, nv, vr), new(pricesPage, pp, np, pr));
    }
    private static List<T> Bound<T>(List<T> values, int max) => values.Count > max ? throw new AgreementFinanceLimitException() : values;
    public async Task<AgreementFinancialState> ForecastStateAsync(AgreementReadScope scope, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var account = scope.ListScope.AccountId;
        var agreement = await AgreementReadQueries.Visible(db, scope.ListScope).Where(a => a.Id == scope.AgreementId)
            .Select(a => new Agreement { Id = a.Id, Direction = a.Direction, Currency = a.Currency, IndexSetupNeedsReview = a.IndexSetupNeedsReview }).SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedAccessException();
        var versions = Bound(await Versions(db, scope).OrderBy(v => v.Id).Take(AgreementFinanceReader.MaxVersions + 1).ToListAsync(ct), AgreementFinanceReader.MaxVersions);
        var prices = Bound(await Prices(db, scope).OrderBy(p => p.Id).Take(AgreementFinanceReader.MaxPrices + 1).ToListAsync(ct), AgreementFinanceReader.MaxPrices);
        var selections = Bound(await db.AgreementIndexSelections.AsNoTracking().Where(x => x.AccountId == account && x.AgreementId == scope.AgreementId)
            .OrderBy(x => x.Id).Select(x => new AgreementIndexSelection { Id = x.Id, IndexId = x.IndexId, Sequence = x.Sequence, EffectiveFrom = x.EffectiveFrom })
            .Take(AgreementFinanceReader.MaxSelections + 1).ToListAsync(ct), AgreementFinanceReader.MaxSelections);
        var ids = selections.Where(x => x.IndexId.HasValue).Select(x => x.IndexId!.Value).Distinct().ToArray();
        var indices = Bound(await db.AgreementIndexRecords.AsNoTracking().Where(x => x.AccountId == account && ids.Contains(x.Id))
            .OrderBy(x => x.Id).Select(x => new AgreementIndex { Id = x.Id, Name = x.Name }).Take(AgreementFinanceReader.MaxIndices + 1).ToListAsync(ct), AgreementFinanceReader.MaxIndices);
        var values = Bound(await db.AgreementIndexValueRecords.AsNoTracking().Where(x => x.AccountId == account && ids.Contains(x.IndexId))
            .OrderBy(x => x.Id).Select(x => new AgreementIndexValue { Id = x.Id, IndexId = x.IndexId, Period = x.Period, Revision = x.Revision,
                Superseded = x.Superseded, PeriodKey = x.PeriodKey, Value = x.Value, PublishedDate = x.PublishedDate })
            .Take(AgreementFinanceReader.MaxValues + 1).ToListAsync(ct), AgreementFinanceReader.MaxValues);
        return new() { Agreement = agreement, Versions = versions, Prices = prices, Selections = selections, Indices = indices, IndexValues = values };
    }
}

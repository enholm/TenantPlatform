using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementIndexReadData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementIndexReadData
{
    private static int Page(int requested, int total) => Math.Min(requested, Math.Max(1, (int)(((long)total + 24) / 25)));
    public async Task<FinancePage<IndexEntry>> RegisterAsync(AgreementListScope scope, string? search, AgreementIndexResolution? resolution, int page, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var query = db.AgreementIndexRecords.AsNoTracking().Where(i => i.AccountId == scope.AccountId);
        if (!string.IsNullOrEmpty(search)) query = query.Where(i => i.Code.Contains(search) || i.Name.Contains(search));
        if (resolution.HasValue) query = query.Where(i => i.Resolution == resolution);
        var total = await query.CountAsync(ct); var actual = Page(page, total);
        var items = await query.OrderBy(i => i.Code).ThenBy(i => i.Id).Skip((actual - 1) * 25).Take(25)
            .Select(i => new IndexEntry(i.Id, i.Code, i.Name, i.Description, i.Source, i.Resolution)).ToArrayAsync(ct);
        return new(page, actual, total, items);
    }
    public async Task<FinancePage<IndexValueEntry>> ValuesAsync(AgreementListScope scope, Guid index, IndexValueView view, DateOnly? from, DateOnly? to, int page, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        if (!await db.AgreementIndexRecords.AsNoTracking().AnyAsync(i => i.AccountId == scope.AccountId && i.Id == index, ct)) throw new UnauthorizedAccessException();
        var all = db.AgreementIndexValueRecords.AsNoTracking().Where(v => v.AccountId == scope.AccountId && v.IndexId == index);
        var query = all;
        if (view == IndexValueView.Current) query = query.Where(v => !v.Superseded && !all.Any(other => !other.Superseded && other.PeriodKey == v.PeriodKey && other.Revision > v.Revision));
        // Choose current revision BEFORE the period filter: a correction can move a period.
        if (from.HasValue) query = query.Where(v => v.Period >= from);
        if (to.HasValue) query = query.Where(v => v.Period <= to);
        var total = await query.CountAsync(ct); var actual = Page(page, total);
        var items = await query.OrderByDescending(v => v.Period).ThenByDescending(v => v.Revision).ThenBy(v => v.Id)
            .Skip((actual - 1) * 25).Take(25).Select(v => new IndexValueEntry(v.Id, v.Period, v.PeriodKey, v.Revision, v.Superseded, v.Value, v.PublishedDate)).ToArrayAsync(ct);
        return new(page, actual, total, items);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Dimensions;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingClassificationData(IDbContextFactory<TenantPlatformDbContext> factory) : ILeasingClassificationData
{
    public async Task<bool> HasOwnedRootAsync(Guid account, Guid user, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LeasingAcquisitions.AnyAsync(x => x.AccountId == account && (x.OwnerUserId == user || db.LeasingFrameworks.Any(f => f.AccountId == account && f.Id == x.FrameworkId && f.OwnerUserId == user)), ct)
            || await db.LeasingFrameworks.AnyAsync(x => x.AccountId == account && x.OwnerUserId == user, ct)
            || await db.LeasingOrders.AnyAsync(x => x.AccountId == account && (x.OwnerUserId == user || db.LeasingFrameworks.Any(f => f.AccountId == account && f.Id == x.FrameworkId && f.OwnerUserId == user)), ct);
    }
    public async Task<LeasingClassificationCatalog> ReadCatalogAsync(Guid account, bool canManage, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await CatalogAsync(db, account, canManage, ct);
    }
    public static async Task<LeasingClassificationCatalog> CatalogAsync(TenantPlatformDbContext db, Guid account, bool canManage, CancellationToken ct)
    {
        var dimensions = await db.Dimensions.AsNoTracking().Where(x => x.AccountId == account).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct);
        var values = await db.DimensionValues.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct);
        var rules = await db.LeasingDimensionRules.AsNoTracking().Where(x => x.AccountId == account).ToDictionaryAsync(x => x.DimensionId, ct);
        return new(dimensions.Select(d => new LeasingDimensionOption(d, rules.GetValueOrDefault(d.Id) ?? new() { AccountId = account, DimensionId = d.Id }, DimensionHierarchy.Build(d, values))).ToList(), canManage);
    }
    public async Task<ILeasingDimensionRuleTransaction> BeginRuleAsync(Guid account, Guid dimension, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            var dim = await db.Dimensions.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == dimension, ct);
            var rule = await db.LeasingDimensionRules.SingleOrDefaultAsync(x => x.AccountId == account && x.DimensionId == dimension, ct);
            return new Transaction(db, tx, dim, rule, account);
        }
        catch { await db.DisposeAsync(); throw; }
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, Dimension? dimension, LeasingDimensionRule? rule, Guid account) : ILeasingDimensionRuleTransaction
    {
        public Dimension? Dimension => dimension;
        public LeasingDimensionRule? Rule => rule;
        public async Task CommitAsync(LeasingDimensionRule value, DimensionHistory history, CancellationToken ct)
        {
            if (dimension is null || value.AccountId != account || value.DimensionId != dimension.Id || history.AccountId != account || history.DimensionId != dimension.Id) throw new UnauthorizedAccessException();
            if (rule is null) db.LeasingDimensionRules.Add(value);
            db.DimensionHistory.Add(history); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { await tx.DisposeAsync(); await db.DisposeAsync(); }
    }
}

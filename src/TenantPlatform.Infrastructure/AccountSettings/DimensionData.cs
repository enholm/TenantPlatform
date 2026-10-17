using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Dimensions;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.AccountSettings;

public sealed class DimensionData(IDbContextFactory<TenantPlatformDbContext> factory) : IDimensionData
{
    public async Task<DimensionRegister> ReadAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return new(await db.Dimensions.AsNoTracking().Where(x => x.AccountId == accountId).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct),
            await db.DimensionValues.AsNoTracking().Where(x => x.AccountId == accountId).ToListAsync(ct),
            await db.DimensionHistory.AsNoTracking().Where(x => x.AccountId == accountId).OrderByDescending(x => x.RecordedUtc).ToListAsync(ct),
            await db.UserAccounts.AsNoTracking().Where(x => x.AccountId == accountId)
                .Select(x => new { x.UserId, Name = x.User.FirstName + " " + x.User.LastName }).ToDictionaryAsync(x => x.UserId, x => x.Name, ct));
    }

    public async Task<IDimensionTransaction> BeginAsync(Guid accountId, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {accountId} FOR UPDATE", ct);
            var dimensions = await db.Dimensions.Where(x => x.AccountId == accountId).ToListAsync(ct);
            var values = await db.DimensionValues.Where(x => x.AccountId == accountId).ToListAsync(ct);
            return new DimensionTransaction(db, transaction, accountId, dimensions, values);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await db.DisposeAsync();
            throw;
        }
    }

    private sealed class DimensionTransaction(TenantPlatformDbContext db, IDbContextTransaction transaction, Guid account,
        List<Dimension> dimensions, List<DimensionValue> values) : IDimensionTransaction
    {
        public List<Dimension> Dimensions => dimensions;
        public List<DimensionValue> Values => values;
        public void Add(Dimension dimension)
        {
            if (dimension.AccountId != account) throw new UnauthorizedAccessException();
            db.Dimensions.Add(dimension);
        }
        public void Add(DimensionValue value)
        {
            if (value.AccountId != account) throw new UnauthorizedAccessException();
            db.DimensionValues.Add(value);
        }
        public void Add(DimensionHistory history)
        {
            if (history.AccountId != account) throw new UnauthorizedAccessException();
            db.DimensionHistory.Add(history);
        }
        public async Task CommitAsync(CancellationToken ct)
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally { await db.DisposeAsync(); }
        }
    }
}

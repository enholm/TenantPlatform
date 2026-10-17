using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Units;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Units;

public sealed class UnitData(IDbContextFactory<TenantPlatformDbContext> factory) : IUnitData
{
    public async Task<List<UnitListItemDto>> ReadListAsync(
        Guid accountId,
        Guid? buildingId = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await factory.CreateDbContextAsync(
                cancellationToken);

        var query = dbContext.Units
            .AsNoTracking()
            .Where(x => x.AccountId == accountId);

        if (buildingId.HasValue)
        {
            query = query.Where(
                x => x.BuildingId == buildingId.Value);
        }

        return await (
            from unit in query
            join building in dbContext.Buildings.Where(x => x.AccountId == accountId)
                on unit.BuildingId equals building.Id
            join parent in dbContext.Units.Where(x => x.AccountId == accountId)
                on unit.ParentUnitId equals parent.Id
                into parents
            from parent in parents.DefaultIfEmpty()
            orderby building.Name, unit.Name
            select new UnitListItemDto
            {
                Id = unit.Id,
                BuildingId = unit.BuildingId,
                BuildingName = building.Name,
                ParentUnitId = unit.ParentUnitId,
                ParentUnitName =
                    parent == null ? null : parent.Name,
                Name = unit.Name,
                Type = unit.Type,
                IsActive = unit.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<UnitDetailsDto?> ReadDetailsAsync(
        Guid accountId,
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await factory.CreateDbContextAsync(
                cancellationToken);

        return await (
            from unit in dbContext.Units.AsNoTracking()
            join building in dbContext.Buildings.Where(x => x.AccountId == accountId)
                on unit.BuildingId equals building.Id
            join parent in dbContext.Units.Where(x => x.AccountId == accountId)
                on unit.ParentUnitId equals parent.Id
                into parents
            from parent in parents.DefaultIfEmpty()
            where unit.Id == unitId &&
                  unit.AccountId == accountId
            select new UnitDetailsDto
            {
                Id = unit.Id,
                BuildingId = unit.BuildingId,
                BuildingName = building.Name,
                ParentUnitId = unit.ParentUnitId,
                ParentUnitName =
                    parent == null ? null : parent.Name,
                Name = unit.Name,
                Type = unit.Type,
                IsActive = unit.IsActive,

                ChildUnitCount = dbContext.Units
                    .Count(x =>
                        x.AccountId == accountId && x.ParentUnitId == unit.Id),

                OccupancyCount = dbContext.Occupancies
                    .Count(x =>
                        x.AccountId == accountId && x.UnitId == unit.Id)
            })
            .SingleOrDefaultAsync(cancellationToken);
    }


    public async Task<IUnitTransaction> BeginAsync(Guid account, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? transaction = null;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            var units = await db.Units.Where(x => x.AccountId == account).ToListAsync(ct);
            var buildings = await db.Buildings.AsNoTracking().Where(x => x.AccountId == account).Select(x => x.Id).ToListAsync(ct);
            return new UnitTransaction(db, transaction, account, units, buildings);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await db.DisposeAsync(); throw;
        }
    }
    private sealed class UnitTransaction(TenantPlatformDbContext db, IDbContextTransaction transaction, Guid account,
        List<Unit> units, List<Guid> buildings) : IUnitTransaction
    {
        public IReadOnlyList<Unit> Units => units;
        public IReadOnlyList<Guid> BuildingIds => buildings;
        public async Task<UnitReferences> ReadReferencesAsync(Guid id, CancellationToken ct) => new(
            units.Any(x => x.ParentUnitId == id),
            await db.Occupancies.AnyAsync(x => x.AccountId == account && x.UnitId == id, ct),
            await db.Agreements.AnyAsync(x => x.AccountId == account && x.UnitId == id, ct));
        public async Task CommitAsync(Unit unit, bool create, bool delete, CancellationToken ct)
        {
            if (unit.AccountId != account) throw new UnauthorizedAccessException();
            if (create) db.Units.Add(unit);
            if (delete) db.Units.Remove(unit);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally { await db.DisposeAsync(); }
        }
    }
}

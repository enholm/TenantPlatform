using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Occupancies;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Occupancies;

public sealed class OccupancyData(IDbContextFactory<TenantPlatformDbContext> factory) : IOccupancyData
{
    public async Task<List<OccupancyListItemDto>> ReadListAsync(
        Guid accountId,
        DateOnly today,
        Guid? buildingId = null,
        Guid? tenantOrganizationId = null,
        Guid? unitId = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await factory.CreateDbContextAsync(
                cancellationToken);

        var query =
            from occupancy in dbContext.Occupancies.AsNoTracking()
            join organization in dbContext.Organizations.Where(x => x.AccountId == accountId)
                on occupancy.TenantOrganizationId equals organization.Id
            join unit in dbContext.Units.Where(x => x.AccountId == accountId)
                on occupancy.UnitId equals unit.Id
            join building in dbContext.Buildings.Where(x => x.AccountId == accountId)
                on unit.BuildingId equals building.Id
            where occupancy.AccountId == accountId
            select new
            {
                Occupancy = occupancy,
                Organization = organization,
                Unit = unit,
                Building = building
            };

        if (buildingId.HasValue)
        {
            query = query.Where(
                x => x.Unit.BuildingId == buildingId.Value);
        }

        if (tenantOrganizationId.HasValue)
        {
            query = query.Where(
                x => x.Occupancy.TenantOrganizationId
                     == tenantOrganizationId.Value);
        }

        if (unitId.HasValue)
        {
            query = query.Where(
                x => x.Occupancy.UnitId == unitId.Value);
        }


        return await query
            .OrderBy(x => x.Building.Name)
            .ThenBy(x => x.Unit.Name)
            .ThenByDescending(x => x.Occupancy.ValidFrom)
            .Select(x => new OccupancyListItemDto
            {
                Id = x.Occupancy.Id,

                TenantOrganizationId =
                    x.Occupancy.TenantOrganizationId,

                TenantOrganizationName =
                    x.Organization.Name,

                UnitId = x.Occupancy.UnitId,
                UnitName = x.Unit.Name,

                BuildingId = x.Unit.BuildingId,
                BuildingName = x.Building.Name,

                ValidFrom = x.Occupancy.ValidFrom,
                ValidTo = x.Occupancy.ValidTo,

                IsActive =
                    x.Occupancy.ValidFrom <= today &&
                    (
                        x.Occupancy.ValidTo == null ||
                        x.Occupancy.ValidTo >= today
                    )
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<OccupancyDetailsDto?> ReadDetailsAsync(
        Guid accountId,
        Guid occupancyId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await factory.CreateDbContextAsync(
                cancellationToken);

        return await (
            from occupancy in dbContext.Occupancies.AsNoTracking()
            join organization in dbContext.Organizations.Where(x => x.AccountId == accountId)
                on occupancy.TenantOrganizationId equals organization.Id
            join unit in dbContext.Units.Where(x => x.AccountId == accountId)
                on occupancy.UnitId equals unit.Id
            join building in dbContext.Buildings.Where(x => x.AccountId == accountId)
                on unit.BuildingId equals building.Id
            where occupancy.Id == occupancyId
                  && occupancy.AccountId == accountId
            select new OccupancyDetailsDto
            {
                Id = occupancy.Id,

                TenantOrganizationId =
                    occupancy.TenantOrganizationId,

                TenantOrganizationName =
                    organization.Name,

                UnitId = occupancy.UnitId,
                UnitName = unit.Name,

                BuildingId = unit.BuildingId,
                BuildingName = building.Name,

                ValidFrom = occupancy.ValidFrom,
                ValidTo = occupancy.ValidTo
            })
            .SingleOrDefaultAsync(cancellationToken);
    }


    public async Task<Occupancy?> ReadEntityAsync(Guid account, Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Occupancies.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == id, ct);
    }
    public async Task<OccupancyState> ReadHierarchyAsync(Guid account, Guid unitId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var building = await db.Units.AsNoTracking().Where(x => x.AccountId == account && x.Id == unitId).Select(x => (Guid?)x.BuildingId).SingleOrDefaultAsync(ct)
            ?? throw new UnauthorizedAccessException();
        var units = await db.Units.AsNoTracking().Where(x => x.AccountId == account && x.BuildingId == building).ToListAsync(ct);
        var ids = units.Select(x => x.Id).ToArray();
        var occurrences = await db.Occupancies.AsNoTracking().Where(x => x.AccountId == account && ids.Contains(x.UnitId)).ToListAsync(ct);
        var organizationIds = occurrences.Select(x => x.TenantOrganizationId).Distinct().ToArray();
        var organizations = await db.Organizations.AsNoTracking().Where(x => x.AccountId == account && organizationIds.Contains(x.Id)).ToListAsync(ct);
        return new(units, organizations, occurrences);
    }
    public async Task<IOccupancyTransaction> BeginAsync(Guid account, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            var units = await db.Units.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct);
            var organizations = await db.Organizations.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct);
            var occurrences = await db.Occupancies.Where(x => x.AccountId == account).ToListAsync(ct);
            return new OccupancyTransaction(db, tx, account, new(units, organizations, occurrences));
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed class OccupancyTransaction(TenantPlatformDbContext db, IDbContextTransaction tx, Guid account, OccupancyState state) : IOccupancyTransaction
    {
        public OccupancyState State => state;
        public async Task CommitAsync(Occupancy occurrence, bool create, bool delete, CancellationToken ct)
        {
            if (occurrence.AccountId != account) throw new UnauthorizedAccessException();
            if (create) db.Occupancies.Add(occurrence);
            if (delete) db.Occupancies.Remove(occurrence);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}

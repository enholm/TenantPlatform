using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Buildings;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Buildings;

public sealed class BuildingData(IDbContextFactory<TenantPlatformDbContext> factory) : IBuildingData
{
    public async Task<List<BuildingListItemDto>> ReadListAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await factory.CreateDbContextAsync(cancellationToken);

        return await dbContext.Buildings
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderBy(x => x.Name)
            .Select(x => new BuildingListItemDto
            {
                Id = x.Id,
                Name = x.Name,
                AddressLine1 = x.AddressLine1,
                PostalCode = x.PostalCode,
                City = x.City
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<BuildingDetailsDto?> ReadDetailsAsync(
        Guid accountId,
        Guid buildingId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await factory.CreateDbContextAsync(cancellationToken);

        return await dbContext.Buildings
            .AsNoTracking()
            .Where(x =>
                x.Id == buildingId &&
                x.AccountId == accountId)
            .Select(x => new BuildingDetailsDto
            {
                Id = x.Id,
                Name = x.Name,
                AddressLine1 = x.AddressLine1,
                PostalCode = x.PostalCode,
                City = x.City,
                CountryCode = x.CountryCode,

                UnitCount = dbContext.Units
                    .Count(u => u.AccountId == accountId && u.BuildingId == x.Id),

                TenantCount =
                    (from occupancy in dbContext.Occupancies
                     join unit in dbContext.Units
                         on occupancy.UnitId equals unit.Id
                     where occupancy.AccountId == accountId && unit.AccountId == accountId && unit.BuildingId == x.Id
                     select occupancy.TenantOrganizationId)
                    .Distinct()
                    .Count(),

                OpenRequestCount = dbContext.ServiceRequests
                    .Count(r =>
                        r.AccountId == accountId && r.BuildingId == x.Id &&
                        r.Status != Core.Services.ServiceRequestStatus.Completed &&
                        r.Status != Core.Services.ServiceRequestStatus.Cancelled &&
                        r.Status != Core.Services.ServiceRequestStatus.Rejected &&
                        r.Status != Core.Services.ServiceRequestStatus.Failed),

                Units = dbContext.Units
                    .Where(u => u.AccountId == accountId && u.BuildingId == x.Id)
                    .OrderBy(u => u.Name)
                    .Select(u => new BuildingUnitDto
                    {
                        Id = u.Id,
                        Name = u.Name,
                        Type = u.Type.ToString()
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);
    }


    public async Task<IBuildingTransaction> BeginAsync(Guid accountId, Guid? id, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await db.Database.BeginTransactionAsync(ct);
            Building entity;
            if (id.HasValue)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM buildings WHERE \"Id\" = {id.Value} AND \"AccountId\" = {accountId} FOR UPDATE", ct);
                entity = await db.Buildings.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == id, ct) ?? throw new UnauthorizedAccessException();
            }
            else
            {
                entity = new Building { Id = Guid.NewGuid(), AccountId = accountId };
                db.Buildings.Add(entity);
            }
            return new BuildingTransaction(db, transaction, entity);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await db.DisposeAsync(); throw;
        }
    }
    private sealed class BuildingTransaction(TenantPlatformDbContext db, IDbContextTransaction transaction, Building entity) : IBuildingTransaction
    {
        public Building Building => entity;
        public async Task<BuildingReferences> ReadReferencesAsync(CancellationToken ct) => new(
            await db.Units.AnyAsync(x => x.AccountId == entity.AccountId && x.BuildingId == entity.Id, ct),
            await db.ServiceRequests.AnyAsync(x => x.AccountId == entity.AccountId && x.BuildingId == entity.Id, ct),
            await db.NetworkEnvironments.AnyAsync(x => x.AccountId == entity.AccountId && x.BuildingId == entity.Id, ct),
            await db.MeetingRooms.AnyAsync(x => x.AccountId == entity.AccountId && x.BuildingId == entity.Id, ct),
            await db.Agreements.AnyAsync(x => x.AccountId == entity.AccountId && x.BuildingId == entity.Id, ct));
        public async Task CommitAsync(bool delete, CancellationToken ct)
        {
            if (delete) db.Buildings.Remove(entity);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync()
        {
            try { await transaction.DisposeAsync(); }
            finally { await db.DisposeAsync(); }
        }
    }
}

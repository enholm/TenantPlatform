using Microsoft.EntityFrameworkCore;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Buildings;

public class BuildingService : IBuildingService
{
    private readonly IDbContextFactory<TenantPlatformDbContext> _dbContextFactory;

    public BuildingService(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<BuildingListItemDto>> GetBuildingsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);

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

    public async Task<BuildingDetailsDto?> GetBuildingAsync(
        Guid accountId,
        Guid buildingId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);

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
                    .Count(u => u.BuildingId == x.Id),

                TenantCount =
                    (from occupancy in dbContext.Occupancies
                     join unit in dbContext.Units
                         on occupancy.UnitId equals unit.Id
                     where unit.BuildingId == x.Id
                     select occupancy.TenantOrganizationId)
                    .Distinct()
                    .Count(),

                OpenRequestCount = dbContext.ServiceRequests
                    .Count(r =>
                        r.BuildingId == x.Id &&
                        r.Status != Core.Services.ServiceRequestStatus.Completed &&
                        r.Status != Core.Services.ServiceRequestStatus.Cancelled &&
                        r.Status != Core.Services.ServiceRequestStatus.Rejected &&
                        r.Status != Core.Services.ServiceRequestStatus.Failed),

                Units = dbContext.Units
                    .Where(u => u.BuildingId == x.Id)
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
}
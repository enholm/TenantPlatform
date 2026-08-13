using Microsoft.EntityFrameworkCore;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Core.Properties;
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

    public async Task<Guid> CreateBuildingAsync(
        Guid accountId,
        CreateBuildingRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var building = new Building
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Name = request.Name.Trim(),
            AddressLine1 = request.AddressLine1?.Trim(),
            PostalCode = request.PostalCode?.Trim(),
            City = request.City?.Trim(),
            CountryCode = request.CountryCode.Trim().ToUpperInvariant(),
        };

        dbContext.Buildings.Add(building);

        await dbContext.SaveChangesAsync(cancellationToken);

        return building.Id;
    }

    public async Task UpdateBuildingAsync(
        Guid accountId,
        Guid buildingId,
        UpdateBuildingRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var building = await dbContext.Buildings
            .SingleOrDefaultAsync(
                x => x.Id == buildingId &&
                    x.AccountId == accountId,
                cancellationToken);

        if (building is null)
        {
            throw new InvalidOperationException(
                "Building was not found in the current account.");
        }

        building.Name = request.Name.Trim();
        building.AddressLine1 = request.AddressLine1?.Trim();
        building.PostalCode = request.PostalCode?.Trim();
        building.City = request.City?.Trim();
        building.CountryCode =
            request.CountryCode.Trim().ToUpperInvariant();

        await dbContext.SaveChangesAsync(cancellationToken);
    }    

    public async Task DeleteBuildingAsync(
        Guid accountId,
        Guid buildingId,
        CancellationToken cancellationToken = default)
    {
        var deleteCheck = await CanDeleteBuildingAsync(
            accountId,
            buildingId,
            cancellationToken);

        if (!deleteCheck.CanDelete)
        {
            throw new BuildingDeleteNotAllowedException(
                deleteCheck.Reason ?? "BuildingCannotBeDeleted");
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var building = await dbContext.Buildings
            .SingleOrDefaultAsync(
                x => x.Id == buildingId &&
                    x.AccountId == accountId,
                cancellationToken);

        if (building is null)
        {
            throw new InvalidOperationException(
                "Building was not found in the current account.");
        }

        dbContext.Buildings.Remove(building);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<BuildingDeleteCheckResult> CanDeleteBuildingAsync(
        Guid accountId,
        Guid buildingId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var buildingExists = await dbContext.Buildings
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == buildingId &&
                    x.AccountId == accountId,
                cancellationToken);

        if (!buildingExists)
        {
            return BuildingDeleteCheckResult.NotAllowed(
                "BuildingNotFound");
        }

        var hasUnits = await dbContext.Units
            .AsNoTracking()
            .AnyAsync(
                x => x.BuildingId == buildingId,
                cancellationToken);

        if (hasUnits)
        {
            return BuildingDeleteCheckResult.NotAllowed(
                "BuildingContainsUnits");
        }

        var hasServiceRequests = await dbContext.ServiceRequests
            .AsNoTracking()
            .AnyAsync(
                x => x.BuildingId == buildingId,
                cancellationToken);

        if (hasServiceRequests)
        {
            return BuildingDeleteCheckResult.NotAllowed(
                "BuildingContainsServiceRequests");
        }

        var hasNetworkEnvironments = await dbContext.NetworkEnvironments
            .AsNoTracking()
            .AnyAsync(
                x => x.BuildingId == buildingId,
                cancellationToken);

        if (hasNetworkEnvironments)
        {
            return BuildingDeleteCheckResult.NotAllowed(
                "BuildingContainsNetworkEnvironments");
        }

        return BuildingDeleteCheckResult.Allowed();
    }
}
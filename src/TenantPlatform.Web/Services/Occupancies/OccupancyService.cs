using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Occupancies;

public class OccupancyService : IOccupancyService
{
    private readonly IDbContextFactory<TenantPlatformDbContext>
        _dbContextFactory;

    public OccupancyService(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<OccupancyListItemDto>> GetOccupanciesAsync(
        Guid accountId,
        Guid? buildingId = null,
        Guid? tenantOrganizationId = null,
        Guid? unitId = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var query =
            from occupancy in dbContext.Occupancies.AsNoTracking()
            join organization in dbContext.Organizations
                on occupancy.TenantOrganizationId equals organization.Id
            join unit in dbContext.Units
                on occupancy.UnitId equals unit.Id
            join building in dbContext.Buildings
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

        var today = DateOnly.FromDateTime(DateTime.Today);

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

    public async Task<OccupancyDetailsDto?> GetOccupancyAsync(
        Guid accountId,
        Guid occupancyId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        return await (
            from occupancy in dbContext.Occupancies.AsNoTracking()
            join organization in dbContext.Organizations
                on occupancy.TenantOrganizationId equals organization.Id
            join unit in dbContext.Units
                on occupancy.UnitId equals unit.Id
            join building in dbContext.Buildings
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

    public async Task<Guid> CreateOccupancyAsync(
        Guid accountId,
        CreateOccupancyRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        await ValidateOccupancyAsync(
            dbContext,
            accountId,
            request.TenantOrganizationId,
            request.UnitId,
            request.ValidFrom,
            request.ValidTo,
            null,
            cancellationToken);

        var occupancy = new Occupancy
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,

            TenantOrganizationId =
                request.TenantOrganizationId,

            UnitId = request.UnitId,

            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo
        };

        dbContext.Occupancies.Add(occupancy);

        await dbContext.SaveChangesAsync(cancellationToken);

        return occupancy.Id;
    }

    public async Task UpdateOccupancyAsync(
        Guid accountId,
        Guid occupancyId,
        UpdateOccupancyRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var occupancy =
            await dbContext.Occupancies
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == occupancyId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (occupancy is null)
        {
            throw new InvalidOperationException(
                "Occupancy was not found in the current account.");
        }

        await ValidateOccupancyAsync(
            dbContext,
            accountId,
            request.TenantOrganizationId,
            request.UnitId,
            request.ValidFrom,
            request.ValidTo,
            occupancyId,
            cancellationToken);

        occupancy.TenantOrganizationId =
            request.TenantOrganizationId;

        occupancy.UnitId = request.UnitId;

        occupancy.ValidFrom = request.ValidFrom;
        occupancy.ValidTo = request.ValidTo;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task ValidateOccupancyAsync(
        TenantPlatformDbContext dbContext,
        Guid accountId,
        Guid tenantOrganizationId,
        Guid unitId,
        DateOnly validFrom,
        DateOnly? validTo,
        Guid? currentOccupancyId,
        CancellationToken cancellationToken)
    {
        if (validTo.HasValue &&
            validTo.Value < validFrom)
        {
            throw new OccupancyValidationException(
                "OccupancyValidToBeforeValidFrom");
        }

        var organization =
            await dbContext.Organizations
                .AsNoTracking()
                .Where(x =>
                    x.Id == tenantOrganizationId &&
                    x.AccountId == accountId)
                .Select(x => new
                {
                    x.Id,
                    x.Type,
                    x.IsActive
                })
                .SingleOrDefaultAsync(cancellationToken);

        if (organization is null)
        {
            throw new OccupancyValidationException(
                "OccupancyTenantOrganizationNotFound");
        }

        if (organization.Type != OrganizationType.Tenant)
        {
            throw new OccupancyValidationException(
                "OccupancyOrganizationMustBeTenant");
        }

        if (!organization.IsActive)
        {
            throw new OccupancyValidationException(
                "OccupancyTenantOrganizationInactive");
        }

        var unit =
            await dbContext.Units
                .AsNoTracking()
                .Where(x =>
                    x.Id == unitId &&
                    x.AccountId == accountId)
                .Select(x => new
                {
                    x.Id,
                    x.IsActive
                })
                .SingleOrDefaultAsync(cancellationToken);

        if (unit is null)
        {
            throw new OccupancyValidationException(
                "OccupancyUnitNotFound");
        }

        if (!unit.IsActive)
        {
            throw new OccupancyValidationException(
                "OccupancyUnitInactive");
        }

        var overlapQuery =
            dbContext.Occupancies
                .AsNoTracking()
                .Where(x =>
                    x.AccountId == accountId &&
                    x.UnitId == unitId);

        if (currentOccupancyId.HasValue)
        {
            overlapQuery =
                overlapQuery.Where(
                    x => x.Id != currentOccupancyId.Value);
        }

        var hasOverlap = await overlapQuery.AnyAsync(
            x =>
                x.ValidFrom <= (validTo ?? DateOnly.MaxValue)
                &&
                (x.ValidTo ?? DateOnly.MaxValue) >= validFrom,
            cancellationToken);

        if (hasOverlap)
        {
            throw new OccupancyValidationException(
                "OccupancyOverlapsExisting");
        }
    }

    public async Task EndOccupancyAsync(
        Guid accountId,
        Guid occupancyId,
        DateOnly validTo,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var occupancy = await dbContext.Occupancies
            .SingleOrDefaultAsync(
                x =>
                    x.Id == occupancyId &&
                    x.AccountId == accountId,
                cancellationToken);

        if (occupancy is null)
        {
            throw new InvalidOperationException(
                "Occupancy was not found in the current account.");
        }

        await ValidateOccupancyAsync(
            dbContext,
            accountId,
            occupancy.TenantOrganizationId,
            occupancy.UnitId,
            occupancy.ValidFrom,
            validTo,
            occupancy.Id,
            cancellationToken);

        occupancy.ValidTo = validTo;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}


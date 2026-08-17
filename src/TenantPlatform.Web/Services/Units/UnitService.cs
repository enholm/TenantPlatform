using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Units;

public class UnitService : IUnitService
{
    private readonly IDbContextFactory<TenantPlatformDbContext>
        _dbContextFactory;

    public UnitService(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<UnitListItemDto>> GetUnitsAsync(
        Guid accountId,
        Guid? buildingId = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
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
            join building in dbContext.Buildings
                on unit.BuildingId equals building.Id
            join parent in dbContext.Units
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

    public async Task<UnitDetailsDto?> GetUnitAsync(
        Guid accountId,
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        return await (
            from unit in dbContext.Units.AsNoTracking()
            join building in dbContext.Buildings
                on unit.BuildingId equals building.Id
            join parent in dbContext.Units
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
                        x.ParentUnitId == unit.Id),

                OccupancyCount = dbContext.Occupancies
                    .Count(x =>
                        x.UnitId == unit.Id)
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid> CreateUnitAsync(
        Guid accountId,
        CreateUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        await ValidateBuildingAndParentAsync(
            dbContext,
            accountId,
            request.BuildingId,
            request.ParentUnitId,
            null,
            cancellationToken);

        var unit = new Unit
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            BuildingId = request.BuildingId,
            ParentUnitId = request.ParentUnitId,
            Name = request.Name.Trim(),
            Type = request.Type,
            IsActive = request.IsActive
        };

        dbContext.Units.Add(unit);

        await dbContext.SaveChangesAsync(cancellationToken);

        return unit.Id;
    }

    public async Task UpdateUnitAsync(
        Guid accountId,
        Guid unitId,
        UpdateUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var unit = await dbContext.Units
            .SingleOrDefaultAsync(
                x =>
                    x.Id == unitId &&
                    x.AccountId == accountId,
                cancellationToken);

        if (unit is null)
        {
            throw new InvalidOperationException(
                "Unit was not found in the current account.");
        }

        await ValidateBuildingAndParentAsync(
            dbContext,
            accountId,
            request.BuildingId,
            request.ParentUnitId,
            unitId,
            cancellationToken);

        unit.BuildingId = request.BuildingId;
        unit.ParentUnitId = request.ParentUnitId;
        unit.Name = request.Name.Trim();
        unit.Type = request.Type;
        unit.IsActive = request.IsActive;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<UnitDeleteCheckResult> CanDeleteUnitAsync(
        Guid accountId,
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var exists = await dbContext.Units
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.Id == unitId &&
                    x.AccountId == accountId,
                cancellationToken);

        if (!exists)
        {
            return UnitDeleteCheckResult.NotAllowed(
                "UnitNotFound");
        }

        var hasChildren = await dbContext.Units
            .AsNoTracking()
            .AnyAsync(
                x => x.ParentUnitId == unitId,
                cancellationToken);

        if (hasChildren)
        {
            return UnitDeleteCheckResult.NotAllowed(
                "UnitContainsChildUnits");
        }

        var hasOccupancies = await dbContext.Occupancies
            .AsNoTracking()
            .AnyAsync(
                x => x.UnitId == unitId,
                cancellationToken);

        if (hasOccupancies)
        {
            return UnitDeleteCheckResult.NotAllowed(
                "UnitContainsOccupancies");
        }

        return UnitDeleteCheckResult.Allowed();
    }

    public async Task DeleteUnitAsync(
        Guid accountId,
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        var deleteCheck =
            await CanDeleteUnitAsync(
                accountId,
                unitId,
                cancellationToken);

        if (!deleteCheck.CanDelete)
        {
            throw new UnitDeleteNotAllowedException(
                deleteCheck.Reason ??
                "UnitCannotBeDeleted");
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var unit = await dbContext.Units
            .SingleOrDefaultAsync(
                x =>
                    x.Id == unitId &&
                    x.AccountId == accountId,
                cancellationToken);

        if (unit is null)
        {
            throw new InvalidOperationException(
                "Unit was not found in the current account.");
        }

        dbContext.Units.Remove(unit);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task ValidateBuildingAndParentAsync(
        TenantPlatformDbContext dbContext,
        Guid accountId,
        Guid buildingId,
        Guid? parentUnitId,
        Guid? currentUnitId,
        CancellationToken cancellationToken)
    {
        var buildingExists =
            await dbContext.Buildings.AnyAsync(
                x =>
                    x.Id == buildingId &&
                    x.AccountId == accountId,
                cancellationToken);

        if (!buildingExists)
        {
            throw new UnitValidationException("UnitParentNotInSelectedAccount");
        }

        if (!parentUnitId.HasValue)
        {
            return;
        }

        if (currentUnitId.HasValue &&
            parentUnitId.Value == currentUnitId.Value)
        {
            throw new UnitValidationException("UnitCannotBeOwnParent");
        }

        var parent = await dbContext.Units
            .AsNoTracking()
            .Where(x =>
                x.Id == parentUnitId.Value &&
                x.AccountId == accountId &&
                x.BuildingId == buildingId)
            .Select(x => new
            {
                x.Id,
                x.ParentUnitId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (parent is null)
        {
            throw new UnitValidationException("UnitParentNotInSelectedBuilding");
        }

        // Ved create finnes currentUnitId ikke ennå, så det kan
        // naturligvis ikke oppstå en syklus tilbake til den nye enheten.
        if (!currentUnitId.HasValue)
        {
            return;
        }

        var visited = new HashSet<Guid>();

        Guid? candidateId = parent.Id;
        Guid? candidateParentId = parent.ParentUnitId;

        while (candidateId.HasValue)
        {
            if (!visited.Add(candidateId.Value))
            {
                throw new UnitValidationException("UnitHierarchyContainsCircularReference");
            }

            if (candidateId.Value == currentUnitId.Value)
            {
                throw new UnitValidationException("UnitParentCreatesCircularHierarchy");
            }

            if (!candidateParentId.HasValue)
            {
                break;
            }

            var next = await dbContext.Units
                .AsNoTracking()
                .Where(x =>
                    x.Id == candidateParentId.Value &&
                    x.AccountId == accountId &&
                    x.BuildingId == buildingId)
                .Select(x => new
                {
                    x.Id,
                    x.ParentUnitId
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (next is null)
            {
                throw new InvalidOperationException(
                    "The unit hierarchy contains an invalid parent reference.");
            }

            candidateId = next.Id;
            candidateParentId = next.ParentUnitId;
        }
    }

    public async Task<List<UnitListItemDto>> GetParentCandidatesAsync(
        Guid accountId,
        Guid buildingId,
        Guid? excludeUnitId = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var query =
            from unit in dbContext.Units.AsNoTracking()
            join building in dbContext.Buildings
                on unit.BuildingId equals building.Id
            join parent in dbContext.Units
                on unit.ParentUnitId equals parent.Id
                into parents
            from parent in parents.DefaultIfEmpty()
            where unit.AccountId == accountId
                && unit.BuildingId == buildingId
            select new UnitListItemDto
            {
                Id = unit.Id,
                BuildingId = unit.BuildingId,
                BuildingName = building.Name,
                ParentUnitId = unit.ParentUnitId,
                ParentUnitName = parent == null
                    ? null
                    : parent.Name,
                Name = unit.Name,
                Type = unit.Type,
                IsActive = unit.IsActive
            };

        if (excludeUnitId.HasValue)
        {
            query = query.Where(
                x => x.Id != excludeUnitId.Value);
        }

        return await query
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }    
}


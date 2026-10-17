using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.ServiceCatalog;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.ServiceDefinitions;

public sealed class ServiceCatalogLocationData(IDbContextFactory<TenantPlatformDbContext> factory) : IServiceCatalogLocationData
{
    public async Task<List<ServiceRequestLocationDto>> ReadAsync(ServiceLocationScope scope, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var source = from occupancy in db.Occupancies.AsNoTracking()
            join organization in db.Organizations.AsNoTracking() on occupancy.TenantOrganizationId equals organization.Id
            join unit in db.Units.AsNoTracking() on occupancy.UnitId equals unit.Id
            join building in db.Buildings.AsNoTracking() on unit.BuildingId equals building.Id
            where occupancy.AccountId == scope.AccountId && organization.AccountId == scope.AccountId && unit.AccountId == scope.AccountId && building.AccountId == scope.AccountId &&
                occupancy.ValidFrom <= scope.Today && (!occupancy.ValidTo.HasValue || occupancy.ValidTo >= scope.Today)
            select new ServiceRequestLocationDto { OccupancyId = occupancy.Id, OrganizationId = organization.Id, OrganizationName = organization.Name,
                BuildingId = building.Id, BuildingName = building.Name, UnitId = unit.Id, UnitName = unit.Name };
        var query = scope.AllLocations ? source : source.Where(x => false);
        if (!scope.AllLocations) foreach (var assignment in scope.TenantAssignments)
        {
            var organization = assignment.OrganizationId; var building = assignment.BuildingId;
            query = query.Union(source.Where(x => x.OrganizationId == organization && (!building.HasValue || x.BuildingId == building)));
        }
        return scope.AllLocations ? await query.OrderBy(x => x.OrganizationName).ThenBy(x => x.BuildingName).ThenBy(x => x.UnitName).ToListAsync(ct)
            : await query.OrderBy(x => x.BuildingName).ThenBy(x => x.UnitName).ToListAsync(ct);
    }
}

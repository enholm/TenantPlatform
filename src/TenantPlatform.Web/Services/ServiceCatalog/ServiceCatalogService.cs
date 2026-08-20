using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Localization;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.ServiceDefinitions;
using TenantPlatform.Core.Identity;
namespace TenantPlatform.Web.Services.ServiceCatalog;

public class ServiceCatalogService : IServiceCatalogService
{
    private readonly IDbContextFactory<TenantPlatformDbContext>
        _dbContextFactory;

    public ServiceCatalogService(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<ServiceCatalogItemDto>> GetAvailableServicesAsync(
        Guid accountId,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var defaultLanguage =
            await dbContext.Accounts
                .AsNoTracking()
                .Where(x => x.Id == accountId)
                .Select(x => x.DefaultLanguage)
                .SingleAsync(cancellationToken);

        var definitions =
            await dbContext.ServiceDefinitions
                .AsNoTracking()
                .Where(x =>
                    x.AccountId == accountId &&
                    x.IsActive &&
                    x.IsBookableByTenant)
                .OrderBy(x => x.Category)
                .ThenBy(x => x.Code)
                .ToListAsync(cancellationToken);

        var ids = definitions
            .Select(x => x.Id)
            .ToList();

        var translations =
            await dbContext.ServiceDefinitionTranslations
                .AsNoTracking()
                .Where(x =>
                    ids.Contains(x.ServiceDefinitionId))
                .ToListAsync(cancellationToken);

        return definitions
            .Select(definition =>
            {
                var translation =
                    TranslationHelper.Select(
                        translations.Where(x =>
                            x.ServiceDefinitionId == definition.Id),
                        x => x.LanguageCode,
                        languageCode,
                        defaultLanguage);

                return new ServiceCatalogItemDto
                {
                    Id = definition.Id,
                    Code = definition.Code,
                    Name = translation?.Name ?? definition.Code,
                    Description = translation?.Description,
                    Category = definition.Category,
                    RequiresApproval = definition.RequiresApproval,
                    RequiresOccupancy = definition.RequiresOccupancy
                };
            })
            .ToList();
    }

    public async Task<ServiceCatalogDetailsDto?> GetServiceAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var defaultLanguage =
            await dbContext.Accounts
                .AsNoTracking()
                .Where(x => x.Id == accountId)
                .Select(x => x.DefaultLanguage)
                .SingleAsync(cancellationToken);

        var definition =
            await dbContext.ServiceDefinitions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == serviceDefinitionId &&
                        x.AccountId == accountId &&
                        x.IsActive &&
                        x.IsBookableByTenant,
                    cancellationToken);

        if (definition is null)
        {
            return null;
        }

        var translations =
            await dbContext.ServiceDefinitionTranslations
                .AsNoTracking()
                .Where(x =>
                    x.ServiceDefinitionId == serviceDefinitionId)
                .ToListAsync(cancellationToken);

        var translation =
            TranslationHelper.Select(
                translations,
                x => x.LanguageCode,
                languageCode,
                defaultLanguage);

        var fields =
            await dbContext.ServiceDefinitionFields
                .AsNoTracking()
                .Where(x =>
                    x.ServiceDefinitionId == serviceDefinitionId)
                .OrderBy(x => x.SortOrder)
                .ToListAsync(cancellationToken);

        var fieldIds = fields
            .Select(x => x.Id)
            .ToList();

        var fieldTranslations =
            await dbContext.ServiceDefinitionFieldTranslations
                .AsNoTracking()
                .Where(x =>
                    fieldIds.Contains(
                        x.ServiceDefinitionFieldId))
                .ToListAsync(cancellationToken);

        var fieldDtos =
            fields.Select(field =>
            {
                var fieldTranslation =
                    TranslationHelper.Select(
                        fieldTranslations.Where(x =>
                            x.ServiceDefinitionFieldId == field.Id),
                        x => x.LanguageCode,
                        languageCode,
                        defaultLanguage);

                return new ServiceDefinitionFieldDto
                {
                    Id = field.Id,
                    Key = field.Key,
                    Label = fieldTranslation?.Label ?? field.Key,
                    Placeholder = fieldTranslation?.Placeholder,
                    HelpText = fieldTranslation?.HelpText,
                    FieldType = field.FieldType,
                    IsRequired = field.IsRequired,
                    SortOrder = field.SortOrder,
                    Options = field.Options
                };
            })
            .ToList();

        return new ServiceCatalogDetailsDto
        {
            Id = definition.Id,
            Code = definition.Code,
            Name = translation?.Name ?? definition.Code,
            Description = translation?.Description,
            Category = definition.Category,
            RequiresApproval = definition.RequiresApproval,
            RequiresOccupancy = definition.RequiresOccupancy,
            Fields = fieldDtos
        };
    }

    public async Task<List<ServiceRequestLocationDto>>
        GetAvailableRequestLocationsAsync(
            Guid accountId,
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var today =
            DateOnly.FromDateTime(DateTime.Today);

        var roles =
            await dbContext.UserAccountRoles
                .AsNoTracking()
                .Where(x =>
                    x.UserAccount.UserId == userId &&
                    x.UserAccount.AccountId == accountId)
                .Select(x => new
                {
                    x.Role,
                    x.OrganizationId
                })
                .ToListAsync(cancellationToken);

        var canSeeAllOccupancies =
            roles.Any(x =>
                x.Role == UserRole.AccountAdmin ||
                x.Role == UserRole.PropertyAdmin);

        if (canSeeAllOccupancies)
        {
            return await (
                from occupancy in dbContext.Occupancies.AsNoTracking()

                join organization in dbContext.Organizations.AsNoTracking()
                    on occupancy.TenantOrganizationId
                    equals organization.Id

                join unit in dbContext.Units.AsNoTracking()
                    on occupancy.UnitId equals unit.Id

                join building in dbContext.Buildings.AsNoTracking()
                    on unit.BuildingId equals building.Id

                where
                    occupancy.AccountId == accountId &&
                    occupancy.ValidFrom <= today &&
                    (!occupancy.ValidTo.HasValue ||
                    occupancy.ValidTo.Value >= today)

                orderby
                    organization.Name,
                    building.Name,
                    unit.Name

                select new ServiceRequestLocationDto
                {
                    OccupancyId = occupancy.Id,

                    OrganizationId = organization.Id,
                    OrganizationName = organization.Name,

                    BuildingId = building.Id,
                    BuildingName = building.Name,

                    UnitId = unit.Id,
                    UnitName = unit.Name
                })
                .ToListAsync(cancellationToken);
        }

        var organizationIds =
            roles
                .Where(x =>
                    x.Role == UserRole.TenantAdmin ||
                    x.Role == UserRole.TenantUser)
                .Where(x => x.OrganizationId.HasValue)
                .Select(x => x.OrganizationId!.Value)
                .Distinct()
                .ToList();

        if (organizationIds.Count == 0)
        {
            return [];
        }

        return await (
            from occupancy in dbContext.Occupancies.AsNoTracking()

            join organization in dbContext.Organizations.AsNoTracking()
                on occupancy.TenantOrganizationId
                equals organization.Id

            join unit in dbContext.Units.AsNoTracking()
                on occupancy.UnitId equals unit.Id

            join building in dbContext.Buildings.AsNoTracking()
                on unit.BuildingId equals building.Id

            where
                occupancy.AccountId == accountId &&
                organizationIds.Contains(
                    occupancy.TenantOrganizationId) &&
                occupancy.ValidFrom <= today &&
                (!occupancy.ValidTo.HasValue ||
                occupancy.ValidTo.Value >= today)

            orderby
                building.Name,
                unit.Name

            select new ServiceRequestLocationDto
            {
                OccupancyId = occupancy.Id,

                OrganizationId = organization.Id,
                OrganizationName = organization.Name,

                BuildingId = building.Id,
                BuildingName = building.Name,

                UnitId = unit.Id,
                UnitName = unit.Name
            })
            .ToListAsync(cancellationToken);
    }
}


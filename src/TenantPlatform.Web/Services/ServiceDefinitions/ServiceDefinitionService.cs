using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Core.Localization;
namespace TenantPlatform.Web.Services.ServiceDefinitions;
using TenantPlatform.Core.Localization;

public class ServiceDefinitionService : IServiceDefinitionService
{
    private readonly IDbContextFactory<TenantPlatformDbContext>
        _dbContextFactory;

    public ServiceDefinitionService(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<ServiceDefinitionListItemDto>>
        GetServiceDefinitionsAsync(
            Guid accountId,
            string languageCode,
            CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var defaultLanguage = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == accountId)
            .Select(x => x.DefaultLanguage)
            .SingleAsync(cancellationToken);

        var definitions = await dbContext.ServiceDefinitions
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);

        var definitionIds =
            definitions.Select(x => x.Id).ToList();

        var translations = await dbContext
            .ServiceDefinitionTranslations
            .AsNoTracking()
            .Where(x => definitionIds.Contains(x.ServiceDefinitionId))
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

                return new ServiceDefinitionListItemDto
                {
                    Id = definition.Id,
                    Code = definition.Code,
                    Name = translation?.Name ?? definition.Code,
                    Category = definition.Category,
                    HandlerType = definition.HandlerType,
                    RequiresApproval = definition.RequiresApproval,
                    IsBookableByTenant =
                        definition.IsBookableByTenant,
                    RequiresOccupancy = definition.RequiresOccupancy,
                    IsActive = definition.IsActive
                };
            })
            .ToList();
    }

    public async Task<ServiceDefinitionDetailsDto?>
        GetServiceDefinitionAsync(
            Guid accountId,
            Guid serviceDefinitionId,
            string languageCode,
            CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var definition =
            await dbContext.ServiceDefinitions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == serviceDefinitionId &&
                        x.AccountId == accountId,
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

        var defaultLanguage = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == accountId)
            .Select(x => x.DefaultLanguage)
            .SingleAsync(cancellationToken);

        var translation =
            TranslationHelper.Select(
                translations.Where(x =>
                    x.ServiceDefinitionId == serviceDefinitionId),
                x => x.LanguageCode,
                languageCode,
                defaultLanguage);

        var fields =
            await GetFieldsAsync(
                dbContext,
                serviceDefinitionId,
                languageCode,
                defaultLanguage,
                cancellationToken);

        var providers =
            await (
                from provider in dbContext.ServiceDefinitionProviders
                    .AsNoTracking()
                join organization in dbContext.Organizations
                    on provider.ServiceProviderOrganizationId
                    equals organization.Id
                where provider.ServiceDefinitionId
                      == serviceDefinitionId
                orderby provider.IsDefault descending,
                    organization.Name
                select new ServiceDefinitionProviderDto
                {
                    Id = provider.Id,

                    ServiceProviderOrganizationId =
                        provider.ServiceProviderOrganizationId,

                    ServiceProviderOrganizationName =
                        organization.Name,

                    IntegrationType =
                        provider.IntegrationType,

                    RequestEmailAddress =
                        provider.RequestEmailAddress,

                    IsDefault = provider.IsDefault,

                    IsActive = provider.IsActive
                })
                .ToListAsync(cancellationToken);

        return new ServiceDefinitionDetailsDto
        {
            Id = definition.Id,
            Code = definition.Code,
            Name = translation?.Name ?? definition.Code,
            Description = translation?.Description,
            Category = definition.Category,
            HandlerType = definition.HandlerType,
            RequiresApproval = definition.RequiresApproval,
            IsBookableByTenant =
                definition.IsBookableByTenant,
            RequiresOccupancy = definition.RequiresOccupancy,
            EstimatedDurationMinutes =
                definition.EstimatedDurationMinutes,
            IsActive = definition.IsActive,
            Fields = fields,
            Providers = providers
        };
    }

    public async Task<Guid> CreateServiceDefinitionAsync(
        Guid accountId,
        CreateServiceDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var normalizedCode =
            request.Code.Trim().ToUpperInvariant();

        var codeExists =
            await dbContext.ServiceDefinitions.AnyAsync(
                x =>
                    x.AccountId == accountId &&
                    x.Code == normalizedCode,
                cancellationToken);

        if (codeExists)
        {
            throw new InvalidOperationException(
                "Service definition code already exists.");
        }

        var id = Guid.NewGuid();

        dbContext.ServiceDefinitions.Add(
            new ServiceDefinition
            {
                Id = id,
                AccountId = accountId,
                Code = normalizedCode,
                Category = NormalizeOptional(request.Category),
                HandlerType =
                    string.IsNullOrWhiteSpace(request.HandlerType)
                        ? "Generic"
                        : request.HandlerType.Trim(),
                RequiresApproval = request.RequiresApproval,
                IsBookableByTenant =
                    request.IsBookableByTenant,
                RequiresOccupancy = request.RequiresOccupancy,
                EstimatedDurationMinutes =
                    request.EstimatedDurationMinutes,
                IsActive = request.IsActive
            });

        dbContext.ServiceDefinitionTranslations.AddRange(
            new ServiceDefinitionTranslation
            {
                Id = Guid.NewGuid(),
                ServiceDefinitionId = id,
                LanguageCode = SupportedLanguages.NbNo,
                Name = request.NorwegianName.Trim(),
                Description =
                    NormalizeOptional(
                        request.NorwegianDescription)
            },
            new ServiceDefinitionTranslation
            {
                Id = Guid.NewGuid(),
                ServiceDefinitionId = id,
                LanguageCode = SupportedLanguages.EnGb,
                Name = request.EnglishName.Trim(),
                Description =
                    NormalizeOptional(
                        request.EnglishDescription)
            });

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return id;
    }

    public async Task UpdateServiceDefinitionAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        UpdateServiceDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var definition =
            await dbContext.ServiceDefinitions
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == serviceDefinitionId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (definition is null)
        {
            throw new InvalidOperationException(
                "Service definition was not found.");
        }

        var normalizedCode =
            request.Code.Trim().ToUpperInvariant();

        var duplicateCode =
            await dbContext.ServiceDefinitions.AnyAsync(
                x =>
                    x.AccountId == accountId &&
                    x.Code == normalizedCode &&
                    x.Id != serviceDefinitionId,
                cancellationToken);

        if (duplicateCode)
        {
            throw new InvalidOperationException(
                "Service definition code already exists.");
        }

        definition.Code = normalizedCode;
        definition.Category =
            NormalizeOptional(request.Category);

        definition.HandlerType =
            string.IsNullOrWhiteSpace(request.HandlerType)
                ? "Generic"
                : request.HandlerType.Trim();

        definition.RequiresApproval =
            request.RequiresApproval;

        definition.IsBookableByTenant =
            request.IsBookableByTenant;

        definition.RequiresOccupancy =
            request.RequiresOccupancy;

        definition.EstimatedDurationMinutes =
            request.EstimatedDurationMinutes;

        definition.IsActive =
            request.IsActive;

        await UpsertTranslationAsync(
            dbContext,
            serviceDefinitionId,
            SupportedLanguages.NbNo,
            request.NorwegianName,
            request.NorwegianDescription,
            cancellationToken);

        await UpsertTranslationAsync(
            dbContext,
            serviceDefinitionId,
            SupportedLanguages.EnGb,
            request.EnglishName,
            request.EnglishDescription,
            cancellationToken);

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    public async Task<ServiceDefinitionDeleteCheckResult>
        CanDeleteServiceDefinitionAsync(
            Guid accountId,
            Guid serviceDefinitionId,
            CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var exists =
            await dbContext.ServiceDefinitions
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Id == serviceDefinitionId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (!exists)
        {
            return ServiceDefinitionDeleteCheckResult
                .NotAllowed(
                    "ServiceDefinitionNotFound");
        }

        var hasRequests =
            await dbContext.ServiceRequests
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ServiceDefinitionId ==
                        serviceDefinitionId,
                    cancellationToken);

        if (hasRequests)
        {
            return ServiceDefinitionDeleteCheckResult
                .NotAllowed(
                    "ServiceDefinitionHasRequests");
        }

        return ServiceDefinitionDeleteCheckResult.Allowed();
    }

    public async Task DeleteServiceDefinitionAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var deleteCheck =
            await CanDeleteServiceDefinitionAsync(
                accountId,
                serviceDefinitionId,
                cancellationToken);

        if (!deleteCheck.CanDelete)
        {
            throw new ServiceDefinitionDeleteNotAllowedException(
                deleteCheck.Reason ??
                "ServiceDefinitionCannotBeDeleted");
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var definition =
            await dbContext.ServiceDefinitions
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == serviceDefinitionId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (definition is null)
        {
            throw new InvalidOperationException(
                "Service definition was not found.");
        }

        // Translation-configen din bruker Restrict,
        // så disse må slettes eksplisitt.
        var translations =
            await dbContext.ServiceDefinitionTranslations
                .Where(x =>
                    x.ServiceDefinitionId == serviceDefinitionId)
                .ToListAsync(cancellationToken);

        dbContext.ServiceDefinitionTranslations
            .RemoveRange(translations);

        dbContext.ServiceDefinitions.Remove(definition);

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    private static async Task<List<ServiceDefinitionFieldDto>>
        GetFieldsAsync(
            TenantPlatformDbContext dbContext,
            Guid serviceDefinitionId,
            string languageCode,
            string defaultLanguage,
            CancellationToken cancellationToken)
    {
        var fields =
            await dbContext.ServiceDefinitionFields
                .AsNoTracking()
                .Where(x =>
                    x.ServiceDefinitionId == serviceDefinitionId)
                .OrderBy(x => x.SortOrder)
                .ToListAsync(cancellationToken);

        if (fields.Count == 0)
        {
            return [];
        }

        var fieldIds =
            fields.Select(x => x.Id).ToList();

        var translations =
            await dbContext.ServiceDefinitionFieldTranslations
                .AsNoTracking()
                .Where(x =>
                    fieldIds.Contains(
                        x.ServiceDefinitionFieldId))
                .ToListAsync(cancellationToken);

        return fields.Select(field =>
        {
            var translation =
                TranslationHelper.Select(
                    translations.Where(x =>
                        x.ServiceDefinitionFieldId == field.Id),
                    x => x.LanguageCode,
                    languageCode,
                    defaultLanguage);

            return new ServiceDefinitionFieldDto
            {
                Id = field.Id,
                Key = field.Key,
                Label = translation?.Label ?? field.Key,
                Placeholder = translation?.Placeholder,
                HelpText = translation?.HelpText,
                FieldType = field.FieldType,
                IsRequired = field.IsRequired,
                SortOrder = field.SortOrder,
                Options = field.Options
            };
        }).ToList();
    }



    private static async Task UpsertTranslationAsync(
        TenantPlatformDbContext dbContext,
        Guid serviceDefinitionId,
        string languageCode,
        string name,
        string? description,
        CancellationToken cancellationToken)
    {
        var translation =
            await dbContext.ServiceDefinitionTranslations
                .SingleOrDefaultAsync(
                    x =>
                        x.ServiceDefinitionId ==
                        serviceDefinitionId &&
                        x.LanguageCode == languageCode,
                    cancellationToken);

        if (translation is null)
        {
            dbContext.ServiceDefinitionTranslations.Add(
                new ServiceDefinitionTranslation
                {
                    Id = Guid.NewGuid(),
                    ServiceDefinitionId =
                        serviceDefinitionId,
                    LanguageCode = languageCode,
                    Name = name.Trim(),
                    Description =
                        NormalizeOptional(description)
                });

            return;
        }

        translation.Name = name.Trim();
        translation.Description =
            NormalizeOptional(description);
    }

    private static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }


    public async Task<IReadOnlyList<ServiceDefinitionProviderDto>>
        GetProvidersAsync(
            Guid accountId,
            Guid serviceDefinitionId,
            CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var serviceExists =
            await dbContext.ServiceDefinitions
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Id == serviceDefinitionId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (!serviceExists)
        {
            return Array.Empty<ServiceDefinitionProviderDto>();
        }

        return await dbContext.ServiceDefinitionProviders
            .AsNoTracking()
            .Where(x =>
                x.AccountId == accountId &&
                x.ServiceDefinitionId == serviceDefinitionId)
            .Join(
                dbContext.Organizations,
                provider =>
                    provider.ServiceProviderOrganizationId,
                organization =>
                    organization.Id,
                (provider, organization) =>
                    new ServiceDefinitionProviderDto
                    {
                        Id =
                            provider.Id,

                        ServiceProviderOrganizationId =
                            provider.ServiceProviderOrganizationId,

                        ServiceProviderOrganizationName =
                            organization.Name,

                        IntegrationType =
                            provider.IntegrationType,

                        RequestEmailAddress =
                            provider.RequestEmailAddress,

                        IsDefault =
                            provider.IsDefault,

                        IsActive =
                            provider.IsActive
                    })
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.ServiceProviderOrganizationName)
            .ToListAsync(cancellationToken);
    }

    public async Task<ServiceDefinitionProviderEditDto?>
        GetProviderAsync(
            Guid accountId,
            Guid serviceDefinitionId,
            Guid providerId,
            CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        return await dbContext.ServiceDefinitionProviders
            .AsNoTracking()
            .Where(x =>
                x.Id == providerId &&
                x.AccountId == accountId &&
                x.ServiceDefinitionId == serviceDefinitionId)
            .Select(x =>
                new ServiceDefinitionProviderEditDto
                {
                    ServiceProviderOrganizationId =
                        x.ServiceProviderOrganizationId,

                    IntegrationType =
                        x.IntegrationType,

                    RequestEmailAddress =
                        x.RequestEmailAddress,

                    IsDefault =
                        x.IsDefault,

                    IsActive =
                        x.IsActive
                })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceProviderOrganizationOptionDto>>
        GetProviderOrganizationOptionsAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        return await dbContext.Organizations
            .AsNoTracking()
            .Where(x =>
                x.AccountId == accountId &&
                x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x =>
                new ServiceProviderOrganizationOptionDto
                {
                    Id = x.Id,
                    Name = x.Name
                })
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> CreateProviderAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        ServiceDefinitionProviderEditDto model,
        CancellationToken cancellationToken = default)
    {
        ValidateProviderModel(model);

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var serviceExists =
            await dbContext.ServiceDefinitions
                .AnyAsync(
                    x =>
                        x.Id == serviceDefinitionId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (!serviceExists)
        {
            throw new InvalidOperationException(
                "ServiceDefinitionNotFound");
        }

        var organizationExists =
            await dbContext.Organizations
                .AnyAsync(
                    x =>
                        x.Id ==
                            model.ServiceProviderOrganizationId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (!organizationExists)
        {
            throw new InvalidOperationException(
                "ServiceProviderOrganizationNotFound");
        }

        var duplicateExists =
            await dbContext.ServiceDefinitionProviders
                .AnyAsync(
                    x =>
                        x.AccountId == accountId &&
                        x.ServiceDefinitionId ==
                            serviceDefinitionId &&
                        x.ServiceProviderOrganizationId ==
                            model.ServiceProviderOrganizationId,
                    cancellationToken);

        if (duplicateExists)
        {
            throw new InvalidOperationException(
                "ServiceProviderAlreadyExists");
        }

        if (model.IsDefault)
        {
            var existingDefaults =
                await dbContext.ServiceDefinitionProviders
                    .Where(x =>
                        x.AccountId == accountId &&
                        x.ServiceDefinitionId ==
                            serviceDefinitionId &&
                        x.IsDefault)
                    .ToListAsync(cancellationToken);

            foreach (var provider in existingDefaults)
            {
                provider.IsDefault = false;
            }
        }

        var entity =
            new ServiceDefinitionProvider
            {
                Id =
                    Guid.NewGuid(),

                AccountId =
                    accountId,

                ServiceDefinitionId =
                    serviceDefinitionId,

                ServiceProviderOrganizationId =
                    model.ServiceProviderOrganizationId,

                IntegrationType =
                    model.IntegrationType,

                RequestEmailAddress =
                    model.RequestEmailAddress,

                IsDefault =
                    model.IsDefault,

                IsActive =
                    model.IsActive
            };

        dbContext.ServiceDefinitionProviders.Add(
            entity);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return entity.Id;
    }

    public async Task UpdateProviderAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        Guid providerId,
        ServiceDefinitionProviderEditDto model,
        CancellationToken cancellationToken = default)
    {
        ValidateProviderModel(model);

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var provider =
            await dbContext.ServiceDefinitionProviders
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == providerId &&
                        x.AccountId == accountId &&
                        x.ServiceDefinitionId ==
                            serviceDefinitionId,
                    cancellationToken);

        if (provider is null)
        {
            throw new InvalidOperationException(
                "ServiceProviderNotFound");
        }

        var organizationExists =
            await dbContext.Organizations
                .AnyAsync(
                    x =>
                        x.Id ==
                            model.ServiceProviderOrganizationId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (!organizationExists)
        {
            throw new InvalidOperationException(
                "ServiceProviderOrganizationNotFound");
        }

        var duplicateExists =
            await dbContext.ServiceDefinitionProviders
                .AnyAsync(
                    x =>
                        x.Id != providerId &&
                        x.AccountId == accountId &&
                        x.ServiceDefinitionId ==
                            serviceDefinitionId &&
                        x.ServiceProviderOrganizationId ==
                            model.ServiceProviderOrganizationId,
                    cancellationToken);

        if (duplicateExists)
        {
            throw new InvalidOperationException(
                "ServiceProviderAlreadyExists");
        }

        if (model.IsDefault)
        {
            var existingDefaults =
                await dbContext.ServiceDefinitionProviders
                    .Where(x =>
                        x.Id != providerId &&
                        x.AccountId == accountId &&
                        x.ServiceDefinitionId ==
                            serviceDefinitionId &&
                        x.IsDefault)
                    .ToListAsync(cancellationToken);

            foreach (var existing in existingDefaults)
            {
                existing.IsDefault = false;
            }
        }

        provider.ServiceProviderOrganizationId =
            model.ServiceProviderOrganizationId;

        provider.IntegrationType =
            model.IntegrationType;

        provider.RequestEmailAddress =
            model.RequestEmailAddress;

        provider.IsDefault =
            model.IsDefault;

        provider.IsActive =
            model.IsActive;

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    public async Task DeleteProviderAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        Guid providerId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var provider =
            await dbContext.ServiceDefinitionProviders
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == providerId &&
                        x.AccountId == accountId &&
                        x.ServiceDefinitionId ==
                            serviceDefinitionId,
                    cancellationToken);

        if (provider is null)
        {
            return;
        }

        dbContext.ServiceDefinitionProviders.Remove(
            provider);

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    private static void ValidateProviderModel(
        ServiceDefinitionProviderEditDto model)
    {
        if (model.ServiceProviderOrganizationId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "ServiceProviderRequired");
        }

        if (model.IntegrationType ==
                ServiceProviderIntegrationType.Email &&
            string.IsNullOrWhiteSpace(
                model.RequestEmailAddress))
        {
            throw new InvalidOperationException(
                "ServiceProviderEmailRequired");
        }

        if (!model.IsActive)
        {
            model.IsDefault = false;
        }

        if (model.IntegrationType !=
            ServiceProviderIntegrationType.Email)
        {
            model.RequestEmailAddress = null;
        }
        else
        {
            model.RequestEmailAddress =
                model.RequestEmailAddress?.Trim();
        }
    }

}


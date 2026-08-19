using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Localization;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.ServiceDefinitionFields;

public class ServiceDefinitionFieldService
    : IServiceDefinitionFieldService
{
    private readonly IDbContextFactory<TenantPlatformDbContext>
        _dbContextFactory;

    public ServiceDefinitionFieldService(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<ServiceDefinitionFieldDetailsDto?> GetFieldAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        Guid fieldId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var field = await (
            from f in dbContext.ServiceDefinitionFields.AsNoTracking()
            join definition in dbContext.ServiceDefinitions.AsNoTracking()
                on f.ServiceDefinitionId equals definition.Id
            where f.Id == fieldId
                  && f.ServiceDefinitionId == serviceDefinitionId
                  && definition.AccountId == accountId
            select f)
            .SingleOrDefaultAsync(cancellationToken);

        if (field is null)
        {
            return null;
        }

        var translations =
            await dbContext.ServiceDefinitionFieldTranslations
                .AsNoTracking()
                .Where(x =>
                    x.ServiceDefinitionFieldId == fieldId)
                .ToListAsync(cancellationToken);

        var norwegian = translations.SingleOrDefault(
            x => x.LanguageCode == SupportedLanguages.NbNo);

        var english = translations.SingleOrDefault(
            x => x.LanguageCode == SupportedLanguages.EnGb);

        return new ServiceDefinitionFieldDetailsDto
        {
            Id = field.Id,
            ServiceDefinitionId = field.ServiceDefinitionId,
            Key = field.Key,
            FieldType = field.FieldType,
            IsRequired = field.IsRequired,
            SortOrder = field.SortOrder,
            OptionsText = DeserializeOptions(field.Options),

            NorwegianLabel = norwegian?.Label ?? string.Empty,
            NorwegianPlaceholder = norwegian?.Placeholder,
            NorwegianHelpText = norwegian?.HelpText,

            EnglishLabel = english?.Label ?? string.Empty,
            EnglishPlaceholder = english?.Placeholder,
            EnglishHelpText = english?.HelpText
        };
    }

    public async Task<Guid> CreateFieldAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        CreateServiceDefinitionFieldRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        await ValidateDefinitionAsync(
            dbContext,
            accountId,
            serviceDefinitionId,
            cancellationToken);

        var normalizedKey =
            NormalizeKey(request.Key);

        var keyExists =
            await dbContext.ServiceDefinitionFields.AnyAsync(
                x =>
                    x.ServiceDefinitionId == serviceDefinitionId &&
                    x.Key == normalizedKey,
                cancellationToken);

        if (keyExists)
        {
            throw new InvalidOperationException(
                "Service definition field key already exists.");
        }

        var fieldId = Guid.NewGuid();

        dbContext.ServiceDefinitionFields.Add(
            new ServiceDefinitionField
            {
                Id = fieldId,
                ServiceDefinitionId = serviceDefinitionId,
                Key = normalizedKey,
                FieldType = request.FieldType,
                IsRequired = request.IsRequired,
                SortOrder = request.SortOrder,
                Options = SerializeOptions(
                    request.FieldType,
                    request.OptionsText)
            });

        dbContext.ServiceDefinitionFieldTranslations.AddRange(
            CreateTranslation(
                fieldId,
                SupportedLanguages.NbNo,
                request.NorwegianLabel,
                request.NorwegianPlaceholder,
                request.NorwegianHelpText),

            CreateTranslation(
                fieldId,
                SupportedLanguages.EnGb,
                request.EnglishLabel,
                request.EnglishPlaceholder,
                request.EnglishHelpText));

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return fieldId;
    }

    public async Task UpdateFieldAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        Guid fieldId,
        UpdateServiceDefinitionFieldRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        await ValidateDefinitionAsync(
            dbContext,
            accountId,
            serviceDefinitionId,
            cancellationToken);

        var field =
            await dbContext.ServiceDefinitionFields
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == fieldId &&
                        x.ServiceDefinitionId == serviceDefinitionId,
                    cancellationToken);

        if (field is null)
        {
            throw new InvalidOperationException(
                "Service definition field was not found.");
        }

        var normalizedKey =
            NormalizeKey(request.Key);

        var duplicateKey =
            await dbContext.ServiceDefinitionFields.AnyAsync(
                x =>
                    x.ServiceDefinitionId == serviceDefinitionId &&
                    x.Key == normalizedKey &&
                    x.Id != fieldId,
                cancellationToken);

        if (duplicateKey)
        {
            throw new InvalidOperationException(
                "Service definition field key already exists.");
        }

        field.Key = normalizedKey;
        field.FieldType = request.FieldType;
        field.IsRequired = request.IsRequired;
        field.SortOrder = request.SortOrder;
        field.Options = SerializeOptions(
            request.FieldType,
            request.OptionsText);

        await UpsertTranslationAsync(
            dbContext,
            fieldId,
            SupportedLanguages.NbNo,
            request.NorwegianLabel,
            request.NorwegianPlaceholder,
            request.NorwegianHelpText,
            cancellationToken);

        await UpsertTranslationAsync(
            dbContext,
            fieldId,
            SupportedLanguages.EnGb,
            request.EnglishLabel,
            request.EnglishPlaceholder,
            request.EnglishHelpText,
            cancellationToken);

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    public async Task DeleteFieldAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        Guid fieldId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        await ValidateDefinitionAsync(
            dbContext,
            accountId,
            serviceDefinitionId,
            cancellationToken);

        var field =
            await dbContext.ServiceDefinitionFields
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == fieldId &&
                        x.ServiceDefinitionId == serviceDefinitionId,
                    cancellationToken);

        if (field is null)
        {
            throw new InvalidOperationException(
                "Service definition field was not found.");
        }

        var hasValues =
            await dbContext.ServiceRequestFieldValues
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ServiceDefinitionFieldId == fieldId,
                    cancellationToken);

        if (hasValues)
        {
            throw new InvalidOperationException(
                "The field cannot be deleted because it has been used in service requests.");
        }

        var translations =
            await dbContext.ServiceDefinitionFieldTranslations
                .Where(x =>
                    x.ServiceDefinitionFieldId == fieldId)
                .ToListAsync(cancellationToken);

        dbContext.ServiceDefinitionFieldTranslations
            .RemoveRange(translations);

        dbContext.ServiceDefinitionFields.Remove(field);

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    private static async Task ValidateDefinitionAsync(
        TenantPlatformDbContext dbContext,
        Guid accountId,
        Guid serviceDefinitionId,
        CancellationToken cancellationToken)
    {
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
            throw new InvalidOperationException(
                "Service definition was not found.");
        }
    }

    private static string NormalizeKey(
        string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string? SerializeOptions(
        ServiceFieldType fieldType,
        string? optionsText)
    {
        if (fieldType is not
            (ServiceFieldType.Choice or
             ServiceFieldType.MultiChoice))
        {
            return null;
        }

        var options = ParseOptions(optionsText);

        if (options.Count == 0)
        {
            throw new InvalidOperationException(
                "Choice fields must contain at least one option.");
        }

        return JsonSerializer.Serialize(
            options.Select(x => new
            {
                value = x
            }));
    }

    private static string? DeserializeOptions(
        string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
        {
            return null;
        }

        try
        {
            using var document =
                JsonDocument.Parse(options);

            return string.Join(
                Environment.NewLine,
                document.RootElement
                    .EnumerateArray()
                    .Select(x =>
                        x.GetProperty("value").GetString())
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> ParseOptions(
        string? optionsText)
    {
        if (string.IsNullOrWhiteSpace(optionsText))
        {
            return [];
        }

        return optionsText
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ServiceDefinitionFieldTranslation
        CreateTranslation(
            Guid fieldId,
            string languageCode,
            string label,
            string? placeholder,
            string? helpText)
    {
        return new ServiceDefinitionFieldTranslation
        {
            Id = Guid.NewGuid(),
            ServiceDefinitionFieldId = fieldId,
            LanguageCode = languageCode,
            Label = label.Trim(),
            Placeholder = NormalizeOptional(placeholder),
            HelpText = NormalizeOptional(helpText)
        };
    }

    private static async Task UpsertTranslationAsync(
        TenantPlatformDbContext dbContext,
        Guid fieldId,
        string languageCode,
        string label,
        string? placeholder,
        string? helpText,
        CancellationToken cancellationToken)
    {
        var translation =
            await dbContext.ServiceDefinitionFieldTranslations
                .SingleOrDefaultAsync(
                    x =>
                        x.ServiceDefinitionFieldId == fieldId &&
                        x.LanguageCode == languageCode,
                    cancellationToken);

        if (translation is null)
        {
            dbContext.ServiceDefinitionFieldTranslations.Add(
                CreateTranslation(
                    fieldId,
                    languageCode,
                    label,
                    placeholder,
                    helpText));

            return;
        }

        translation.Label = label.Trim();
        translation.Placeholder =
            NormalizeOptional(placeholder);
        translation.HelpText =
            NormalizeOptional(helpText);
    }

    private static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}


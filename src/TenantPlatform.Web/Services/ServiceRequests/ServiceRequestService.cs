using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Services;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Core.Localization;
using TenantPlatform.Web.Email;


namespace TenantPlatform.Web.Services.ServiceRequests;

public class ServiceRequestService : IServiceRequestService
{
    private readonly IDbContextFactory<TenantPlatformDbContext> _dbContextFactory;
private readonly IServiceRequestEmailComposer _emailComposer;

private readonly IServiceRequestEmailAddressService _emailAddressService;

    public ServiceRequestService(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory,
        IServiceRequestEmailComposer emailComposer,
        IServiceRequestEmailAddressService emailAddressService)
    {
        _dbContextFactory = dbContextFactory;
        _emailComposer = emailComposer;
        _emailAddressService = emailAddressService;
    }

    public async Task<Guid> CreateServiceRequestAsync(
        Guid accountId,
        Guid requesterUserId,
        CreateServiceRequestRequest request,
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
                        x.Id == request.ServiceDefinitionId &&
                        x.AccountId == accountId &&
                        x.IsActive &&
                        x.IsBookableByTenant,
                    cancellationToken);

        if (definition is null)
        {
            throw new ServiceRequestValidationException(
                "ServiceNotAvailable");
        }

        var today =
            DateOnly.FromDateTime(DateTime.Today);

        var occupancy =
            await (
                from o in dbContext.Occupancies
                join unit in dbContext.Units
                    on o.UnitId equals unit.Id
                where
                    o.Id == request.OccupancyId &&
                    o.AccountId == accountId &&
                    o.ValidFrom <= today &&
                    (!o.ValidTo.HasValue ||
                    o.ValidTo.Value >= today)
                select new
                {
                    Occupancy = o,
                    unit.BuildingId
                })
                .SingleOrDefaultAsync(cancellationToken);

        if (occupancy is null)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestInvalidOccupancy");
        }

        var userRoles =
            await dbContext.UserAccountRoles
                .AsNoTracking()
                .Where(x =>
                    x.UserAccount.UserId == requesterUserId &&
                    x.UserAccount.AccountId == accountId)
                .Select(x => new
                {
                    x.Role,
                    x.OrganizationId
                })
                .ToListAsync(cancellationToken);

        var isAdministrator =
            userRoles.Any(x =>
                x.Role == UserRole.AccountAdmin ||
                x.Role == UserRole.PropertyAdmin);

        var hasTenantAccess =
            userRoles.Any(x =>
                (x.Role == UserRole.TenantAdmin ||
                x.Role == UserRole.TenantUser) &&
                x.OrganizationId ==
                    occupancy.Occupancy.TenantOrganizationId);

        if (!isAdministrator && !hasTenantAccess)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestInvalidRequester");
        }

        var fields =
            await dbContext.ServiceDefinitionFields
                .AsNoTracking()
                .Where(x =>
                    x.ServiceDefinitionId ==
                    request.ServiceDefinitionId)
                .OrderBy(x => x.SortOrder)
                .ToListAsync(cancellationToken);

        var normalizedValues =
            ValidateAndNormalizeValues(
                fields,
                request.Values);

        var serviceRequestId = Guid.NewGuid();

        var serviceRequest =
            new ServiceRequest
            {
                Id = serviceRequestId,

                AccountId = accountId,

                ServiceDefinitionId =
                    request.ServiceDefinitionId,

                RequesterUserId =
                    requesterUserId,

                RequesterOrganizationId =
                    occupancy.Occupancy.TenantOrganizationId,

                BuildingId =
                    occupancy.BuildingId,

                UnitId =
                    occupancy.Occupancy.UnitId,

                Status =
                    ServiceRequestStatus.Submitted,

                CreatedAt =
                    DateTimeOffset.UtcNow,

                SubmittedAt =
                    DateTimeOffset.UtcNow,

                ReplyToken =
                    CreateReplyToken()
            };

        dbContext.ServiceRequests.Add(serviceRequest);

        foreach (var value in normalizedValues)
        {
            dbContext.ServiceRequestFieldValues.Add(
                new ServiceRequestFieldValue
                {
                    Id = Guid.NewGuid(),
                    ServiceRequestId = serviceRequestId,
                    ServiceDefinitionFieldId =
                        value.ServiceDefinitionFieldId,
                    Value = value.Value
                });
        }

        dbContext.ServiceRequestMessages.Add(
            new ServiceRequestMessage
            {
                Id = Guid.NewGuid(),
                ServiceRequestId = serviceRequestId,
                Direction =
                    ServiceRequestMessageDirection.Outbound,
                Type =
                    ServiceRequestMessageType.System,
                EventType =
                    ServiceRequestEventType.Submitted,
                CreatedByUserId = requesterUserId,
                Body = "Service request submitted.",
                CreatedAt = DateTimeOffset.UtcNow
            });

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return serviceRequestId;
    }

    private static async Task ValidateRequesterAsync(
        TenantPlatformDbContext dbContext,
        Guid accountId,
        Guid requesterUserId,
        Guid requesterOrganizationId,
        CancellationToken cancellationToken)
    {
        var hasOrganizationAccess =
            await dbContext.UserAccountRoles
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.UserAccount.UserId == requesterUserId &&
                        x.UserAccount.AccountId == accountId &&
                        x.OrganizationId ==
                            requesterOrganizationId,
                    cancellationToken);

        if (!hasOrganizationAccess)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestInvalidRequester");
        }
    }

    private static async Task ValidateLocationAsync(
        TenantPlatformDbContext dbContext,
        Guid accountId,
        Guid requesterOrganizationId,
        Guid buildingId,
        Guid? unitId,
        bool requiresOccupancy,
        CancellationToken cancellationToken)
    {
        var buildingExists =
            await dbContext.Buildings
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.Id == buildingId &&
                        x.AccountId == accountId &&
                        x.IsActive,
                    cancellationToken);

        if (!buildingExists)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestInvalidBuilding");
        }

        if (unitId.HasValue)
        {
            var unitExists =
                await dbContext.Units
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.Id == unitId.Value &&
                            x.AccountId == accountId &&
                            x.BuildingId == buildingId &&
                            x.IsActive,
                        cancellationToken);

            if (!unitExists)
            {
                throw new ServiceRequestValidationException(
                    "ServiceRequestInvalidUnit");
            }
        }

        if (!requiresOccupancy)
        {
            return;
        }

        var today =
            DateOnly.FromDateTime(DateTime.Today);

        var hasOccupancy =
            await (
                from occupancy in dbContext.Occupancies.AsNoTracking()
                join unit in dbContext.Units.AsNoTracking()
                    on occupancy.UnitId equals unit.Id
                where
                    occupancy.AccountId == accountId &&
                    occupancy.TenantOrganizationId ==
                        requesterOrganizationId &&
                    unit.BuildingId == buildingId &&
                    (!unitId.HasValue ||
                     occupancy.UnitId == unitId.Value) &&
                    occupancy.ValidFrom <= today &&
                    (!occupancy.ValidTo.HasValue ||
                     occupancy.ValidTo.Value >= today)
                select occupancy.Id)
                .AnyAsync(cancellationToken);

        if (!hasOccupancy)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestRequiresOccupancy");
        }
    }

    private static List<CreateServiceRequestFieldValueRequest>
        ValidateAndNormalizeValues(
            IReadOnlyCollection<ServiceDefinitionField> fields,
            IReadOnlyCollection<CreateServiceRequestFieldValueRequest>
                submittedValues)
    {
        var submittedByField =
            submittedValues
                .GroupBy(x => x.ServiceDefinitionFieldId)
                .ToDictionary(
                    x => x.Key,
                    x => x.Last());

        var result =
            new List<CreateServiceRequestFieldValueRequest>();

        foreach (var field in fields)
        {
            submittedByField.TryGetValue(
                field.Id,
                out var submitted);

            var value =
                NormalizeValue(
                    field.FieldType,
                    submitted?.Value);

            if (field.IsRequired &&
                string.IsNullOrWhiteSpace(value))
            {
                throw new ServiceRequestValidationException(
                    "ServiceRequestRequiredFieldMissing");
            }

            if (value is null)
            {
                continue;
            }

            result.Add(
                new CreateServiceRequestFieldValueRequest
                {
                    ServiceDefinitionFieldId = field.Id,
                    Value = value
                });
        }

        return result;
    }

    private static string? NormalizeValue(
        ServiceFieldType fieldType,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();

        switch (fieldType)
        {
            case ServiceFieldType.Integer:
                if (!int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var integer))
                {
                    throw new ServiceRequestValidationException(
                        "ServiceRequestInvalidInteger");
                }

                return integer.ToString(
                    CultureInfo.InvariantCulture);

            case ServiceFieldType.Decimal:
                if (!decimal.TryParse(
                    value,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var decimalValue))
                {
                    throw new ServiceRequestValidationException(
                        "ServiceRequestInvalidDecimal");
                }

                return decimalValue.ToString(
                    CultureInfo.InvariantCulture);

            case ServiceFieldType.Boolean:
                if (!bool.TryParse(
                    value,
                    out var booleanValue))
                {
                    throw new ServiceRequestValidationException(
                        "ServiceRequestInvalidBoolean");
                }

                return booleanValue
                    ? "true"
                    : "false";

            case ServiceFieldType.Date:
                if (!DateOnly.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
                {
                    throw new ServiceRequestValidationException(
                        "ServiceRequestInvalidDate");
                }

                return date.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

            case ServiceFieldType.Email:
                if (!value.Contains('@'))
                {
                    throw new ServiceRequestValidationException(
                        "ServiceRequestInvalidEmail");
                }

                return value;

            default:
                return value;
        }
    }

    private static string CreateReplyToken()
    {
        return Convert.ToHexString(
                RandomNumberGenerator.GetBytes(16))
            .ToLowerInvariant();
    }

    public async Task<List<ServiceRequestListItemDto>> GetMyRequestsAsync(
        Guid accountId,
        Guid userId,
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

        var requests =
            await (
                from request in dbContext.ServiceRequests.AsNoTracking()

                join definition in dbContext.ServiceDefinitions.AsNoTracking()
                    on request.ServiceDefinitionId equals definition.Id

                join organization in dbContext.Organizations.AsNoTracking()
                    on request.RequesterOrganizationId equals organization.Id

                join building in dbContext.Buildings.AsNoTracking()
                    on request.BuildingId equals building.Id

                join unitJoin in dbContext.Units.AsNoTracking()
                    on request.UnitId equals unitJoin.Id
                    into units

                from unit in units.DefaultIfEmpty()

                where
                    request.AccountId == accountId &&
                    request.RequesterUserId == userId

                orderby request.CreatedAt descending

                select new
                {
                    Request = request,
                    Definition = definition,
                    OrganizationName = organization.Name,
                    BuildingName = building.Name,
                    UnitName = unit != null
                        ? unit.Name
                        : null
                })
                .ToListAsync(cancellationToken);

        if (requests.Count == 0)
        {
            return [];
        }

        var definitionIds =
            requests
                .Select(x => x.Definition.Id)
                .Distinct()
                .ToList();

        var translations =
            await dbContext.ServiceDefinitionTranslations
                .AsNoTracking()
                .Where(x =>
                    definitionIds.Contains(
                        x.ServiceDefinitionId))
                .ToListAsync(cancellationToken);

        return requests
            .Select(x =>
            {
                var translation =
                    TranslationHelper.Select(
                        translations.Where(t =>
                            t.ServiceDefinitionId ==
                            x.Definition.Id),
                        t => t.LanguageCode,
                        languageCode,
                        defaultLanguage);

                return new ServiceRequestListItemDto
                {
                    Id = x.Request.Id,

                    ServiceName =
                        translation?.Name ??
                        x.Definition.Code,

                    Category =
                        x.Definition.Category,

                    RequesterOrganizationName =
                        x.OrganizationName,

                    BuildingName =
                        x.BuildingName,

                    UnitName =
                        x.UnitName,

                    Status =
                        x.Request.Status,

                    CreatedAt =
                        x.Request.CreatedAt,

                    SubmittedAt =
                        x.Request.SubmittedAt,

                    CompletedAt =
                        x.Request.CompletedAt
                };
            })
            .ToList();
    }


    public async Task<ServiceRequestDetailsDto?> GetRequestAsync(
        Guid accountId,
        Guid requestId,
        Guid userId,
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

        var request =
            await (
                from r in dbContext.ServiceRequests.AsNoTracking()

                join definition in dbContext.ServiceDefinitions.AsNoTracking()
                    on r.ServiceDefinitionId equals definition.Id

                join organization in dbContext.Organizations.AsNoTracking()
                    on r.RequesterOrganizationId equals organization.Id

                join building in dbContext.Buildings.AsNoTracking()
                    on r.BuildingId equals building.Id

                join unitJoin in dbContext.Units.AsNoTracking()
                    on r.UnitId equals unitJoin.Id
                    into units

                from unit in units.DefaultIfEmpty()

                where
                    r.Id == requestId &&
                    r.AccountId == accountId

                select new
                {
                    Request = r,
                    Definition = definition,

                    OrganizationName =
                        organization.Name,

                    BuildingName =
                        building.Name,

                    UnitName =
                        unit != null
                            ? unit.Name
                            : null
                })
                .SingleOrDefaultAsync(cancellationToken);

        if (request is null)
        {
            return null;
        }

        string? providerName = null;

        if (request.Request.AssignedServiceProviderOrganizationId.HasValue)
        {
            providerName =
                await dbContext.Organizations
                    .AsNoTracking()
                    .Where(x =>
                        x.Id ==
                        request.Request.AssignedServiceProviderOrganizationId.Value)
                    .Select(x => x.Name)
                    .SingleOrDefaultAsync(cancellationToken);
        }

        //
        // Authorization
        //

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

        var isAdministrator =
            roles.Any(x =>
                x.Role == UserRole.AccountAdmin ||
                x.Role == UserRole.PropertyAdmin);

        var isRequester =
            request.Request.RequesterUserId == userId;

        var hasTenantAccess =
            roles.Any(x =>
                (x.Role == UserRole.TenantAdmin ||
                x.Role == UserRole.TenantUser) &&
                x.OrganizationId ==
                    request.Request.RequesterOrganizationId);

        var hasProviderAccess =
            request.Request.AssignedServiceProviderOrganizationId.HasValue &&
            roles.Any(x =>
                x.Role == UserRole.ServiceProviderUser &&
                x.OrganizationId ==
                    request.Request.AssignedServiceProviderOrganizationId);

        if (!isAdministrator &&
            !isRequester &&
            !hasTenantAccess &&
            !hasProviderAccess)
        {
            return null;
        }

        //
        // Service translation
        //

        var translations =
            await dbContext.ServiceDefinitionTranslations
                .AsNoTracking()
                .Where(x =>
                    x.ServiceDefinitionId ==
                    request.Definition.Id)
                .ToListAsync(cancellationToken);

        var translation =
            TranslationHelper.Select(
                translations,
                x => x.LanguageCode,
                languageCode,
                defaultLanguage);

        //
        // Values
        //

        var rawValues =
            await (
                from value in dbContext.ServiceRequestFieldValues
                    .AsNoTracking()

                join field in dbContext.ServiceDefinitionFields
                    .AsNoTracking()
                    on value.ServiceDefinitionFieldId
                    equals field.Id

                where
                    value.ServiceRequestId == requestId

                orderby field.SortOrder

                select new
                {
                    Value = value,
                    Field = field
                })
                .ToListAsync(cancellationToken);

        var fieldIds =
            rawValues
                .Select(x => x.Field.Id)
                .Distinct()
                .ToList();

        var fieldTranslations =
            await dbContext.ServiceDefinitionFieldTranslations
                .AsNoTracking()
                .Where(x =>
                    fieldIds.Contains(
                        x.ServiceDefinitionFieldId))
                .ToListAsync(cancellationToken);

        var values =
            rawValues
                .Select(x =>
                {
                    var fieldTranslation =
                        TranslationHelper.Select(
                            fieldTranslations.Where(t =>
                                t.ServiceDefinitionFieldId ==
                                x.Field.Id),
                            t => t.LanguageCode,
                            languageCode,
                            defaultLanguage);

                    return new ServiceRequestFieldValueDto
                    {
                        FieldId =
                            x.Field.Id,

                        Key =
                            x.Field.Key,

                        Label =
                            fieldTranslation?.Label ??
                            x.Field.Key,

                        FieldType =
                            x.Field.FieldType,

                        SortOrder =
                            x.Field.SortOrder,

                        Value =
                            x.Value.Value
                    };
                })
                .ToList();

        //
        // Timeline/messages
        //

        var messages =
            await dbContext.ServiceRequestMessages
                .AsNoTracking()
                .Where(x =>
                    x.ServiceRequestId == requestId)
                .OrderBy(x => x.CreatedAt)
                .Select(x =>
                    new ServiceRequestMessageDto
                    {
                        Id = x.Id,
                        Direction = x.Direction,
                        Type = x.Type,
                        EventType = x.EventType,
                        CreatedByUserId =
                            x.CreatedByUserId,
                        FromAddress =
                            x.FromAddress,
                        ToAddress =
                            x.ToAddress,
                        Subject =
                            x.Subject,
                        Body =
                            x.Body,
                        CreatedAt =
                            x.CreatedAt
                    })
                .ToListAsync(cancellationToken);

        return new ServiceRequestDetailsDto
        {
            Id =
                request.Request.Id,

            ServiceDefinitionId =
                request.Definition.Id,

            ServiceName =
                translation?.Name ??
                request.Definition.Code,

            ServiceDescription =
                translation?.Description,

            Category =
                request.Definition.Category,

            RequesterUserId =
                request.Request.RequesterUserId,

            RequesterOrganizationId =
                request.Request.RequesterOrganizationId,

            RequesterOrganizationName =
                request.OrganizationName,

            BuildingId =
                request.Request.BuildingId,

            BuildingName =
                request.BuildingName,

            UnitId =
                request.Request.UnitId,

            UnitName =
                request.UnitName,

            AssignedServiceProviderOrganizationId =
                request.Request.AssignedServiceProviderOrganizationId,

            AssignedServiceProviderOrganizationName =
                providerName,

            Status =
                request.Request.Status,

            CreatedAt =
                request.Request.CreatedAt,

            SubmittedAt =
                request.Request.SubmittedAt,

            CompletedAt =
                request.Request.CompletedAt,

            Values =
                values,

            Messages =
                messages
        };
    }

    public async Task ApproveRequestAsync(
        Guid accountId,
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var request =
            await dbContext.ServiceRequests
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == requestId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (request is null)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestNotFound");
        }

        var canApprove =
            await dbContext.UserAccountRoles
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.UserAccount.UserId == userId &&
                        x.UserAccount.AccountId == accountId &&
                        (
                            x.Role == UserRole.AccountAdmin ||
                            x.Role == UserRole.PropertyAdmin
                        ),
                    cancellationToken);

        if (!canApprove)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestApprovalNotAllowed");
        }

        if (request.Status != ServiceRequestStatus.Submitted)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestCannotBeApproved");
        }

        request.Status = ServiceRequestStatus.Approved;

        var defaultProvider =
            await dbContext.ServiceDefinitionProviders
                .AsNoTracking()
                .Where(x =>
                    x.ServiceDefinitionId == request.ServiceDefinitionId &&
                    x.AccountId == accountId &&
                    x.IsActive &&
                    x.IsDefault)
                .SingleOrDefaultAsync(cancellationToken);

        if (defaultProvider is not null)
        {
            request.AssignedServiceProviderOrganizationId =
                defaultProvider.ServiceProviderOrganizationId;

            dbContext.ServiceRequestMessages.Add(
                new ServiceRequestMessage
                {
                    Id = Guid.NewGuid(),
                    ServiceRequestId = request.Id,
                    Direction = ServiceRequestMessageDirection.Outbound,
                    Type = ServiceRequestMessageType.System,
                    EventType = ServiceRequestEventType.Assigned,
                    CreatedByUserId = userId,
                    Body = "Service request assigned to provider.",
                    CreatedAt = DateTimeOffset.UtcNow
                });
        }

        dbContext.ServiceRequestMessages.Add(
            new ServiceRequestMessage
            {
                Id = Guid.NewGuid(),
                ServiceRequestId = request.Id,
                Direction = ServiceRequestMessageDirection.Outbound,
                Type = ServiceRequestMessageType.System,
                EventType = ServiceRequestEventType.Approved,
                CreatedByUserId = userId,
                Body = "Service request approved.",
                CreatedAt = DateTimeOffset.UtcNow
            });

        await dbContext.SaveChangesAsync(cancellationToken);
        
        if (defaultProvider is not null)
        {
            await QueueProviderEmailAsync(
                accountId,
                requestId,
                cancellationToken);
        }
    }

    public async Task CompleteRequestAsync(
        Guid accountId,
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var request =
            await dbContext.ServiceRequests
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == requestId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (request is null)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestNotFound");
        }

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

        var isAdmin =
            roles.Any(x =>
                x.Role == UserRole.AccountAdmin ||
                x.Role == UserRole.PropertyAdmin);

        var isAssignedProvider =
            request.AssignedServiceProviderOrganizationId.HasValue &&
            roles.Any(x =>
                x.Role == UserRole.ServiceProviderUser &&
                x.OrganizationId ==
                    request.AssignedServiceProviderOrganizationId.Value);

        if (!isAdmin && !isAssignedProvider)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestCompletionNotAllowed");
        }

        var canCompleteInCurrentStatus =
            isAdmin
                ? request.Status == ServiceRequestStatus.Approved ||
                request.Status == ServiceRequestStatus.InProgress
                : request.Status == ServiceRequestStatus.InProgress;

        if (!canCompleteInCurrentStatus)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestCannotBeCompleted");
        }

        request.Status =
            ServiceRequestStatus.Completed;

        request.CompletedAt =
            DateTimeOffset.UtcNow;

        dbContext.ServiceRequestMessages.Add(
            new ServiceRequestMessage
            {
                Id =
                    Guid.NewGuid(),

                ServiceRequestId =
                    request.Id,

                Direction =
                    ServiceRequestMessageDirection.Outbound,

                Type =
                    ServiceRequestMessageType.System,

                EventType =
                    ServiceRequestEventType.Completed,

                CreatedByUserId =
                    userId,

                Body =
                    "Service request completed.",

                CreatedAt =
                    DateTimeOffset.UtcNow
            });

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    public async Task<List<ServiceRequestListItemDto>> GetAssignedRequestsAsync(
        Guid accountId,
        Guid userId,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var providerOrganizationIds =
            await dbContext.UserAccountRoles
                .AsNoTracking()
                .Where(x =>
                    x.UserAccount.UserId == userId &&
                    x.UserAccount.AccountId == accountId &&
                    x.Role == UserRole.ServiceProviderUser &&
                    x.OrganizationId.HasValue)
                .Select(x => x.OrganizationId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

        if (providerOrganizationIds.Count == 0)
        {
            return [];
        }

        var defaultLanguage =
            await dbContext.Accounts
                .AsNoTracking()
                .Where(x => x.Id == accountId)
                .Select(x => x.DefaultLanguage)
                .SingleAsync(cancellationToken);

        var requests =
            await (
                from request in dbContext.ServiceRequests.AsNoTracking()

                join definition in dbContext.ServiceDefinitions.AsNoTracking()
                    on request.ServiceDefinitionId equals definition.Id

                join organization in dbContext.Organizations.AsNoTracking()
                    on request.RequesterOrganizationId equals organization.Id

                join building in dbContext.Buildings.AsNoTracking()
                    on request.BuildingId equals building.Id

                join unitJoin in dbContext.Units.AsNoTracking()
                    on request.UnitId equals unitJoin.Id
                    into units

                from unit in units.DefaultIfEmpty()

                where
                    request.AccountId == accountId &&
                    providerOrganizationIds.Contains(
                        request.AssignedServiceProviderOrganizationId ?? Guid.Empty)

                orderby request.CreatedAt descending

                select new
                {
                    Request = request,
                    Definition = definition,
                    OrganizationName = organization.Name,
                    BuildingName = building.Name,
                    UnitName = unit != null ? unit.Name : null
                })
                .ToListAsync(cancellationToken);

        if (requests.Count == 0)
        {
            return [];
        }

        var definitionIds =
            requests
                .Select(x => x.Definition.Id)
                .Distinct()
                .ToList();

        var translations =
            await dbContext.ServiceDefinitionTranslations
                .AsNoTracking()
                .Where(x =>
                    definitionIds.Contains(x.ServiceDefinitionId))
                .ToListAsync(cancellationToken);

        return requests
            .Select(x =>
            {
                var translation =
                    TranslationHelper.Select(
                        translations.Where(t =>
                            t.ServiceDefinitionId == x.Definition.Id),
                        t => t.LanguageCode,
                        languageCode,
                        defaultLanguage);

                return new ServiceRequestListItemDto
                {
                    Id = x.Request.Id,
                    ServiceName =
                        translation?.Name ?? x.Definition.Code,
                    Category =
                        x.Definition.Category,
                    RequesterOrganizationName =
                        x.OrganizationName,
                    BuildingName =
                        x.BuildingName,
                    UnitName =
                        x.UnitName,
                    Status =
                        x.Request.Status,
                    CreatedAt =
                        x.Request.CreatedAt,
                    SubmittedAt =
                        x.Request.SubmittedAt,
                    CompletedAt =
                        x.Request.CompletedAt
                };
            })
            .ToList();
    }

    public async Task QueueProviderEmailAsync(
        Guid accountId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var request =
            await dbContext.ServiceRequests
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == requestId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (request is null ||
            !request.AssignedServiceProviderOrganizationId.HasValue)
        {
            return;
        }

        var provider =
            await dbContext.ServiceDefinitionProviders
                .AsNoTracking()
                .Where(x =>
                    x.AccountId == accountId &&
                    x.ServiceDefinitionId ==
                        request.ServiceDefinitionId &&
                    x.ServiceProviderOrganizationId ==
                        request.AssignedServiceProviderOrganizationId.Value &&
                    x.IsActive)
                .OrderByDescending(x => x.IsDefault)
                .FirstOrDefaultAsync(cancellationToken);

        if (provider is null)
        {
            return;
        }

        if (provider.IntegrationType !=
            ServiceProviderIntegrationType.Email)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
            provider.RequestEmailAddress))
        {
            return;
        }

        var alreadyQueued =
            await dbContext.EmailOutboxMessages
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ServiceRequestId == requestId &&
                        x.Status != EmailOutboxStatus.Failed,
                    cancellationToken);

        if (alreadyQueued)
        {
            return;
        }

        var email =
            await _emailComposer
                .ComposeProviderRequestAsync(
                    accountId,
                    requestId,
                    cancellationToken);

        dbContext.EmailOutboxMessages.Add(
            new EmailOutboxMessage
            {
                Id = Guid.NewGuid(),

                AccountId =
                    accountId,

                ServiceRequestId =
                    requestId,

                ToAddress =
                    provider.RequestEmailAddress,

                ReplyToAddress =
                    _emailAddressService
                        .GetReplyAddress(
                            request.ReplyToken),

                Subject =
                    email.Subject,

                Body =
                    email.Body,

                Status =
                    EmailOutboxStatus.Pending,

                AttemptCount =
                    0,

                CreatedAt =
                    DateTimeOffset.UtcNow,

                NextAttemptAt =
                    DateTimeOffset.UtcNow
            });

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    public async Task StartWorkAsync(
        Guid accountId,
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var request =
            await dbContext.ServiceRequests
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == requestId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (request is null)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestNotFound");
        }

        if (!request
            .AssignedServiceProviderOrganizationId
            .HasValue)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestNotAssigned");
        }

        var hasProviderAccess =
            await dbContext.UserAccountRoles
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.UserAccount.UserId ==
                            userId &&
                        x.UserAccount.AccountId ==
                            accountId &&
                        x.Role ==
                            UserRole.ServiceProviderUser &&
                        x.OrganizationId ==
                            request
                                .AssignedServiceProviderOrganizationId,
                    cancellationToken);

        if (!hasProviderAccess)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestActionNotAllowed");
        }

        if (request.Status !=
            ServiceRequestStatus.Approved)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestCannotStart");
        }

        request.Status =
            ServiceRequestStatus.InProgress;

        dbContext.ServiceRequestMessages.Add(
            new ServiceRequestMessage
            {
                Id =
                    Guid.NewGuid(),

                ServiceRequestId =
                    request.Id,

                Direction =
                    ServiceRequestMessageDirection.Outbound,

                Type =
                    ServiceRequestMessageType.System,

                EventType =
                    ServiceRequestEventType.InProgress,

                CreatedByUserId =
                    userId,

                Body =
                    "Work started.",

                CreatedAt =
                    DateTimeOffset.UtcNow
            });

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    public async Task AddCommentAsync(
        Guid accountId,
        Guid requestId,
        Guid userId,
        string comment,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            throw new ServiceRequestValidationException(
                "CommentRequired");
        }

        comment = comment.Trim();

        if (comment.Length > 4000)
        {
            throw new ServiceRequestValidationException(
                "CommentTooLong");
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        //
        // Bruk samme objekt-autorisasjon som GetRequestAsync.
        // Ideelt flytter vi denne senere til en felles helper.
        //

        var request =
            await dbContext.ServiceRequests
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == requestId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (request is null)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestNotFound");
        }

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

        var allowed =
            request.RequesterUserId == userId ||

            roles.Any(x =>
                x.Role == UserRole.AccountAdmin ||
                x.Role == UserRole.PropertyAdmin) ||

            roles.Any(x =>
                (x.Role == UserRole.TenantAdmin ||
                x.Role == UserRole.TenantUser) &&
                x.OrganizationId ==
                    request.RequesterOrganizationId) ||

            (
                request.AssignedServiceProviderOrganizationId
                    .HasValue &&
                roles.Any(x =>
                    x.Role ==
                        UserRole.ServiceProviderUser &&
                    x.OrganizationId ==
                        request
                            .AssignedServiceProviderOrganizationId)
            );

        if (!allowed)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestActionNotAllowed");
        }

        dbContext.ServiceRequestMessages.Add(
            new ServiceRequestMessage
            {
                Id =
                    Guid.NewGuid(),

                ServiceRequestId =
                    requestId,

                Type =
                    ServiceRequestMessageType.Comment,

                CreatedByUserId =
                    userId,

                Body =
                    comment,

                CreatedAt =
                    DateTimeOffset.UtcNow,

                Direction =
                    ServiceRequestMessageDirection.Outbound
            });

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }


    public async Task<IReadOnlyList<ServiceRequestProviderOptionDto>>
        GetAvailableProvidersAsync(
            Guid accountId,
            Guid serviceRequestId,
            CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var request =
            await dbContext.ServiceRequests
                .AsNoTracking()
                .Where(x =>
                    x.Id == serviceRequestId &&
                    x.AccountId == accountId)
                .Select(x => new
                {
                    x.ServiceDefinitionId
                })
                .SingleOrDefaultAsync(cancellationToken);

        if (request is null)
        {
            return Array.Empty<ServiceRequestProviderOptionDto>();
        }

        return await dbContext.ServiceDefinitionProviders
            .AsNoTracking()
            .Where(x =>
                x.AccountId == accountId &&
                x.ServiceDefinitionId == request.ServiceDefinitionId &&
                x.IsActive)
            .Join(
                dbContext.Organizations,
                provider => provider.ServiceProviderOrganizationId,
                organization => organization.Id,
                (provider, organization) =>
                    new ServiceRequestProviderOptionDto
                    {
                        OrganizationId = organization.Id,
                        OrganizationName = organization.Name,
                        IntegrationType = provider.IntegrationType,
                        RequestEmailAddress = provider.RequestEmailAddress
                    })
            .OrderBy(x => x.OrganizationName)
            .ToListAsync(cancellationToken);
    }

    public async Task AssignProviderAsync(
        Guid accountId,
        Guid serviceRequestId,
        Guid providerOrganizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var request =
            await dbContext.ServiceRequests
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == serviceRequestId &&
                        x.AccountId == accountId,
                    cancellationToken);

        if (request is null)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestNotFound");
        }

        var canAssign =
            await dbContext.UserAccountRoles
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.UserAccount.UserId == userId &&
                        x.UserAccount.AccountId == accountId &&
                        (
                            x.Role == UserRole.AccountAdmin ||
                            x.Role == UserRole.PropertyAdmin
                        ),
                    cancellationToken);

        if (!canAssign)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestProviderAssignmentNotAllowed");
        }

        if (request.Status != ServiceRequestStatus.Approved)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestProviderAssignmentNotAllowed");
        }

        if (request.AssignedServiceProviderOrganizationId.HasValue)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestAlreadyAssigned");
        }

        var provider =
            await dbContext.ServiceDefinitionProviders
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.AccountId == accountId &&
                        x.ServiceDefinitionId ==
                            request.ServiceDefinitionId &&
                        x.ServiceProviderOrganizationId ==
                            providerOrganizationId &&
                        x.IsActive,
                    cancellationToken);

        if (provider is null)
        {
            throw new ServiceRequestValidationException(
                "ServiceRequestProviderNotAvailable");
        }

        request.AssignedServiceProviderOrganizationId =
            providerOrganizationId;

        dbContext.ServiceRequestMessages.Add(
            new ServiceRequestMessage
            {
                Id = Guid.NewGuid(),
                ServiceRequestId = request.Id,
                Direction = ServiceRequestMessageDirection.Internal,
                Type = ServiceRequestMessageType.System,
                EventType = ServiceRequestEventType.Assigned,
                CreatedByUserId = userId,
                Body = "Service request assigned to provider.",
                CreatedAt = DateTimeOffset.UtcNow
            });

        await dbContext.SaveChangesAsync(cancellationToken);

        if (provider.IntegrationType ==
            ServiceProviderIntegrationType.Email)
        {
            await QueueProviderEmailAsync(
                accountId,
                serviceRequestId,
                cancellationToken);
        }
    }

}


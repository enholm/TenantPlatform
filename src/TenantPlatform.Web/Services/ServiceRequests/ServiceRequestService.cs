using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Services;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.ServiceRequests;

public class ServiceRequestService : IServiceRequestService
{
    private readonly IDbContextFactory<TenantPlatformDbContext>
        _dbContextFactory;

    public ServiceRequestService(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
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
}


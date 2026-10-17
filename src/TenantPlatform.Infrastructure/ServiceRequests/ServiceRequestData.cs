using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.ServiceRequests;
using TenantPlatform.Core.Services;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Localization;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.ServiceRequests;

public sealed class ServiceRequestData(IDbContextFactory<TenantPlatformDbContext> factory) : IServiceRequestData
{
    private static IQueryable<ServiceRequest> Clauses(IQueryable<ServiceRequest> source, IReadOnlyList<TenantPlatform.Application.Security.RoleAssignment> clauses, bool provider)
    {
        var result = source.Where(x => false);
        foreach (var clause in clauses)
        {
            var building = clause.BuildingId; var organization = clause.OrganizationId;
            result = result.Union(source.Where(x => (!building.HasValue || x.BuildingId == building) &&
                (!organization.HasValue || (provider ? x.AssignedServiceProviderOrganizationId == organization : x.RequesterOrganizationId == organization))));
        }
        return result;
    }
    private static IQueryable<ServiceRequest> Visible(TenantPlatformDbContext db, ServiceRequestReadScope scope, ServiceRequestListKind? kind)
    {
        var source = db.ServiceRequests.AsNoTracking().Where(x => x.AccountId == scope.AccountId);
        if (kind == ServiceRequestListKind.Mine) return source.Where(x => x.RequesterUserId == scope.UserId);
        if (kind == ServiceRequestListKind.Assigned) return Clauses(source, scope.Providers, true);
        var admin = Clauses(source, scope.Administrators, false);
        if (kind == ServiceRequestListKind.Administration) return admin;
        return admin.Union(source.Where(x => x.RequesterUserId == scope.UserId)).Union(Clauses(source, scope.Tenants, false)).Union(Clauses(source, scope.Providers, true));
    }
    public async Task<List<ServiceRequestListItemDto>> ReadListAsync(ServiceRequestReadScope scope, ServiceRequestListKind kind, string languageCode, CancellationToken cancellationToken)
    {
        var accountId = scope.AccountId;
        await using var dbContext =
            await factory.CreateDbContextAsync(
                cancellationToken);

        var defaultLanguage =
            await dbContext.Accounts
                .AsNoTracking()
                .Where(x => x.Id == accountId)
                .Select(x => x.DefaultLanguage)
                .SingleAsync(cancellationToken);

        var requests =
            await (
                from request in Visible(dbContext, scope, kind)

                join definition in dbContext.ServiceDefinitions.AsNoTracking().Where(x => x.AccountId == accountId)
                    on request.ServiceDefinitionId equals definition.Id

                join organization in dbContext.Organizations.AsNoTracking().Where(x => x.AccountId == accountId)
                    on request.RequesterOrganizationId equals organization.Id

                join building in dbContext.Buildings.AsNoTracking().Where(x => x.AccountId == accountId)
                    on request.BuildingId equals building.Id

                join unitJoin in dbContext.Units.AsNoTracking().Where(x => x.AccountId == accountId)
                    on request.UnitId equals unitJoin.Id
                    into units

                from unit in units.DefaultIfEmpty()

                where
                    request.AccountId == accountId

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

    public Task<ServiceRequestDetailsDto?> ReadDetailsAsync(ServiceRequestReadScope scope, Guid requestId, string languageCode, CancellationToken cancellationToken) => ReadDetails(scope, requestId, languageCode, false, cancellationToken);
    public Task<ServiceRequestDetailsDto?> ReadEmailDetailsAsync(ServiceRequestReadScope scope, Guid requestId, CancellationToken cancellationToken) => ReadDetails(scope, requestId, "en-GB", true, cancellationToken);
    private async Task<ServiceRequestDetailsDto?> ReadDetails(ServiceRequestReadScope scope, Guid requestId, string languageCode, bool email, CancellationToken cancellationToken)
    {
        var accountId = scope.AccountId;
        await using var dbContext =
            await factory.CreateDbContextAsync(
                cancellationToken);

        var defaultLanguage =
            await dbContext.Accounts
                .AsNoTracking()
                .Where(x => x.Id == accountId)
                .Select(x => x.DefaultLanguage)
                .SingleAsync(cancellationToken);

        var request =
            await (
                from r in Visible(dbContext, scope, null)

                join definition in dbContext.ServiceDefinitions.AsNoTracking().Where(x => x.AccountId == accountId)
                    on r.ServiceDefinitionId equals definition.Id

                join organization in dbContext.Organizations.AsNoTracking().Where(x => x.AccountId == accountId)
                    on r.RequesterOrganizationId equals organization.Id

                join building in dbContext.Buildings.AsNoTracking().Where(x => x.AccountId == accountId)
                    on r.BuildingId equals building.Id

                join unitJoin in dbContext.Units.AsNoTracking().Where(x => x.AccountId == accountId)
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
                        x.AccountId == accountId && x.Id ==
                        request.Request.AssignedServiceProviderOrganizationId.Value)
                    .Select(x => x.Name)
                    .SingleOrDefaultAsync(cancellationToken);
        }

        var translations =
            await dbContext.ServiceDefinitionTranslations
                .AsNoTracking()
                .Where(x =>
                    x.ServiceDefinitionId ==
                    request.Definition.Id)
                .ToListAsync(cancellationToken);

        var translation = email ? translations.OrderBy(x => x.LanguageCode == "en-GB" ? 0 : 1).FirstOrDefault() :
            TranslationHelper.Select(
                translations,
                x => x.LanguageCode,
                languageCode,
                defaultLanguage);

        var rawValues =
            await (
                from value in dbContext.ServiceRequestFieldValues
                    .AsNoTracking()

                join field in dbContext.ServiceDefinitionFields
                    .AsNoTracking()
                    on value.ServiceDefinitionFieldId
                    equals field.Id

                where
                    value.ServiceRequestId == requestId && field.ServiceDefinitionId == request.Definition.Id

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
                    var fieldTranslation = email ? fieldTranslations.FirstOrDefault(t => t.ServiceDefinitionFieldId == x.Field.Id && t.LanguageCode == "en-GB") :
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
                        ExternalMessageId =
                            x.ExternalMessageId,
                        ExternalThreadId =
                            x.ExternalThreadId,
                        InReplyToMessageId =
                            x.InReplyToMessageId,
                        References =
                            x.References,
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

    public async Task<List<ServiceRequestAdminListItemDto>> ReadAdminAsync(ServiceRequestReadScope scope, string languageCode, CancellationToken cancellationToken)
    {
        var accountId = scope.AccountId;
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);
        var defaultLanguage = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == accountId)
            .Select(x => x.DefaultLanguage)
            .SingleAsync(cancellationToken);

        var rawRequests = await (
            from request in Visible(dbContext, scope, ServiceRequestListKind.Administration)
            join definition in dbContext.ServiceDefinitions.AsNoTracking().Where(x => x.AccountId == accountId)
                on request.ServiceDefinitionId equals definition.Id
            join requesterRow in dbContext.Users.AsNoTracking().Where(x => dbContext.UserAccounts.Any(m => m.AccountId == accountId && m.UserId == x.Id))
                on request.RequesterUserId equals requesterRow.Id into requesters
            from requester in requesters.DefaultIfEmpty()
            join requesterOrganization in dbContext.Organizations.AsNoTracking().Where(x => x.AccountId == accountId)
                on request.RequesterOrganizationId equals requesterOrganization.Id
            join building in dbContext.Buildings.AsNoTracking().Where(x => x.AccountId == accountId)
                on request.BuildingId equals building.Id
            join unitJoin in dbContext.Units.AsNoTracking().Where(x => x.AccountId == accountId)
                on request.UnitId equals unitJoin.Id into units
            from unit in units.DefaultIfEmpty()
            join providerJoin in dbContext.Organizations.AsNoTracking().Where(x => x.AccountId == accountId)
                on request.AssignedServiceProviderOrganizationId equals providerJoin.Id
                into providers
            from provider in providers.DefaultIfEmpty()
            where
                request.AccountId == accountId &&
                definition.AccountId == accountId &&
                requesterOrganization.AccountId == accountId &&
                building.AccountId == accountId &&
                (unit == null || unit.AccountId == accountId) &&
                (provider == null || provider.AccountId == accountId)
            orderby request.CreatedAt descending
            select new
            {
                Request = request,
                DefinitionCode = definition.Code,
                RequesterName = requester == null ? "" : requester.FirstName + " " + requester.LastName,
                RequesterEmail = requester == null ? "" : requester.Email,
                RequesterOrganizationName = requesterOrganization.Name,
                BuildingName = building.Name,
                UnitName = unit != null ? unit.Name : null,
                ProviderName = provider != null ? provider.Name : null
            })
            .ToListAsync(cancellationToken);

        var definitionIds = rawRequests
            .Select(x => x.Request.ServiceDefinitionId)
            .Distinct()
            .ToList();

        var translations = await dbContext.ServiceDefinitionTranslations
            .AsNoTracking()
            .Where(x => definitionIds.Contains(x.ServiceDefinitionId))
            .ToListAsync(cancellationToken);

        var allRequests = rawRequests
            .Select(x =>
            {
                var translation = TranslationHelper.Select(
                    translations.Where(t =>
                        t.ServiceDefinitionId == x.Request.ServiceDefinitionId),
                    t => t.LanguageCode,
                    languageCode,
                    defaultLanguage);

                return new ServiceRequestAdminListItemDto
                {
                    Id = x.Request.Id,
                    ServiceDefinitionId = x.Request.ServiceDefinitionId,
                    ServiceName = translation?.Name ?? x.DefinitionCode,
                    Title = x.Request.Title,
                    RequesterName = x.RequesterName.Trim(),
                    RequesterEmail = x.RequesterEmail,
                    RequesterOrganizationName = x.RequesterOrganizationName,
                    BuildingId = x.Request.BuildingId,
                    BuildingName = x.BuildingName,
                    UnitName = x.UnitName,
                    AssignedProviderOrganizationId =
                        x.Request.AssignedServiceProviderOrganizationId,
                    AssignedProviderOrganizationName = x.ProviderName,
                    Status = x.Request.Status,
                    CreatedAt = x.Request.CreatedAt
                };
            })
            .ToList();

        return allRequests;
    }

    public async Task<ServiceRequest?> ReadRequestAsync(Guid account, Guid request, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ServiceRequests.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == request, ct);
    }
    public async Task<IReadOnlyList<ServiceRequestProviderOptionDto>> ReadProviderOptionsAsync(Guid account, Guid definition, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await (from p in db.ServiceDefinitionProviders.AsNoTracking()
            join organization in db.Organizations.AsNoTracking() on p.ServiceProviderOrganizationId equals organization.Id
            where p.AccountId == account && organization.AccountId == account && p.ServiceDefinitionId == definition && p.IsActive
            orderby organization.Name
            select new ServiceRequestProviderOptionDto { OrganizationId = organization.Id, OrganizationName = organization.Name, IntegrationType = p.IntegrationType, RequestEmailAddress = p.RequestEmailAddress }).ToListAsync(ct);
    }
    public Task<IServiceRequestTransaction> BeginCreateAsync(Guid account, Guid definition, Guid occupancy, CancellationToken ct) => Begin(account, null, definition, occupancy, ct);
    public Task<IServiceRequestTransaction> BeginRequestAsync(Guid account, Guid request, CancellationToken ct) => Begin(account, request, null, null, ct);
    private async Task<IServiceRequestTransaction> Begin(Guid account, Guid? requestId, Guid? definitionId, Guid? occupancyId, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            ServiceRequest? request = null;
            if (requestId.HasValue)
            {
                request = await db.ServiceRequests.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == requestId, ct) ?? throw new UnauthorizedAccessException();
                definitionId = request.ServiceDefinitionId;
            }
            var definition = await db.ServiceDefinitions.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == definitionId, ct);
            var occupancy = occupancyId.HasValue ? await db.Occupancies.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == occupancyId, ct) : null;
            var unit = occupancy is null ? null : await db.Units.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == occupancy.UnitId, ct);
            var fields = definition is null ? [] : await db.ServiceDefinitionFields.AsNoTracking().Where(x => x.ServiceDefinitionId == definition.Id).ToListAsync(ct);
            var providers = definition is null ? [] : await (from p in db.ServiceDefinitionProviders.AsNoTracking()
                join organization in db.Organizations.AsNoTracking() on p.ServiceProviderOrganizationId equals organization.Id
                where p.AccountId == account && organization.AccountId == account && p.ServiceDefinitionId == definition.Id select p).ToListAsync(ct);
            var messages = request is null ? [] : await db.ServiceRequestMessages.Where(x => x.ServiceRequestId == request.Id).ToListAsync(ct);
            var values = request is null ? [] : await db.ServiceRequestFieldValues.Where(x => x.ServiceRequestId == request.Id).ToListAsync(ct);
            var outbox = request is null ? [] : await db.EmailOutboxMessages.Where(x => x.AccountId == account && x.ServiceRequestId == request.Id).ToListAsync(ct);
            return new RequestTransaction(db, tx, account, new(request, definition, occupancy, unit, fields, providers, messages, values, outbox));
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed class RequestTransaction(TenantPlatformDbContext db, IDbContextTransaction tx, Guid account, ServiceRequestState state) : IServiceRequestTransaction
    {
        private readonly HashSet<Guid> messages = state.Messages.Select(x => x.Id).ToHashSet();
        private readonly HashSet<Guid> values = state.Values.Select(x => x.Id).ToHashSet();
        private readonly HashSet<Guid> outbox = state.Outbox.Select(x => x.Id).ToHashSet();
        public ServiceRequestState State => state;
        public async Task CommitAsync(ServiceRequest request, bool create, CancellationToken ct)
        {
            if (request.AccountId != account || state.Messages.Any(x => x.ServiceRequestId != request.Id) || state.Values.Any(x => x.ServiceRequestId != request.Id) ||
                state.Outbox.Any(x => x.AccountId != account || x.ServiceRequestId != request.Id)) throw new UnauthorizedAccessException();
            if (create) db.ServiceRequests.Add(request);
            db.ServiceRequestMessages.AddRange(state.Messages.Where(x => !messages.Contains(x.Id)));
            db.ServiceRequestFieldValues.AddRange(state.Values.Where(x => !values.Contains(x.Id)));
            db.EmailOutboxMessages.AddRange(state.Outbox.Where(x => !outbox.Contains(x.Id)));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}

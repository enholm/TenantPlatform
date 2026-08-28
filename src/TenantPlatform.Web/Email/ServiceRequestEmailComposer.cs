using System.Text;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Email;

public class ServiceRequestEmailComposer
    : IServiceRequestEmailComposer
{
    private readonly IDbContextFactory<TenantPlatformDbContext>
        _dbContextFactory;

    public ServiceRequestEmailComposer(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<ComposedEmail> ComposeProviderRequestAsync(
        Guid accountId,
        Guid serviceRequestId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

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
                    r.Id == serviceRequestId &&
                    r.AccountId == accountId

                select new
                {
                    Request = r,
                    Definition = definition,
                    OrganizationName = organization.Name,
                    BuildingName = building.Name,
                    UnitName = unit != null
                        ? unit.Name
                        : null
                })
                .SingleAsync(cancellationToken);

        var translation =
            await dbContext.ServiceDefinitionTranslations
                .AsNoTracking()
                .Where(x =>
                    x.ServiceDefinitionId ==
                        request.Definition.Id)
                .OrderBy(x =>
                    x.LanguageCode == "en-GB"
                        ? 0
                        : 1)
                .FirstOrDefaultAsync(cancellationToken);

        var serviceName =
            translation?.Name ??
            request.Definition.Code;

        var values =
            await (
                from value in dbContext.ServiceRequestFieldValues
                    .AsNoTracking()

                join field in dbContext.ServiceDefinitionFields
                    .AsNoTracking()
                    on value.ServiceDefinitionFieldId
                    equals field.Id

                join translationValue
                    in dbContext.ServiceDefinitionFieldTranslations
                        .AsNoTracking()
                    on field.Id
                    equals translationValue.ServiceDefinitionFieldId
                    into translations

                from fieldTranslation in translations
                    .Where(x => x.LanguageCode == "en-GB")
                    .DefaultIfEmpty()

                where
                    value.ServiceRequestId == serviceRequestId

                orderby field.SortOrder

                select new
                {
                    Label =
                        fieldTranslation != null
                            ? fieldTranslation.Label
                            : field.Key,

                    Value =
                        value.Value
                })
                .ToListAsync(cancellationToken);

        var body =
            new StringBuilder();

        body.AppendLine("A new service request has been assigned to you.");
        body.AppendLine();

        body.AppendLine($"Service: {serviceName}");
        body.AppendLine(
            $"Organisation: {request.OrganizationName}");

        body.AppendLine(
            $"Building: {request.BuildingName}");

        if (!string.IsNullOrWhiteSpace(
            request.UnitName))
        {
            body.AppendLine(
                $"Unit: {request.UnitName}");
        }

        body.AppendLine();

        body.AppendLine("Request details:");
        body.AppendLine();

        foreach (var value in values)
        {
            body.AppendLine(
                $"{value.Label}: {value.Value}");
        }

        body.AppendLine();
        body.AppendLine(
            "You can reply directly to this email. " +
            "Your reply will be added to the service request.");

        return new ComposedEmail
        {
            Subject =
                $"Service request: {serviceName}",

            Body =
                body.ToString()
        };
    }
}


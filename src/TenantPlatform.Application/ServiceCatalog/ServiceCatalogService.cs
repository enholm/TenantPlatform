using TenantPlatform.Application.Security;
using TenantPlatform.Application.ServiceDefinitions;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Localization;

namespace TenantPlatform.Application.ServiceCatalog;

public sealed record ServiceLocationScope(Guid AccountId, bool AllLocations, IReadOnlyList<RoleAssignment> TenantAssignments, DateOnly Today);
public interface IServiceCatalogLocationData
{
    Task<List<ServiceRequestLocationDto>> ReadAsync(ServiceLocationScope scope, CancellationToken ct);
}
public sealed class ServiceCatalogService(IAuthorizationService authorization, IServiceDefinitionData definitions,
    IServiceCatalogLocationData locations, TimeProvider clock) : IServiceCatalogService
{
    private async Task<AccountActorScope> Require(Guid account, CancellationToken ct)
    {
        var scope = await authorization.GetAccountActorScopeAsync(account, ct);
        if (!scope.Assignments.Any(x => LegacyRolePermissions.Grants(x, Permissions.Navigation.TenantPortal))) throw new UnauthorizedAccessException();
        return scope;
    }
    public async Task<List<ServiceCatalogItemDto>> GetAvailableServicesAsync(Guid accountId, string languageCode, CancellationToken cancellationToken = default)
    {
        await Require(accountId, cancellationToken);
        var state = await definitions.ReadBookableAsync(accountId, null, cancellationToken);
        return state.Definitions.OrderBy(x => x.Category).ThenBy(x => x.Code).Select(x =>
        {
            var t = TranslationHelper.Select(state.Translations.Where(t => t.ServiceDefinitionId == x.Id), t => t.LanguageCode, languageCode, state.DefaultLanguage);
            return new ServiceCatalogItemDto { Id = x.Id, Code = x.Code, Name = t?.Name ?? x.Code, Description = t?.Description, Category = x.Category, RequiresApproval = x.RequiresApproval, RequiresOccupancy = x.RequiresOccupancy };
        }).ToList();
    }
    public async Task<ServiceCatalogDetailsDto?> GetServiceAsync(Guid accountId, Guid serviceDefinitionId, string languageCode, CancellationToken cancellationToken = default)
    {
        await Require(accountId, cancellationToken);
        var state = await definitions.ReadBookableAsync(accountId, serviceDefinitionId, cancellationToken);
        var definition = state.Definitions.SingleOrDefault();
        if (definition is null) return null;
        var t = TranslationHelper.Select(state.Translations, x => x.LanguageCode, languageCode, state.DefaultLanguage);
        return new ServiceCatalogDetailsDto { Id = definition.Id, Code = definition.Code, Name = t?.Name ?? definition.Code, Description = t?.Description,
            Category = definition.Category, RequiresApproval = definition.RequiresApproval, RequiresOccupancy = definition.RequiresOccupancy,
            Fields = state.Fields.OrderBy(x => x.SortOrder).Select(f =>
            {
                var ft = TranslationHelper.Select(state.FieldTranslations.Where(x => x.ServiceDefinitionFieldId == f.Id), x => x.LanguageCode, languageCode, state.DefaultLanguage);
                return new ServiceDefinitionFieldDto { Id = f.Id, Key = f.Key, Label = ft?.Label ?? f.Key, Placeholder = ft?.Placeholder, HelpText = ft?.HelpText,
                    FieldType = f.FieldType, IsRequired = f.IsRequired, SortOrder = f.SortOrder, Options = f.Options };
            }).ToList() };
    }
    public async Task<List<ServiceRequestLocationDto>> GetAvailableRequestLocationsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var actor = await Require(accountId, cancellationToken);
        // Explicit legacy administrative selector. This is not request-creation authority.
        var all = actor.Assignments.Any(x => x.Role == UserRole.AccountAdmin && x.OrganizationId is null && x.BuildingId is null || x.Role == UserRole.PropertyAdmin);
        var tenant = actor.Assignments.Where(x => x.Role is UserRole.TenantAdmin or UserRole.TenantUser && x.OrganizationId.HasValue).ToArray();
        return await locations.ReadAsync(new(accountId, all, tenant, today), cancellationToken);
    }
}

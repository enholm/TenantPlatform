using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Localization;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Application.ServiceDefinitions;

public sealed class ServiceDefinitionService(IAuthorizationService authorization, IServiceDefinitionData data) : IServiceDefinitionService
{
    internal async Task Require(Guid account, string permission, Guid? definition, CancellationToken ct)
    { if (!await authorization.CanAsync(permission, new(definition.HasValue ? ResourceKind.ServiceDefinition : ResourceKind.Account, definition, account), ct)) throw new UnauthorizedAccessException(); }
    internal static T Snapshot<T>(T input) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(input))!;
    public async Task<List<ServiceDefinitionListItemDto>> GetServiceDefinitionsAsync(Guid accountId, string languageCode, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.ServiceDefinitions.Open, null, cancellationToken);
        var state = await data.ReadAsync(accountId, null, cancellationToken);
        return state.Definitions.OrderBy(x => x.Category).ThenBy(x => x.Code).Select(x =>
        {
            var t = TranslationHelper.Select(state.Translations.Where(t => t.ServiceDefinitionId == x.Id), t => t.LanguageCode, languageCode, state.DefaultLanguage);
            return new ServiceDefinitionListItemDto { Id = x.Id, Code = x.Code, Name = t?.Name ?? x.Code, Category = x.Category, HandlerType = x.HandlerType,
                RequiresApproval = x.RequiresApproval, IsBookableByTenant = x.IsBookableByTenant, RequiresOccupancy = x.RequiresOccupancy, IsActive = x.IsActive };
        }).ToList();
    }
    public async Task<ServiceDefinitionDetailsDto?> GetServiceDefinitionAsync(Guid accountId, Guid serviceDefinitionId, string languageCode, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.ServiceDefinitions.Read, serviceDefinitionId, cancellationToken);
        var state = await data.ReadAsync(accountId, serviceDefinitionId, cancellationToken);
        var definition = state.Definitions.SingleOrDefault(x => x.Id == serviceDefinitionId);
        if (definition is null) return null;
        var translations = state.Translations.Where(x => x.ServiceDefinitionId == serviceDefinitionId).OrderBy(x => x.LanguageCode).ToList();
        var t = TranslationHelper.Select(translations, x => x.LanguageCode, languageCode, state.DefaultLanguage);
        var fields = state.Fields.OrderBy(x => x.SortOrder).Select(f =>
        {
            var ft = TranslationHelper.Select(state.FieldTranslations.Where(x => x.ServiceDefinitionFieldId == f.Id), x => x.LanguageCode, languageCode, state.DefaultLanguage);
            return new ServiceDefinitionFieldDto { Id = f.Id, Key = f.Key, Label = ft?.Label ?? f.Key, Placeholder = ft?.Placeholder, HelpText = ft?.HelpText,
                FieldType = f.FieldType, IsRequired = f.IsRequired, SortOrder = f.SortOrder, Options = f.Options };
        }).ToList();
        return new ServiceDefinitionDetailsDto { Id = definition.Id, Code = definition.Code, Name = t?.Name ?? definition.Code, Description = t?.Description,
            Category = definition.Category, HandlerType = definition.HandlerType, RequiresApproval = definition.RequiresApproval, IsBookableByTenant = definition.IsBookableByTenant,
            RequiresOccupancy = definition.RequiresOccupancy, EstimatedDurationMinutes = definition.EstimatedDurationMinutes, IsActive = definition.IsActive,
            Fields = fields, Providers = Providers(state, serviceDefinitionId), Translations = translations.Select(x => new ServiceDefinitionTranslationDto { LanguageCode = x.LanguageCode, Name = x.Name, Description = x.Description }).ToList() };
    }
    public Task<Guid> CreateServiceDefinitionAsync(Guid accountId, CreateServiceDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var r = Snapshot(request);
        return Save(accountId, null, r.Code, r.Category, r.HandlerType, r.RequiresApproval, r.IsBookableByTenant, r.RequiresOccupancy, r.EstimatedDurationMinutes, r.IsActive, r.Translations, cancellationToken);
    }
    public async Task UpdateServiceDefinitionAsync(Guid accountId, Guid serviceDefinitionId, UpdateServiceDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var r = Snapshot(request);
        await Save(accountId, serviceDefinitionId, r.Code, r.Category, r.HandlerType, r.RequiresApproval, r.IsBookableByTenant, r.RequiresOccupancy, r.EstimatedDurationMinutes, r.IsActive, r.Translations, cancellationToken);
    }
    private async Task<Guid> Save(Guid account, Guid? id, string code, string? category, string handler, bool approval, bool bookable, bool occupancy, int? duration, bool active,
        List<ServiceDefinitionTranslationRequest> translations, CancellationToken ct)
    {
        var permission = id.HasValue ? Permissions.ServiceDefinitions.Update : Permissions.ServiceDefinitions.Create;
        await Require(account, permission, id, ct);
        await using var tx = await data.BeginAsync(account, ct);
        await Require(account, permission, id, ct);
        var normalized = code.Trim().ToUpperInvariant();
        if (tx.State.Definitions.Any(x => x.Code == normalized && x.Id != id)) throw new InvalidOperationException("Service definition code already exists.");
        ServiceDefinitionRules.ValidateTranslations(translations);
        var entity = id.HasValue ? tx.State.Definitions.SingleOrDefault(x => x.Id == id) ?? throw new UnauthorizedAccessException() : new ServiceDefinition { Id = Guid.NewGuid(), AccountId = account };
        entity.Code = normalized; entity.Category = ServiceDefinitionRules.NormalizeOptional(category); entity.HandlerType = string.IsNullOrWhiteSpace(handler) ? "Generic" : handler.Trim();
        entity.RequiresApproval = approval; entity.IsBookableByTenant = bookable; entity.RequiresOccupancy = occupancy; entity.EstimatedDurationMinutes = duration; entity.IsActive = active;
        if (!id.HasValue) tx.State.Definitions.Add(entity);
        foreach (var input in translations)
        {
            var language = input.LanguageCode.Trim();
            var t = tx.State.Translations.SingleOrDefault(x => x.ServiceDefinitionId == entity.Id && string.Equals(x.LanguageCode, language, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(input.Name)) { if (t is not null) tx.State.Translations.Remove(t); continue; }
            if (t is null) { t = new ServiceDefinitionTranslation { Id = Guid.NewGuid(), ServiceDefinitionId = entity.Id, LanguageCode = language }; tx.State.Translations.Add(t); }
            t.Name = input.Name.Trim(); t.Description = ServiceDefinitionRules.NormalizeOptional(input.Description);
        }
        await tx.CommitAsync(ct); return entity.Id;
    }
    public async Task<ServiceDefinitionDeleteCheckResult> CanDeleteServiceDefinitionAsync(Guid accountId, Guid serviceDefinitionId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.ServiceDefinitions.Delete, serviceDefinitionId, cancellationToken);
        return (await data.ReadUsageAsync(accountId, serviceDefinitionId, cancellationToken)).Count != 0 ? ServiceDefinitionDeleteCheckResult.NotAllowed("ServiceDefinitionHasRequests") : ServiceDefinitionDeleteCheckResult.Allowed();
    }
    public async Task DeleteServiceDefinitionAsync(Guid accountId, Guid serviceDefinitionId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.ServiceDefinitions.Delete, serviceDefinitionId, cancellationToken);
        await using var tx = await data.BeginAsync(accountId, cancellationToken);
        await Require(accountId, Permissions.ServiceDefinitions.Delete, serviceDefinitionId, cancellationToken);
        if (tx.State.Usage.Any(x => x.DefinitionId == serviceDefinitionId)) throw new ServiceDefinitionDeleteNotAllowedException("ServiceDefinitionHasRequests");
        var entity = tx.State.Definitions.SingleOrDefault(x => x.Id == serviceDefinitionId) ?? throw new UnauthorizedAccessException();
        tx.State.Translations.RemoveAll(x => x.ServiceDefinitionId == serviceDefinitionId);
        tx.State.Definitions.Remove(entity);
        await tx.CommitAsync(cancellationToken);
    }
    private static List<ServiceDefinitionProviderDto> Providers(ServiceDefinitionState state, Guid id) => state.Providers.Where(x => x.ServiceDefinitionId == id)
        .Select(x => new ServiceDefinitionProviderDto { Id = x.Id, ServiceProviderOrganizationId = x.ServiceProviderOrganizationId,
            ServiceProviderOrganizationName = state.Organizations.SingleOrDefault(o => o.Id == x.ServiceProviderOrganizationId)?.Name ?? "", IntegrationType = x.IntegrationType,
            RequestEmailAddress = x.RequestEmailAddress, IsDefault = x.IsDefault, IsActive = x.IsActive }).OrderByDescending(x => x.IsDefault).ThenBy(x => x.ServiceProviderOrganizationName).ToList();
    public async Task<IReadOnlyList<ServiceDefinitionProviderDto>> GetProvidersAsync(Guid accountId, Guid serviceDefinitionId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.ServiceDefinitions.Read, serviceDefinitionId, cancellationToken);
        return Providers(await data.ReadAsync(accountId, serviceDefinitionId, cancellationToken), serviceDefinitionId);
    }
    public async Task<ServiceDefinitionProviderEditDto?> GetProviderAsync(Guid accountId, Guid serviceDefinitionId, Guid providerId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.ServiceDefinitions.Read, serviceDefinitionId, cancellationToken);
        var state = await data.ReadAsync(accountId, serviceDefinitionId, cancellationToken);
        var p = state.Providers.SingleOrDefault(x => x.Id == providerId);
        return p is null ? null : new ServiceDefinitionProviderEditDto { ServiceProviderOrganizationId = p.ServiceProviderOrganizationId, IntegrationType = p.IntegrationType,
            RequestEmailAddress = p.RequestEmailAddress, IsDefault = p.IsDefault, IsActive = p.IsActive };
    }
    public async Task<IReadOnlyList<ServiceProviderOrganizationOptionDto>> GetProviderOrganizationOptionsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.ServiceDefinitions.Open, null, cancellationToken);
        return (await data.ReadOrganizationsAsync(accountId, cancellationToken)).Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new ServiceProviderOrganizationOptionDto { Id = x.Id, Name = x.Name }).ToList();
    }
    public Task<Guid> CreateProviderAsync(Guid accountId, Guid serviceDefinitionId, ServiceDefinitionProviderEditDto model, CancellationToken cancellationToken = default) => SaveProvider(accountId, serviceDefinitionId, null, Snapshot(model), cancellationToken);
    public async Task UpdateProviderAsync(Guid accountId, Guid serviceDefinitionId, Guid providerId, ServiceDefinitionProviderEditDto model, CancellationToken cancellationToken = default) =>
        _ = await SaveProvider(accountId, serviceDefinitionId, providerId, Snapshot(model), cancellationToken);
    private async Task<Guid> SaveProvider(Guid account, Guid definition, Guid? id, ServiceDefinitionProviderEditDto input, CancellationToken ct)
    {
        await Require(account, Permissions.ServiceDefinitions.Update, definition, ct);
        ServiceDefinitionRules.ValidateProviderModel(input);
        await using var tx = await data.BeginAsync(account, ct);
        await Require(account, Permissions.ServiceDefinitions.Update, definition, ct);
        if (!tx.State.Definitions.Any(x => x.Id == definition)) throw new UnauthorizedAccessException();
        if (!tx.State.Organizations.Any(x => x.Id == input.ServiceProviderOrganizationId)) throw new InvalidOperationException("ServiceProviderOrganizationNotFound");
        if (tx.State.Providers.Any(x => x.ServiceDefinitionId == definition && x.Id != id && x.ServiceProviderOrganizationId == input.ServiceProviderOrganizationId)) throw new InvalidOperationException("ServiceProviderAlreadyExists");
        var entity = id.HasValue ? tx.State.Providers.SingleOrDefault(x => x.Id == id && x.ServiceDefinitionId == definition) ?? throw new UnauthorizedAccessException()
            : new ServiceDefinitionProvider { Id = Guid.NewGuid(), AccountId = account, ServiceDefinitionId = definition };
        if (input.IsDefault) foreach (var p in tx.State.Providers.Where(x => x.ServiceDefinitionId == definition && x.Id != id)) p.IsDefault = false;
        entity.ServiceProviderOrganizationId = input.ServiceProviderOrganizationId; entity.IntegrationType = input.IntegrationType;
        entity.RequestEmailAddress = input.RequestEmailAddress; entity.IsDefault = input.IsDefault; entity.IsActive = input.IsActive;
        if (!id.HasValue) tx.State.Providers.Add(entity);
        await tx.CommitAsync(ct); return entity.Id;
    }
    private static int OpenRequests(IEnumerable<ServiceDefinitionUsage> usage, Guid definition, Guid provider) =>
        usage.Count(x => x.DefinitionId == definition && x.ProviderOrganizationId == provider && x.Status is ServiceRequestStatus.Approved or ServiceRequestStatus.InProgress);
    public async Task<ServiceDefinitionProviderDeleteCheckResult> CanDeleteProviderAsync(Guid accountId, Guid serviceDefinitionId, Guid providerId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.ServiceDefinitions.Update, serviceDefinitionId, cancellationToken);
        var state = await data.ReadAsync(accountId, serviceDefinitionId, cancellationToken);
        var p = state.Providers.SingleOrDefault(x => x.Id == providerId);
        var count = p is null ? 0 : OpenRequests(await data.ReadUsageAsync(accountId, serviceDefinitionId, cancellationToken), serviceDefinitionId, p.ServiceProviderOrganizationId);
        return new ServiceDefinitionProviderDeleteCheckResult { CanDelete = p is not null && count == 0, OpenRequestCount = count };
    }
    public async Task DeleteProviderAsync(Guid accountId, Guid serviceDefinitionId, Guid providerId, CancellationToken cancellationToken = default)
    {
        await Require(accountId, Permissions.ServiceDefinitions.Update, serviceDefinitionId, cancellationToken);
        await using var tx = await data.BeginAsync(accountId, cancellationToken);
        await Require(accountId, Permissions.ServiceDefinitions.Update, serviceDefinitionId, cancellationToken);
        var p = tx.State.Providers.SingleOrDefault(x => x.ServiceDefinitionId == serviceDefinitionId && x.Id == providerId);
        if (p is null) return;
        if (OpenRequests(tx.State.Usage, serviceDefinitionId, p.ServiceProviderOrganizationId) != 0) throw new InvalidOperationException("ServiceProviderHasOpenRequests");
        tx.State.Providers.Remove(p); await tx.CommitAsync(cancellationToken);
    }
}

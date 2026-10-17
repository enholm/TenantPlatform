using TenantPlatform.Application.Security;
using TenantPlatform.Application.ServiceDefinitions;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Application.ServiceDefinitionFields;

public sealed class ServiceDefinitionFieldService(ServiceDefinitionService definitions, IServiceDefinitionData data) : IServiceDefinitionFieldService
{
    public async Task<ServiceDefinitionFieldDetailsDto?> GetFieldAsync(Guid accountId, Guid serviceDefinitionId, Guid fieldId, CancellationToken cancellationToken = default)
    {
        await definitions.Require(accountId, Permissions.ServiceDefinitions.Read, serviceDefinitionId, cancellationToken);
        var state = await data.ReadAsync(accountId, serviceDefinitionId, cancellationToken);
        var field = state.Fields.SingleOrDefault(x => x.Id == fieldId && x.ServiceDefinitionId == serviceDefinitionId);
        return field is null ? null : new ServiceDefinitionFieldDetailsDto { Id = field.Id, ServiceDefinitionId = field.ServiceDefinitionId, Key = field.Key,
            FieldType = field.FieldType, IsRequired = field.IsRequired, SortOrder = field.SortOrder, OptionsText = ServiceDefinitionFieldRules.DeserializeOptions(field.Options),
            Translations = state.FieldTranslations.Where(x => x.ServiceDefinitionFieldId == fieldId).OrderBy(x => x.LanguageCode).Select(x => new ServiceDefinitionFieldTranslationDto
            { LanguageCode = x.LanguageCode, Label = x.Label, Placeholder = x.Placeholder, HelpText = x.HelpText }).ToList() };
    }
    public Task<Guid> CreateFieldAsync(Guid accountId, Guid serviceDefinitionId, CreateServiceDefinitionFieldRequest request, CancellationToken cancellationToken = default)
    {
        var r = ServiceDefinitionService.Snapshot(request);
        return Save(accountId, serviceDefinitionId, null, r.Key, r.FieldType, r.IsRequired, r.SortOrder, r.OptionsText, r.Translations, cancellationToken);
    }
    public async Task UpdateFieldAsync(Guid accountId, Guid serviceDefinitionId, Guid fieldId, UpdateServiceDefinitionFieldRequest request, CancellationToken cancellationToken = default)
    {
        var r = ServiceDefinitionService.Snapshot(request);
        await Save(accountId, serviceDefinitionId, fieldId, r.Key, r.FieldType, r.IsRequired, r.SortOrder, r.OptionsText, r.Translations, cancellationToken);
    }
    private async Task<Guid> Save(Guid account, Guid definition, Guid? id, string key, ServiceFieldType type, bool required, int sortOrder, string? options,
        List<ServiceDefinitionFieldTranslationRequest> translations, CancellationToken ct)
    {
        await definitions.Require(account, Permissions.ServiceDefinitions.Update, definition, ct);
        await using var tx = await data.BeginAsync(account, ct);
        await definitions.Require(account, Permissions.ServiceDefinitions.Update, definition, ct);
        if (!tx.State.Definitions.Any(x => x.Id == definition)) throw new UnauthorizedAccessException();
        var normalized = ServiceDefinitionFieldRules.NormalizeKey(key);
        if (tx.State.Fields.Any(x => x.ServiceDefinitionId == definition && x.Key == normalized && x.Id != id)) throw new InvalidOperationException("Service definition field key already exists.");
        ServiceDefinitionFieldRules.ValidateTranslations(translations);
        var field = id.HasValue ? tx.State.Fields.SingleOrDefault(x => x.ServiceDefinitionId == definition && x.Id == id) ?? throw new UnauthorizedAccessException()
            : new ServiceDefinitionField { Id = Guid.NewGuid(), ServiceDefinitionId = definition };
        field.Key = normalized; field.FieldType = type; field.IsRequired = required; field.SortOrder = sortOrder;
        field.Options = ServiceDefinitionFieldRules.SerializeOptions(type, options);
        if (!id.HasValue) tx.State.Fields.Add(field);
        foreach (var input in translations)
        {
            var language = input.LanguageCode.Trim();
            var t = tx.State.FieldTranslations.SingleOrDefault(x => x.ServiceDefinitionFieldId == field.Id && string.Equals(x.LanguageCode, language, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(input.Label)) { if (t is not null) tx.State.FieldTranslations.Remove(t); continue; }
            if (t is null) { t = ServiceDefinitionFieldRules.CreateTranslation(field.Id, language, input.Label, input.Placeholder, input.HelpText); tx.State.FieldTranslations.Add(t); }
            else { t.Label = input.Label.Trim(); t.Placeholder = ServiceDefinitionFieldRules.NormalizeOptional(input.Placeholder); t.HelpText = ServiceDefinitionFieldRules.NormalizeOptional(input.HelpText); }
        }
        await tx.CommitAsync(ct); return field.Id;
    }
    public async Task DeleteFieldAsync(Guid accountId, Guid serviceDefinitionId, Guid fieldId, CancellationToken cancellationToken = default)
    {
        await definitions.Require(accountId, Permissions.ServiceDefinitions.Update, serviceDefinitionId, cancellationToken);
        await using var tx = await data.BeginAsync(accountId, cancellationToken);
        await definitions.Require(accountId, Permissions.ServiceDefinitions.Update, serviceDefinitionId, cancellationToken);
        var field = tx.State.Fields.SingleOrDefault(x => x.ServiceDefinitionId == serviceDefinitionId && x.Id == fieldId) ?? throw new UnauthorizedAccessException();
        if (tx.State.UsedFieldIds.Contains(fieldId)) throw new InvalidOperationException("The field cannot be deleted because it has been used in service requests.");
        tx.State.FieldTranslations.RemoveAll(x => x.ServiceDefinitionFieldId == fieldId); tx.State.Fields.Remove(field);
        await tx.CommitAsync(cancellationToken);
    }
}

using TenantPlatform.Core.Services;

namespace TenantPlatform.Web.Services.ServiceDefinitionFields;

public class CreateServiceDefinitionFieldRequest
{
    public string Key { get; init; } = string.Empty;

    public ServiceFieldType FieldType { get; init; }

    public bool IsRequired { get; init; }

    public int SortOrder { get; init; }

    public string? OptionsText { get; init; }

    public List<ServiceDefinitionFieldTranslationRequest> Translations { get; init; } = [];
}


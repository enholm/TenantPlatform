using TenantPlatform.Core.Services;

namespace TenantPlatform.Web.Services.ServiceDefinitionFields;

public class UpdateServiceDefinitionFieldRequest
{
    public string Key { get; init; } = string.Empty;

    public ServiceFieldType FieldType { get; init; }

    public bool IsRequired { get; init; }

    public int SortOrder { get; init; }

    public string? OptionsText { get; init; }

    public List<ServiceDefinitionFieldTranslationRequest> Translations { get; init; } = [];
}

public class ServiceDefinitionFieldTranslationRequest
{
    public string LanguageCode { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string? Placeholder { get; init; }

    public string? HelpText { get; init; }
}


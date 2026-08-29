using TenantPlatform.Core.Services;

namespace TenantPlatform.Web.Services.ServiceDefinitionFields;

public class ServiceDefinitionFieldDetailsDto
{
    public Guid Id { get; init; }

    public Guid ServiceDefinitionId { get; init; }

    public string Key { get; init; } = string.Empty;

    public ServiceFieldType FieldType { get; init; }

    public bool IsRequired { get; init; }

    public int SortOrder { get; init; }

    public string? OptionsText { get; init; }

    public List<ServiceDefinitionFieldTranslationDto> Translations { get; init; } = [];
}

public class ServiceDefinitionFieldTranslationDto
{
    public string LanguageCode { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string? Placeholder { get; init; }

    public string? HelpText { get; init; }
}


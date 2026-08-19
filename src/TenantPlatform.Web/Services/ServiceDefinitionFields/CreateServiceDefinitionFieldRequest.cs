using TenantPlatform.Core.Services;

namespace TenantPlatform.Web.Services.ServiceDefinitionFields;

public class CreateServiceDefinitionFieldRequest
{
    public string Key { get; init; } = string.Empty;

    public ServiceFieldType FieldType { get; init; }

    public bool IsRequired { get; init; }

    public int SortOrder { get; init; }

    public string? OptionsText { get; init; }

    public string NorwegianLabel { get; init; } = string.Empty;

    public string? NorwegianPlaceholder { get; init; }

    public string? NorwegianHelpText { get; init; }

    public string EnglishLabel { get; init; } = string.Empty;

    public string? EnglishPlaceholder { get; init; }

    public string? EnglishHelpText { get; init; }
}


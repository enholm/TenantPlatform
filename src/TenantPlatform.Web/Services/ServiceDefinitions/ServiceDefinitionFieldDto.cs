using TenantPlatform.Core.Services;

namespace TenantPlatform.Web.Services.ServiceDefinitions;

public class ServiceDefinitionFieldDto
{
    public Guid Id { get; init; }

    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string? HelpText { get; init; }

    public ServiceFieldType FieldType { get; init; }

    public bool IsRequired { get; init; }

    public int SortOrder { get; init; }

    public string? OptionsJson { get; init; }
}


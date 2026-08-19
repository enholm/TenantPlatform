namespace TenantPlatform.Core.Services;

public class ServiceDefinitionFieldTranslation
{
    public Guid Id { get; set; }

    public Guid ServiceDefinitionFieldId { get; set; }

    public string LanguageCode { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string? HelpText { get; set; }
}


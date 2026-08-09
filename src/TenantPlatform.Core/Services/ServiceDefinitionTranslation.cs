namespace TenantPlatform.Core.Services;

public class ServiceDefinitionTranslation
{
    public Guid Id { get; set; }

    public Guid ServiceDefinitionId { get; set; }

    public string LanguageCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}

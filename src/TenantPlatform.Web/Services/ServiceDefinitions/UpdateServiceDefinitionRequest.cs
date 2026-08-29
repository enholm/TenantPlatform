namespace TenantPlatform.Web.Services.ServiceDefinitions;

public class UpdateServiceDefinitionRequest
{
    public string Code { get; init; } = string.Empty;

    public string? Category { get; init; }

    public string HandlerType { get; init; } = "Generic";

    public bool RequiresApproval { get; init; }

    public bool IsBookableByTenant { get; init; }

    public bool RequiresOccupancy { get; init; }

    public int? EstimatedDurationMinutes { get; init; }

    public bool IsActive { get; init; }

    public List<ServiceDefinitionTranslationRequest> Translations { get; init; } = [];
}

public class ServiceDefinitionTranslationRequest
{
    public string LanguageCode { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }
}


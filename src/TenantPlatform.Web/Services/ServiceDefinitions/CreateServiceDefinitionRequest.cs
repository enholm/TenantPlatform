namespace TenantPlatform.Web.Services.ServiceDefinitions;

public class CreateServiceDefinitionRequest
{
    public string Code { get; init; } = string.Empty;

    public string? Category { get; init; }

    public string HandlerType { get; init; } = "Generic";

    public bool RequiresApproval { get; init; }

    public bool IsBookableByTenant { get; init; } = true;

    public int? EstimatedDurationMinutes { get; init; }

    public bool IsActive { get; init; } = true;

    public string NorwegianName { get; init; } = string.Empty;

    public string? NorwegianDescription { get; init; }

    public string EnglishName { get; init; } = string.Empty;

    public string? EnglishDescription { get; init; }
}


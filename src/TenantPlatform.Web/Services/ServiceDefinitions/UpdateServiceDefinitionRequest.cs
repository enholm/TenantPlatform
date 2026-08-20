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

    public string NorwegianName { get; init; } = string.Empty;

    public string? NorwegianDescription { get; init; }

    public string EnglishName { get; init; } = string.Empty;

    public string? EnglishDescription { get; init; }
}


namespace TenantPlatform.Core.Services;

public class ServiceDefinition
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string? Category { get; set; }

    public string HandlerType { get; set; } = "Generic";

    public bool RequiresApproval { get; set; }

    public bool IsBookableByTenant { get; set; } = true;

    public bool RequiresOccupancy { get; set; } = true;
    
    public int? EstimatedDurationMinutes { get; set; }

    public bool IsActive { get; set; } = true;
}


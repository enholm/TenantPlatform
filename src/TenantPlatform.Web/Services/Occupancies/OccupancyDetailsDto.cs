namespace TenantPlatform.Web.Services.Occupancies;

public class OccupancyDetailsDto
{
    public Guid Id { get; init; }

    public Guid TenantOrganizationId { get; init; }

    public string TenantOrganizationName { get; init; } = string.Empty;

    public Guid UnitId { get; init; }

    public string UnitName { get; init; } = string.Empty;

    public Guid BuildingId { get; init; }

    public string BuildingName { get; init; } = string.Empty;

    public DateOnly ValidFrom { get; init; }

    public DateOnly? ValidTo { get; init; }
}


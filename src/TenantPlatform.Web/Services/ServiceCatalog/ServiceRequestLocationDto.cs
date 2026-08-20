namespace TenantPlatform.Web.Services.ServiceCatalog;

public class ServiceRequestLocationDto
{
    public Guid OccupancyId { get; init; }

    public Guid OrganizationId { get; init; }

    public string OrganizationName { get; init; } = string.Empty;

    public Guid BuildingId { get; init; }

    public string BuildingName { get; init; } = string.Empty;

    public Guid UnitId { get; init; }

    public string UnitName { get; init; } = string.Empty;

    public string DisplayName =>
        $"{OrganizationName} — {BuildingName} — {UnitName}";
}


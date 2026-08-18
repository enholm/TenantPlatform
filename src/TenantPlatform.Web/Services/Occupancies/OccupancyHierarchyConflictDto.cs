namespace TenantPlatform.Web.Services.Occupancies;

public class OccupancyHierarchyConflictDto
{
    public Guid OccupancyId { get; init; }

    public Guid UnitId { get; init; }

    public string UnitName { get; init; } = string.Empty;

    public string TenantOrganizationName { get; init; } = string.Empty;

    public DateOnly ValidFrom { get; init; }

    public DateOnly? ValidTo { get; init; }

    public OccupancyHierarchyConflictType ConflictType { get; init; }
}


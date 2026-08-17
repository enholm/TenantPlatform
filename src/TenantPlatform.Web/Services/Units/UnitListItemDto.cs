using TenantPlatform.Core.Properties;

namespace TenantPlatform.Web.Services.Units;

public class UnitListItemDto
{
    public Guid Id { get; init; }

    public Guid BuildingId { get; init; }

    public string BuildingName { get; init; } = string.Empty;

    public Guid? ParentUnitId { get; init; }

    public string? ParentUnitName { get; init; }

    public string Name { get; init; } = string.Empty;

    public UnitType Type { get; init; }

    public bool IsActive { get; init; }
}


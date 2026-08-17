using TenantPlatform.Core.Properties;

namespace TenantPlatform.Web.Services.Units;

public class UpdateUnitRequest
{
    public Guid BuildingId { get; init; }

    public Guid? ParentUnitId { get; init; }

    public string Name { get; init; } = string.Empty;

    public UnitType Type { get; init; }

    public bool IsActive { get; init; }
}


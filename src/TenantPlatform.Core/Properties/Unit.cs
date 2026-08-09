namespace TenantPlatform.Core.Properties;

public class Unit
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid BuildingId { get; set; }

    public Guid? ParentUnitId { get; set; }

    public string Name { get; set; } = string.Empty;

    public UnitType Type { get; set; }

    public bool IsActive { get; set; } = true;
}

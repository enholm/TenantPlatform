namespace TenantPlatform.Core.Dimensions;

public sealed class Dimension
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowMultiple { get; set; }
    public bool LeafOnly { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid Revision { get; set; }
}
public sealed class DimensionValue
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid DimensionId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public Guid Revision { get; set; }
}
public sealed class DimensionHistory
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid DimensionId { get; set; }
    public Guid? ValueId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
}

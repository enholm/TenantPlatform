namespace TenantPlatform.Core.Leasing;

// Module-specific rules deliberately live outside the shared dimension register.
public sealed class LeasingDimensionRule
{
    public Guid AccountId { get; set; }
    public Guid DimensionId { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsRequired { get; set; }
    public bool AllowAllocation { get; set; }
    public Guid Revision { get; set; }
}
public enum LeasingAllocationMode { None = 0, Percent = 1, NetAmount = 2, Quantity = 3 }
public sealed class LeasingAllocationDimension
{
    public Guid AccountId { get; set; }
    public Guid ItemId { get; set; }
    public Guid DimensionId { get; set; }
    public string DimensionName { get; set; } = "";
    public string DimensionCode { get; set; } = "";
}
public sealed class LeasingAllocationRow
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid ItemId { get; set; }
    public int Position { get; set; }
    // Only the chosen mode's input is stored; money, VAT, percentages and quantities are derived.
    public decimal InputValue { get; set; }
}
public sealed class LeasingDimensionSelection
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid ItemId { get; set; }
    public Guid DimensionId { get; set; }
    public Guid ValueId { get; set; }
    // Null means common classification. Otherwise the value applies only to this allocation row.
    public Guid? AllocationRowId { get; set; }
    public string DimensionName { get; set; } = "";
    public string DimensionCode { get; set; } = "";
    public string ValueName { get; set; } = "";
    public string ValueCode { get; set; } = "";
    public string ValuePath { get; set; } = "";
}

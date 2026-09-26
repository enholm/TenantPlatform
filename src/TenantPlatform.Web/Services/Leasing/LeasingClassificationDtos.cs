using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Leasing;

public sealed record LeasingDimensionOption(Dimension Dimension, LeasingDimensionRule Rule, List<DimensionValueOption> Values);
public sealed record LeasingClassificationCatalog(List<LeasingDimensionOption> Dimensions, bool CanManage);
public sealed record LeasingDimensionChoice(Guid DimensionId, Guid ValueId);
public sealed class LeasingAllocationInput
{
    public Guid? Id { get; set; }
    public decimal InputValue { get; set; }
    public List<LeasingDimensionChoice> Choices { get; set; } = [];
}
public sealed class LeasingClassificationInput
{
    public LeasingAllocationMode Mode { get; set; }
    public List<Guid> VaryingDimensions { get; set; } = [];
    public List<LeasingDimensionChoice> Common { get; set; } = [];
    public List<LeasingAllocationInput> Rows { get; set; } = [];
    public static LeasingClassificationInput From(LeasingItem item) => new()
    {
        Mode = item.AllocationMode, VaryingDimensions = item.AllocationDimensions.Select(x => x.DimensionId).ToList(),
        Common = item.DimensionSelections.Where(x => x.AllocationRowId == null).Select(x => new LeasingDimensionChoice(x.DimensionId, x.ValueId)).ToList(),
        Rows = item.AllocationRows.OrderBy(x => x.Position).Select(x => new LeasingAllocationInput { Id = x.Id, InputValue = x.InputValue,
            Choices = item.DimensionSelections.Where(v => v.AllocationRowId == x.Id).Select(v => new LeasingDimensionChoice(v.DimensionId, v.ValueId)).ToList() }).ToList()
    };
    public LeasingClassificationInput Copy() => new()
    {
        Mode = Mode, Common = [..Common], VaryingDimensions = [..VaryingDimensions],
        Rows = Rows.Select(x => new LeasingAllocationInput { InputValue = x.InputValue, Choices = [..x.Choices] }).ToList()
    };
    public LeasingItem Preview(LeasingItem source) => new()
    {
        Quantity = source.Quantity, UnitPrice = source.UnitPrice, VatPercent = source.VatPercent, AllocationMode = Mode,
        AllocationRows = Rows.Select((x, i) => new LeasingAllocationRow { Id = x.Id ?? Guid.Empty, Position = i, InputValue = x.InputValue }).ToList()
    };
}

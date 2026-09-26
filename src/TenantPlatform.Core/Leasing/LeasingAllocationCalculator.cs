namespace TenantPlatform.Core.Leasing;

public sealed record LeasingAllocatedAmount(Guid? RowId, decimal? Percent, decimal? Quantity, decimal Net, decimal Vat)
{
    public decimal Gross => Net + Vat;
}
public sealed record LeasingAllocationResult(bool Complete, decimal Target, decimal Assigned, IReadOnlyList<LeasingAllocatedAmount> Rows)
{
    public decimal Remaining => Target - Assigned;
}
public static class LeasingAllocationCalculator
{
    public static LeasingAllocationResult Calculate(LeasingItem item)
    {
        var total = LeasingCalculator.Line(item);
        if (item.AllocationMode == LeasingAllocationMode.None)
            return new(item.AllocationRows.Count == 0, 100, 100, [new(null, 100, item.Quantity, total.Net, total.Vat)]);
        if (!Enum.IsDefined(item.AllocationMode)) throw new ArgumentOutOfRangeException(nameof(item));
        var rows = item.AllocationRows.OrderBy(x => x.Position).ThenBy(x => x.Id).ToList();
        var target = item.AllocationMode switch { LeasingAllocationMode.Percent => 100, LeasingAllocationMode.NetAmount => total.Net, _ => item.Quantity };
        if (rows.Any(x => x.InputValue < 0 || x.InputValue > 1000000000000m)) throw new ArgumentOutOfRangeException(nameof(item));
        var assigned = rows.Sum(x => x.InputValue);
        var complete = rows.Count > 0 && assigned == target;
        // Incomplete input is displayed as such; never silently scale explicit money or quantities.
        if (!complete) return new(false, target, assigned, []);
        decimal cumulative = 0, previousNet = 0, previousVat = 0, previousQuantity = 0;
        var result = new List<LeasingAllocatedAmount>();
        foreach (var row in rows)
        {
            cumulative += row.InputValue;
            // A zero-net amount split has zero money/VAT and undefined derived percentages/quantities.
            var weight = target == 0 ? 0 : cumulative / target;
            var net = item.AllocationMode == LeasingAllocationMode.NetAmount ? cumulative : Money(total.Net * weight);
            var vat = Money(total.Vat * weight);
            var quantity = item.AllocationMode == LeasingAllocationMode.Quantity ? cumulative : decimal.Round(item.Quantity * weight, 4, MidpointRounding.AwayFromZero);
            result.Add(new(row.Id, target == 0 ? null : row.InputValue / target * 100,
                target == 0 ? null : quantity - previousQuantity, net - previousNet, vat - previousVat));
            previousNet = net; previousVat = vat; previousQuantity = quantity;
        }
        return new(true, target, assigned, result);
    }
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

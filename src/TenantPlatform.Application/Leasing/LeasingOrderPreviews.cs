using System.Text.Json;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public static class LeasingOrderPreviews
{
    private static string Snapshot<T>(T entity) => JsonSerializer.Serialize(entity);
    public static LeasingClassificationInput DeliveryClassification(LeasingOrderLine line, LeasingItem item)
    {
        var value = (JsonSerializer.Deserialize<LeasingClassificationInput>(line.ClassificationJson) ?? new()).Copy();
        if (value.Mode is LeasingAllocationMode.Quantity or LeasingAllocationMode.NetAmount && value.Rows.Count > 0)
        {
            var total = value.Rows.Sum(x => x.InputValue);
            var target = value.Mode == LeasingAllocationMode.Quantity ? item.Quantity : LeasingCalculator.Line(item).Net;
            if (total > 0)
            {
                decimal allocated = 0;
                for (int i = 0; i < value.Rows.Count; i++) { var row = value.Rows[i]; row.InputValue = i == value.Rows.Count - 1 ? target - allocated : decimal.Round(target * row.InputValue / total, value.Mode == LeasingAllocationMode.Quantity ? 4 : 2, MidpointRounding.AwayFromZero); allocated += row.InputValue; }
            }
        }
        return value;
    }
    public static LeasingItem OrderClassificationPreview(LeasingOrderLine line, LeasingClassificationCatalog catalog)
    {
        var input = JsonSerializer.Deserialize<LeasingClassificationInput>(line.ClassificationJson) ?? new(); var item = line.Item(); item.AllocationMode = input.Mode;
        item.AllocationDimensions = input.VaryingDimensions.Select(id => new LeasingAllocationDimension { DimensionId = id, DimensionName = catalog.Dimensions.FirstOrDefault(x => x.Dimension.Id == id)?.Dimension.Name ?? "" }).ToList();
        void Choices(IEnumerable<LeasingDimensionChoice> choices, Guid? row)
        {
            foreach (var choice in choices)
            {
                var dimension = catalog.Dimensions.FirstOrDefault(x => x.Dimension.Id == choice.DimensionId); var value = dimension?.Values.FirstOrDefault(x => x.Id == choice.ValueId);
                item.DimensionSelections.Add(new() { DimensionId = choice.DimensionId, ValueId = choice.ValueId, AllocationRowId = row, DimensionName = dimension?.Dimension.Name ?? "", ValueName = value?.Name ?? "", ValuePath = value?.Path ?? "" });
            }
        }
        Choices(input.Common, null);
        for (int i = 0; i < input.Rows.Count; i++) { var row = input.Rows[i]; var id = Guid.NewGuid(); item.AllocationRows.Add(new() { Id = id, Position = i, InputValue = row.InputValue }); Choices(row.Choices, id); }
        return item;
    }
    public static string FulfillmentKey(LeasingOrder o) => o.Lines.All(x => x.Fulfilled == 0) ? "OrderNotRealized" : o.Lines.All(x => x.Fulfilled == x.Scope) ? "OrderFullyRealized" : "OrderPartRealized";
    public static decimal ApprovedOrderValue(OrderDetails d)
    {
        if (d.Order.ApprovedUtc == null) return 0;
        var pending = JsonSerializer.Deserialize<LeasingOrder>(Snapshot(d.Order))!;
        // Reversed history is retained, but must not count twice after an explicit reopening.
        // Unreserved scope is still part of the approved order value, not active capacity usage.
        foreach (var line in pending.Lines) line.Unreserved = 0;
        return d.Realizations.Where(x => !x.Reversed).Sum(x => x.ApprovedNet + (d.Framework.IncludesVat ? x.ApprovedVat : 0)) +
            pending.Lines.Sum(x => d.Framework.IncludesVat ? LeasingOrderCalculator.Remaining(x).Gross : LeasingOrderCalculator.Remaining(x).Net);
    }
    public static decimal ProposedReservation(LeasingOrder current, LeasingOrder proposed, bool vat)
    {
        var copy = JsonSerializer.Deserialize<LeasingOrder>(Snapshot(proposed))!; copy.Status = LeasingOrderStatus.Approved;
        foreach (var line in copy.Lines) { var old = current.Lines.SingleOrDefault(x => x.Id == line.Id); line.Fulfilled = old?.Fulfilled ?? 0; line.Unreserved = old?.Unreserved ?? 0; }
        return LeasingOrderCalculator.Reserved(copy, vat);
    }
    public static decimal PreviewOrderRelease(LeasingOrder order, OrderDelivery delivery, bool vat)
    {
        var copy = JsonSerializer.Deserialize<LeasingOrder>(Snapshot(order))!;
        var before = LeasingOrderCalculator.Reserved(copy, vat);
        foreach (var part in delivery.Lines.Where(x => x.OrderLineId != Guid.Empty))
        {
            var line = copy.Lines.Single(x => x.Id == part.OrderLineId);
            if (part.Scope <= 0 || part.Scope + line.Fulfilled + line.Unreserved > line.Scope) throw new LeasingValidationException("LeasingOrderOverDelivery");
            line.Fulfilled += part.Scope;
        }
        return before - LeasingOrderCalculator.Reserved(copy, vat);
    }
}

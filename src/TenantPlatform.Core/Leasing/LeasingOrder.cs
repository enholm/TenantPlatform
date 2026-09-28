namespace TenantPlatform.Core.Leasing;

public enum LeasingOrderStatus { Draft = 1, Pending = 2, Approved = 3, Rejected = 4, Cancelled = 5, Closed = 6 }
public enum LeasingOrderMethod { Quantity = 1, AmountShare = 2 }

public sealed class LeasingOrder
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid FrameworkId { get; set; }
    public string Number { get; set; } = "";
    public Guid SupplierOrganizationId { get; set; }
    public Guid OwnerUserId { get; set; }
    public DateOnly OrderDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public string Currency { get; set; } = "NOK";
    public string? Notes { get; set; }
    public LeasingOrderStatus Status { get; set; }
    public Guid Revision { get; set; }
    public string? ProposalJson { get; set; }
    public Guid? ProposedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public List<LeasingOrderLine> Lines { get; set; } = [];
}
public sealed class LeasingOrderLine
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid OrderId { get; set; }
    public int Position { get; set; }
    public string Description { get; set; } = "";
    public string? ItemNumber { get; set; }
    public string Unit { get; set; } = "stk";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal VatPercent { get; set; } = 25;
    public LeasingOrderMethod Method { get; set; } = LeasingOrderMethod.Quantity;
    public string ClassificationJson { get; set; } = "{}";
    public decimal Fulfilled { get; set; }
    // Reversed portions remain consumed until an explicit, approved reopening.
    public decimal Unreserved { get; set; }
    public decimal Scope => Method == LeasingOrderMethod.Quantity ? Quantity : 1m;
    public LeasingItem Item() => new() { Description = Description, ItemNumber = ItemNumber, Quantity = Quantity, UnitPrice = UnitPrice, VatPercent = VatPercent };
}
public sealed class LeasingOrderRealization
{
    public Guid RequestId { get; set; }
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public Guid AcquisitionId { get; set; }
    public Guid ItemId { get; set; }
    public decimal Scope { get; set; }
    public decimal ApprovedNet { get; set; }
    public decimal ApprovedVat { get; set; }
    public decimal ActualNet { get; set; }
    public decimal ActualVat { get; set; }
    public bool Reversed { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
}
public sealed class LeasingOrderEvent
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
    public DateTimeOffset RecordedUtc { get; set; }
}
public sealed class LeasingLimitChange
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid FrameworkId { get; set; }
    public decimal PreviousLimit { get; set; }
    public decimal NewLimit { get; set; }
    public string Reason { get; set; } = "";
    public string? DocumentReference { get; set; }
    public Guid ProposedByUserId { get; set; }
    public DateTimeOffset ProposedUtc { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
}
public static class LeasingOrderCalculator
{
    public static LeasingTotals Remaining(LeasingOrderLine line)
    {
        var total = LeasingCalculator.Line(line.Item());
        var consumed = line.Fulfilled + line.Unreserved;
        if (consumed < 0 || consumed > line.Scope) throw new ArgumentOutOfRangeException(nameof(line));
        // Cumulative rounding ensures the last delivery consumes every reserved cent.
        return new(total.Net - Money(total.Net * consumed / line.Scope), total.Vat - Money(total.Vat * consumed / line.Scope));
    }
    public static decimal Reserved(LeasingOrder order, bool vat) => order.Status != LeasingOrderStatus.Approved ? 0 :
        order.Lines.Sum(x => vat ? Remaining(x).Gross : Remaining(x).Net);
    public static decimal Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}

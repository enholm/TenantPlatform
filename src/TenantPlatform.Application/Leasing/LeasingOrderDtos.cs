using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed record LeasingCapacity(decimal Limit, decimal Used, decimal Reserved)
{
    public decimal Available => Limit - Used - Reserved;
}
public sealed class OrderDelivery
{
    public Guid OrderId { get; set; }
    public Guid Revision { get; set; }
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public bool VarianceConfirmed { get; set; }
    public List<OrderDeliveryLine> Lines { get; set; } = [];
}
public sealed class OrderDeliveryLine
{
    public Guid OrderLineId { get; set; }
    public int ItemPosition { get; set; }
    public decimal Scope { get; set; }
}
public sealed record OrderDetails(LeasingOrder Order, LeasingFramework Framework, LeasingCapacity Capacity,
    List<LeasingOrderRealization> Realizations, List<LeasingOrderEvent> History, List<LeasingInvoice> Invoices,
    List<LeasingDocument> Documents, bool CanApprove, bool NeedsReview, decimal DocumentedNet = 0, bool CanEdit = false);


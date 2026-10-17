using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    public Task<LeasingAcquisition> NewOrderAcquisitionAsync(Guid account, Guid orderId, CancellationToken ct = default) => orderReader.NewOrderAcquisitionAsync(account, orderId, ct);
    public Task<List<LeasingOrder>> AcquisitionOrdersAsync(Guid account, Guid acquisition, CancellationToken ct = default) => orderReader.AcquisitionOrdersAsync(account, acquisition, ct);
    public Task<List<LeasingAcquisition>> OrderLinkCandidatesAsync(Guid account, Guid order, CancellationToken ct = default) => orderReader.OrderLinkCandidatesAsync(account, order, ct);
    public static LeasingItem OrderClassificationPreview(LeasingOrderLine line, LeasingClassificationCatalog catalog) => LeasingOrderPreviews.OrderClassificationPreview(line, catalog);
    public static string FulfillmentKey(LeasingOrder order) => LeasingOrderPreviews.FulfillmentKey(order);
    public static decimal ApprovedOrderValue(OrderDetails details) => LeasingOrderPreviews.ApprovedOrderValue(details);
    public static decimal ProposedReservation(LeasingOrder current, LeasingOrder proposed, bool vat) => LeasingOrderPreviews.ProposedReservation(current, proposed, vat);
    public static decimal PreviewOrderRelease(LeasingOrder order, OrderDelivery delivery, bool vat) => LeasingOrderPreviews.PreviewOrderRelease(order, delivery, vat);
}

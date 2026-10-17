using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Leasing.Invoices;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Web.Services.Agreements;

namespace TenantPlatform.Web.Services.Leasing;

public sealed partial class LeasingService
{
    public Task<bool> CanChangeLimitAsync(Guid account, CancellationToken ct = default) => orderReader.CanChangeLimitAsync(account, ct);
    public Task<List<LeasingOrder>> ListOrdersAsync(Guid account, Guid? framework = null, CancellationToken ct = default) => orderReader.ListOrdersAsync(account, framework, ct);
    public Task<OrderDetails> GetOrderAsync(Guid account, Guid id, CancellationToken ct = default) => orderReader.GetOrderAsync(account, id, ct);
    public Task<Guid> SaveOrderAsync(Guid account, Guid? id, LeasingOrder input, string reason, Guid request, CancellationToken ct = default) => orderWriter.SaveOrderAsync(account, id, input, reason, request, ct);
    public Task OrderActionAsync(Guid account, Guid id, Guid revision, LeasingOrderStatus status, string reason, Guid request, CancellationToken ct = default) => orderWriter.OrderActionAsync(account, id, revision, status, reason, request, ct);
    public Task LinkOrderAcquisitionAsync(Guid account, Guid acquisition, Guid revision, OrderDelivery delivery, CancellationToken ct = default) => acquisitionWriter.LinkOrderAcquisitionAsync(account, acquisition, revision, delivery, ct);
    // Suggestions only: required dimensions are validated by SaveAcquisitionCore on registration.
    public static LeasingClassificationInput DeliveryClassification(LeasingOrderLine line, LeasingItem item) => LeasingOrderPreviews.DeliveryClassification(line, item);
    public Task ReopenOrderAsync(Guid account, Guid id, Guid revision, string reason, Guid request, CancellationToken ct = default) => orderWriter.ReopenOrderAsync(account, id, revision, reason, request, ct);
    public Task<List<LeasingLimitChange>> LimitChangesAsync(Guid account, Guid framework, CancellationToken ct = default) => orderReader.LimitChangesAsync(account, framework, ct);
    public Task ProposeLimitAsync(Guid account, Guid framework, Guid revision, decimal limit, string reason, string? document, Guid request, CancellationToken ct = default) => orderWriter.ProposeLimitAsync(account, framework, revision, limit, reason, document, request, ct);
    public Task ApproveLimitAsync(Guid account, Guid id, CancellationToken ct = default) => orderWriter.ApproveLimitAsync(account, id, ct);
}

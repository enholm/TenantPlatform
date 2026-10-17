using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;

public sealed record AcquisitionLoad(Guid? Id, Guid? Framework, Guid Finance, Guid Owner, Guid Supplier, Guid? Order = null, Guid? Request = null, Guid? Invoice = null);
public sealed record AcquisitionFacts(LeasingAcquisition? Acquisition, LeasingFramework? OriginalFramework, LeasingFramework? TargetFramework,
    bool FinanceExists, bool SupplierExists, bool OwnerActiveMember, bool HasFinancing, List<LeasingInvoice> Invoices,
    List<LeasingInvoiceLine> InvoiceLines, List<LeasingOrderRealization> Realizations, List<LeasingOrder> Orders,
    LeasingOrderEvent? PreviousOrderRequest, LeasingInvoice? ApprovalInvoice,
    Dictionary<Guid,int> EquipmentCounts, Dictionary<Guid,decimal> UntrackedQuantities, LeasingClassificationCatalog Catalog);
public interface ILeasingAcquisitionData
{
    Task<ILeasingAcquisitionWork> BeginAsync(Guid account, CancellationToken ct);
}
// A single account-locked transaction, also used by invoice approval and order linking.
// Concrete methods preserve the existing intermediate SaveChanges boundaries.
public interface ILeasingAcquisitionWork : IAsyncDisposable
{
    Guid AccountId { get; }
    Task<AcquisitionFacts> LoadAsync(AcquisitionLoad request, CancellationToken ct);
    Task SaveAcquisitionAsync(LeasingAcquisition acquisition, LeasingHistory history, CancellationToken ct);
    Task SaveFinancingAsync(LeasingFinancingRevision? revision, CancellationToken ct);
    Task SaveOrdersAsync(IReadOnlyList<LeasingOrderEvent> events, IReadOnlyList<LeasingOrderRealization> added, CancellationToken ct);
    Task<LeasingCapacity> ReadCapacityAsync(Guid framework, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
}

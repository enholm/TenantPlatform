using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Organizations;
namespace TenantPlatform.Application.Leasing.Invoices;
public sealed record LeasingInvoicePurchase(LeasingAcquisition? Acquisition,LeasingFramework? Framework,bool HasOrderRealization,List<LeasingInvoice> Documents,List<LeasingInvoiceLine> Lines);
public sealed record LeasingInvoiceOrder(LeasingOrder? Order,LeasingFramework? Framework);
public sealed record LeasingInvoiceUploadFacts(LeasingAcquisition? Acquisition,LeasingFramework? Framework,bool HasOwnedRoot);
public interface ILeasingPurchaseInvoiceData
{
    Task<ILeasingPurchaseInvoiceWork> OpenAsync(Guid account,Guid? invoice,bool write,CancellationToken ct);
    Task<List<LeasingInvoice>> ReadListAsync(LeasingReadScope scope,CancellationToken ct);
    Task<LeasingInvoiceUploadFacts> ReadUploadFactsAsync(Guid account,Guid user,Guid? acquisition,CancellationToken ct);
    Task CleanupUncommittedAsync(Guid account,string storageKey);
}
public interface ILeasingPurchaseInvoiceWork:IAsyncDisposable
{
    LeasingInvoice? Invoice {get;set;}
    ILeasingAcquisitionWork AcquisitionWork {get;}
    Task<LeasingInvoicePurchase> ReadPurchaseAsync(Guid acquisition,CancellationToken ct);
    Task<LeasingFramework?> ReadFrameworkAsync(Guid framework,CancellationToken ct);
    Task<LeasingInvoiceOrder> ReadOrderAsync(Guid order,CancellationToken ct);
    Task<Organization?> ReadOrganizationAsync(Guid organization,CancellationToken ct);
    Task<List<LeasingInvoice>> ReadApprovedAsync(CancellationToken ct);
    Task<List<LeasingInvoiceInterpretation>> ReadInterpretationsAsync(CancellationToken ct);
    Task<List<LeasingInvoiceHistory>> ReadHistoryAsync(CancellationToken ct);
    Task<List<LeasingOption>> ReadAcquisitionOptionsAsync(LeasingReadScope scope,CancellationToken ct);
    Task<LeasingCapacity> ReadCapacityAsync(Guid framework,CancellationToken ct);
    Task<bool> HasDependenciesAsync(Guid invoice,Guid[] lines,Guid[] items,CancellationToken ct);
    Task SaveInvoiceAsync(LeasingInvoiceHistory history,CancellationToken ct);
    Task SaveFinancialHistoryAsync(LeasingHistory history,CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
}

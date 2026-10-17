using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Organizations;
namespace TenantPlatform.Application.Leasing;
public sealed class LeasingRentalInvoiceState
{
    public LeasingInvoice? Invoice {get;set;}
    public List<LeasingInvoice> AccountInvoices {get;init;}=[];
    public List<LeasingAcquisition> Acquisitions {get;init;}=[];
    public List<LeasingFramework> Frameworks {get;init;}=[];
    public List<LeasingOrder> Orders {get;init;}=[];
    public List<LeasingPaymentPlan> Plans {get;init;}=[];
    public List<LeasingInstallment> Installments {get;init;}=[];
    public List<LeasingFinancingRevision> Financing {get;init;}=[];
    public List<LeasingPaymentAllocation> Allocations {get;init;}=[];
    public List<LeasingLifecycle> Lifecycles {get;init;}=[];
    public List<Organization> Organizations {get;init;}=[];
    public List<LeasingInvoiceInterpretation> Interpretations {get;init;}=[];
    public List<LeasingPaymentEvent> History {get;init;}=[];
    public LeasingPaymentEvent? PreviousRequest {get;init;}
}
public interface ILeasingRentalInvoiceData
{
    Task<ILeasingRentalInvoiceWork> OpenAsync(Guid account,Guid? invoice,Guid? request,bool write,CancellationToken ct);
    Task CleanupUncommittedAsync(Guid account,string storageKey);
    Task DeleteFileAsync(Guid invoice,string storageKey);
}
public interface ILeasingRentalInvoiceWork:IAsyncDisposable
{
    LeasingRentalInvoiceState State {get;}
    Task FlushReversalAsync(CancellationToken ct);
    Task CommitAsync(IReadOnlyList<LeasingPaymentEvent> history,LeasingInvoiceHistory? uploadHistory,CancellationToken ct);
    Task DeleteDraftAsync(LeasingPaymentEvent history,CancellationToken ct);
}

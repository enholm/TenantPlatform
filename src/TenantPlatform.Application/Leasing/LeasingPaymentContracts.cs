using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed record PaymentPermissions(bool CanRegister, bool CanActivate, bool CanApproveInvoices, bool CanAcceptVariance);
public sealed record PaymentTermView(LeasingPlanTerm Term, LeasingInstallment Installment, decimal InvoicedNet, decimal InvoicedVat, LeasingBillingStatus Status, List<LeasingPaymentAllocation> Allocations);
public sealed record PaymentDetails(LeasingAcquisition Acquisition, List<LeasingFinancingRevision> Financing, List<LeasingPaymentPlan> Plans,
    List<PaymentTermView> Terms, List<LeasingInvoice> Invoices, List<LeasingDocument> Documents, List<LeasingPaymentEvent> History, PaymentPermissions Permissions);
public sealed record PaymentPlanRow(Guid AcquisitionId, string Acquisition, string FinanceCompany, string Currency, Guid? PlanId, int? Version, LeasingPlanStatus? Status, DateOnly? From, DateOnly? To, DateOnly? NextDue, bool NeedsReview);


public enum LeasingPaymentTarget { Acquisition,Plan,Installment }
public sealed class LeasingPaymentState
{
    public LeasingAcquisition? Acquisition {get;init;}
    public LeasingFramework? Framework {get;init;}
    public List<LeasingFinancingRevision> Financing {get;init;}=[];
    public List<LeasingPaymentPlan> Plans {get;init;}=[];
    public List<LeasingInstallment> Installments {get;init;}=[];
    public List<LeasingPaymentAllocation> Allocations {get;init;}=[];
    public List<LeasingInvoice> Invoices {get;init;}=[];
    public List<LeasingDocument> Documents {get;init;}=[];
    public List<LeasingPaymentEvent> History {get;init;}=[];
    public LeasingPaymentEvent? PreviousRequest {get;init;}
}
public sealed record LeasingPaymentList(List<LeasingAcquisition> Acquisitions,List<LeasingFinancingRevision> Financing,List<LeasingPaymentPlan> Plans,Dictionary<Guid,string> Organizations,string TimeZoneId);
public interface ILeasingPaymentData
{
    Task<ILeasingPaymentWork> OpenAsync(Guid account,Guid target,LeasingPaymentTarget kind,Guid? request,bool write,CancellationToken ct);
    Task<LeasingPaymentList> ReadListAsync(LeasingReadScope scope,string? search,string? currency,CancellationToken ct);
}
public interface ILeasingPaymentWork:IAsyncDisposable
{
    LeasingPaymentState State {get;}
    Task<bool> OrganizationExistsAsync(Guid organization,CancellationToken ct);
    Task<bool> MemberExistsAsync(Guid user,CancellationToken ct);
    Task FlushReplacedPlanAsync(CancellationToken ct);
    Task CommitAsync(LeasingPaymentEvent history,CancellationToken ct);
}

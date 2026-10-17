using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Leasing.Invoices;
using TenantPlatform.Core.Leasing;

namespace TenantPlatform.Web.Services.Leasing;

public sealed partial class LeasingService
{
    public static LeasingFinancingRevision? FinancingAt(IEnumerable<LeasingFinancingRevision> revisions,DateOnly date)=>LeasingPaymentService.FinancingAt(revisions,date);
    public Task AddFinancingRevisionAsync(Guid account,Guid acquisition,Guid acquisitionRevision,DateOnly effectiveFrom,LeasingFinancingSnapshot input,string reason,Guid request,CancellationToken ct=default)=>payments.AddFinancingRevisionAsync(account,acquisition,acquisitionRevision,effectiveFrom,input,reason,request,ct);
    public Task<PaymentDetails> GetPaymentsAsync(Guid account,Guid acquisition,CancellationToken ct=default)=>payments.GetPaymentsAsync(account,acquisition,ct);
    public Task<List<PaymentPlanRow>> ListPaymentPlansAsync(Guid account,string? search=null,string? currency=null,Guid? finance=null,LeasingPlanStatus? status=null,CancellationToken ct=default)=>payments.ListPaymentPlansAsync(account,search,currency,finance,status,ct);
    public Task<Guid> SavePaymentPlanAsync(Guid account,Guid acquisition,Guid? basedOn,List<LeasingPlanTerm> terms,string? notes,LeasingPlanSource source,Guid? document,Guid request,CancellationToken ct=default)=>payments.SavePaymentPlanAsync(account,acquisition,basedOn,terms,notes,source,document,request,ct);
    public Task ActivatePaymentPlanAsync(Guid account,Guid plan,Guid revision,string reason,bool invoicedChangesConfirmed,Guid request,CancellationToken ct=default)=>payments.ActivatePaymentPlanAsync(account,plan,revision,reason,invoicedChangesConfirmed,request,ct);
    public Task ReviewPaymentTermAsync(Guid account,Guid installment,Guid revision,bool billingComplete,bool acceptVariance,bool reviewPlan,string reason,Guid request,CancellationToken ct=default)=>payments.ReviewPaymentTermAsync(account,installment,revision,billingComplete,acceptVariance,reviewPlan,reason,request,ct);
}

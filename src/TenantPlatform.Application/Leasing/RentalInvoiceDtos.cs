using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed class RentalInvoiceReview
{
    public InvoiceData Data {get;set;}=new();
    public Guid FinanceOrganizationId {get;set;}
    public Guid? OriginalInvoiceId {get;set;}
    public string AgreementReference {get;set;}="";
    public string PeriodReference {get;set;}="";
    public bool ContentConfirmed {get;set;}
    public string Reason {get;set;}="";
    public List<RentalAllocationInput> Allocations {get;set;}=[];
}
public sealed class RentalAllocationInput
{
    public Guid InstallmentId {get;set;}
    public decimal Net {get;set;}
    public decimal Vat {get;set;}
    public Guid? CreditedAllocationId {get;set;}
}
public sealed record RentalMatchOption(Guid InstallmentId,Guid AcquisitionId,string Acquisition,string Reference,DateOnly From,DateOnly To,DateOnly Due,decimal? Net,decimal? Vat,string Currency,string AgreementReference,bool Suggested);
public sealed record RentalInvoiceDetails(LeasingInvoice Invoice,RentalInvoiceReview Review,List<LeasingInvoiceInterpretation> Interpretations,List<LeasingPaymentAllocation> Allocations,List<LeasingPaymentEvent> History,List<RentalMatchOption> Options,List<LeasingInvoice> CreditSources,List<LeasingPaymentAllocation> CreditAllocations,bool CanEdit,bool CanApprove);
public sealed record RentalInvoiceRow(LeasingInvoice Invoice,string FinanceName,decimal AllocatedNet,decimal AllocatedVat,List<string> Acquisitions);


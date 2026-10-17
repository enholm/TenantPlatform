using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Leasing.Invoices;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Web.Services.Leasing.Invoices;
using TenantPlatform.Web.Services.Agreements;
namespace TenantPlatform.Web.Services.Leasing;

public sealed partial class LeasingService
{
    public Task<bool> CanRegisterRentalInvoiceAsync(Guid account,CancellationToken ct=default)=>rentalInvoices.CanRegisterRentalInvoiceAsync(account,ct);
    public Task<Guid> NewRentalInvoiceAsync(Guid account,Guid request,CancellationToken ct=default)=>rentalInvoices.NewRentalInvoiceAsync(account,request,ct);
    public Task DeleteRentalInvoiceAsync(Guid account,Guid id,Guid revision,CancellationToken ct=default)=>rentalInvoices.DeleteRentalInvoiceAsync(account,id,revision,ct);
    public Task<Guid> UploadRentalInvoiceAsync(Guid account,string filename,Stream stream,CancellationToken ct=default)=>rentalInvoices.UploadRentalInvoiceAsync(account,filename,stream,ct);
    public Task<List<RentalInvoiceRow>> ListRentalInvoicesAsync(Guid account,string? search=null,string? currency=null,Guid? finance=null,LeasingInvoiceStatus? status=null,CancellationToken ct=default)=>rentalInvoices.ListRentalInvoicesAsync(account,search,currency,finance,status,ct);
    public Task<RentalInvoiceDetails> GetRentalInvoiceAsync(Guid account,Guid id,CancellationToken ct=default)=>rentalInvoices.GetRentalInvoiceAsync(account,id,ct);
    public Task SaveRentalReviewAsync(Guid account,Guid id,Guid revision,RentalInvoiceReview review,CancellationToken ct=default)=>rentalInvoices.SaveRentalReviewAsync(account,id,revision,review,ct);
    public Task UseRentalInterpretationAsync(Guid account,Guid id,Guid revision,Guid interpretation,CancellationToken ct=default)=>rentalInvoices.UseRentalInterpretationAsync(account,id,revision,interpretation,ct);
    public Task RetryRentalInterpretationAsync(Guid account,Guid id,Guid revision,CancellationToken ct=default)=>rentalInvoices.RetryRentalInterpretationAsync(account,id,revision,ct);
    public Task ApproveRentalInvoiceAsync(Guid account,Guid id,Guid revision,CancellationToken ct=default)=>rentalInvoices.ApproveRentalInvoiceAsync(account,id,revision,ct);
    public Task CorrectRentalAllocationsAsync(Guid account,Guid id,Guid revision,List<RentalAllocationInput> inputs,string reason,Guid request,CancellationToken ct=default)=>rentalInvoices.CorrectRentalAllocationsAsync(account,id,revision,inputs,reason,request,ct);
    public Task ReverseRentalInvoiceAsync(Guid account,Guid id,Guid revision,string reason,Guid request,CancellationToken ct=default)=>rentalInvoices.ReverseRentalInvoiceAsync(account,id,revision,reason,request,ct);
    public Task<AgreementDownload> DownloadRentalInvoiceAsync(Guid account,Guid id,CancellationToken ct=default)=>rentalInvoices.DownloadRentalInvoiceAsync(account,id,ct);
}

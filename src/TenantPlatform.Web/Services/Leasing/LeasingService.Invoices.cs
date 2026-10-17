using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Leasing.Invoices;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Leasing.Invoices;

namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    public Task<(List<LeasingInvoice> Documents,List<LeasingInvoiceLine> Lines)> PurchaseInvoicesAsync(Guid account,Guid acquisition,CancellationToken ct=default)=>purchaseInvoices.PurchaseInvoicesAsync(account,acquisition,ct);
    public Task<List<LeasingInvoice>> ListInvoicesAsync(Guid account,CancellationToken ct=default)=>purchaseInvoices.ListInvoicesAsync(account,ct);
    public Task<Guid> UploadInvoiceAsync(Guid account,Guid? acquisition,string fileName,Stream content,CancellationToken ct=default)=>purchaseInvoices.UploadInvoiceAsync(account,acquisition,fileName,content,ct);
    public Task<InvoiceDetails> GetInvoiceAsync(Guid account,Guid id,CancellationToken ct=default)=>purchaseInvoices.GetInvoiceAsync(account,id,ct);
    public Task<AgreementDownload> DownloadInvoiceAsync(Guid account,Guid id,CancellationToken ct=default)=>purchaseInvoices.DownloadInvoiceAsync(account,id,ct);
    public Task SaveInvoiceReviewAsync(Guid account,Guid id,Guid revision,InvoiceReview review,CancellationToken ct=default)=>purchaseInvoices.SaveInvoiceReviewAsync(account,id,revision,review,ct);
    public Task RetryInvoiceAsync(Guid account,Guid id,Guid revision,CancellationToken ct=default)=>purchaseInvoices.RetryInvoiceAsync(account,id,revision,ct);
    public Task UseInterpretationAsync(Guid account,Guid id,Guid revision,Guid interpretationId,CancellationToken ct=default)=>purchaseInvoices.UseInterpretationAsync(account,id,revision,interpretationId,ct);
    public Task RejectInvoiceAsync(Guid account,Guid id,Guid revision,string reason,CancellationToken ct=default)=>purchaseInvoices.RejectInvoiceAsync(account,id,revision,reason,ct);
    public Task<InvoiceApprovalPreview> PreviewInvoiceAsync(Guid account,Guid id,Guid revision,CancellationToken ct=default)=>purchaseInvoices.PreviewInvoiceAsync(account,id,revision,ct);
    public Task<Guid> ApproveInvoiceAsync(Guid account,Guid id,Guid revision,CancellationToken ct=default)=>purchaseInvoices.ApproveInvoiceAsync(account,id,revision,ct);
    public Task ReverseInvoiceAsync(Guid account,Guid id,Guid revision,string reason,CancellationToken ct=default)=>purchaseInvoices.ReverseInvoiceAsync(account,id,revision,reason,ct);
    public static LeasingItem ImportedItem(InvoiceDataLine line,decimal net,decimal vat)=>LeasingPurchaseInvoiceService.ImportedItem(line,net,vat);
}

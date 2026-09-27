using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Web.Services.Leasing.Invoices;
public sealed class InvoiceReview
{
    public InvoiceData Data { get; set; } = new();
    public Guid? AcquisitionId { get; set; }
    public Guid? AcquisitionRevision { get; set; }
    public LeasingAcquisition NewAcquisition { get; set; } = new();
    public bool PurchaseDateConfirmed { get; set; }
    public bool ContentConfirmed { get; set; }
    public string ReviewReason { get; set; } = "";
    public string DuplicateOverrideReason { get; set; } = "";
    public List<InvoiceLineMatch> Matches { get; set; } = [];
}
public sealed class InvoiceLineMatch
{
    public Guid ReviewLineId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? CreditedLineId { get; set; }
    public LeasingClassificationInput Classification { get; set; } = new();
}
public sealed record InvoiceDetails(LeasingInvoice Invoice, InvoiceReview Review, List<LeasingInvoiceInterpretation> Interpretations,
    List<LeasingInvoiceHistory> History, List<LeasingOption> Acquisitions, LeasingAcquisition? Acquisition,
    List<LeasingInvoiceLine> DocumentedLines, List<LeasingInvoice> Documents, List<string> DuplicateWarnings);
public sealed record InvoiceApprovalPreview(decimal CurrentPurchase, decimal Change, decimal NewPurchase, decimal CurrentUsed, decimal NewUsed, decimal? Limit, bool? CreditReleasesLimit);

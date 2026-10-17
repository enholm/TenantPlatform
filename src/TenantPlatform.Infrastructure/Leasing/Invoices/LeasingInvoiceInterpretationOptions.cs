namespace TenantPlatform.Infrastructure.Leasing.Invoices;
public sealed class LeasingInvoiceInterpretationOptions
{
    public bool Enabled { get; set; }
    public string PdfTextExecutable { get; set; } = "pdftotext";
}

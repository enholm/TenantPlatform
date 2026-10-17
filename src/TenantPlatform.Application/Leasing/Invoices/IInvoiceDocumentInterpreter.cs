using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing.Invoices;
public sealed record InvoiceInterpretationResult(InvoiceData Data, string Version, string Text, List<string> Warnings);
public interface IInvoiceDocumentInterpreter
{
    Task<InvoiceInterpretationResult> InterpretAsync(LeasingInvoice invoice, CancellationToken ct);
}

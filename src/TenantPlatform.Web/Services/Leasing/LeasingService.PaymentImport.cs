using TenantPlatform.Application.Leasing;
namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    public Task<(Guid Document, PaymentImportGrid Grid)> UploadPaymentPlanSourceAsync(Guid account, Guid acquisition, string fileName, Stream stream, CancellationToken ct = default) => documents.UploadPaymentPlanSourceAsync(account, acquisition, fileName, stream, ct);
    public Task<PaymentImportGrid> ReadPaymentPlanSourceAsync(Guid account, Guid acquisition, Guid document, string? sheet = null, CancellationToken ct = default) => documents.ReadPaymentPlanSourceAsync(account, acquisition, document, sheet, ct);
}

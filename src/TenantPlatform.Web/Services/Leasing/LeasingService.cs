using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Leasing.Invoices;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Leasing;

namespace TenantPlatform.Web.Services.Leasing;

public sealed partial class LeasingService(IAgreementDocumentStorage storage, LeasingClassificationService classifications, LeasingFollowupService followups, LeasingFrameworkWriter frameworkWriter, LeasingReader reader, LeasingDocumentService documents, LeasingAcquisitionWriter acquisitionWriter, LeasingOrderReader orderReader, LeasingOrderWriter orderWriter, LeasingEquipmentService equipment, LeasingLifecycleService lifecycle, LeasingPaymentService payments, LeasingPurchaseInvoiceService purchaseInvoices, LeasingRentalInvoiceService rentalInvoices, LeasingReportService reports)
{
    public long MaxFileSizeBytes => storage.MaxFileSizeBytes;
    public Task<LeasingOptions> OptionsAsync(Guid account, CancellationToken ct = default) => reader.OptionsAsync(account, ct);
    public Task<LeasingPage> ListAsync(Guid account, string? search, string kind, string? status, Guid? owner, int page, CancellationToken ct = default) => reader.ListAsync(account, search, kind, status, owner, page, ct);
    public Task<LeasingDetails> GetAsync(Guid account, Guid id, bool framework, CancellationToken ct = default) => reader.GetAsync(account, id, framework, ct);
    public Task<LeasingAcquisition> NewAcquisitionAsync(Guid account, Guid? frameworkId, CancellationToken ct = default) => reader.NewAcquisitionAsync(account, frameworkId, ct);

    public Task<Guid> SaveFrameworkAsync(Guid account, Guid? id, LeasingFramework input, string reason, CancellationToken ct = default) => frameworkWriter.SaveFrameworkAsync(account, id, input, reason, ct);

    public Task<Guid> SaveAcquisitionAsync(Guid account, Guid? id, LeasingAcquisition input, string reason, CancellationToken ct = default, IReadOnlyDictionary<int, LeasingClassificationInput>? classifications = null, OrderDelivery? delivery = null) => acquisitionWriter.SaveAcquisitionAsync(account, id, input, reason, ct, classifications, delivery);
    public Task CancelAsync(Guid account, Guid id, Guid revision, string reason, CancellationToken ct = default) => acquisitionWriter.CancelAsync(account, id, revision, reason, ct);

    public Task UploadAsync(Guid account, Guid parentId, bool framework, Guid revision, string fileName, Stream content, CancellationToken ct = default) => documents.UploadAsync(account, parentId, framework, revision, fileName, content, ct);
    public Task<AgreementDownload> DownloadAsync(Guid account, Guid id, CancellationToken ct = default) => documents.DownloadAsync(account, id, ct);
}

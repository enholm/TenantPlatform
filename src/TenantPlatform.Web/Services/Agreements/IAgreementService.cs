using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public interface IAgreementService
{
    Task RecordTerminationAsync(Guid accountId, Guid agreementId, AgreementTerminationRequest request, CancellationToken cancellationToken = default);
    Task<AgreementLinesDto> GetLinesAsync(Guid accountId, Guid agreementId, CancellationToken ct = default);
    Task<Guid> SaveLineAsync(Guid accountId, Guid agreementId, Guid? lineId, SaveAgreementLineRequest request, CancellationToken ct = default);
    Task CloseLineAsync(Guid accountId, Guid agreementId, Guid lineId, Guid revision, DateOnly date, bool deactivate, string reason, CancellationToken ct = default);
    Task AddDeliveryGroupAsync(Guid accountId, Guid agreementId, Guid revision, string name, CancellationToken ct = default);
    Task<AgreementForecastDto> ForecastAsync(Guid accountId, Guid agreementId, DateOnly from, DateOnly to, CancellationToken ct = default);
    long MaxFileSizeBytes { get; }
    Task<AgreementPageDto> ListAsync(Guid accountId, AgreementFilter filter, CancellationToken cancellationToken = default);
    Task<AgreementDetailsDto> GetAsync(Guid accountId, Guid agreementId, CancellationToken cancellationToken = default);
    Task<AgreementOptionsDto> GetOptionsAsync(Guid accountId, Guid? agreementId = null, CancellationToken cancellationToken = default);
    Task<Guid> CreateAsync(Guid accountId, SaveAgreementRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid accountId, Guid agreementId, SaveAgreementRequest request, CancellationToken cancellationToken = default);
    Task SetAccessAsync(Guid accountId, Guid agreementId, Guid userId, AgreementAccessLevel? level, Guid revision, CancellationToken cancellationToken = default);
    Task UploadAsync(Guid accountId, Guid agreementId, Guid revision, string fileName, Stream content,
        AgreementDocumentCategory category, string? description, CancellationToken cancellationToken = default);
    Task<AgreementDownload> DownloadAsync(Guid accountId, Guid documentId, CancellationToken cancellationToken = default);
}

using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public interface IAgreementService
{
    Task<AgreementIndexRegister> GetIndicesAsync(Guid accountId, CancellationToken ct = default);
    Task<Guid> CreateIndexAsync(Guid accountId, string code, string name, string description, string source, AgreementIndexResolution resolution, CancellationToken ct = default);
    Task RecordIndexValueAsync(Guid accountId, Guid indexId, DateOnly period, decimal value, DateOnly? published, string reason, CancellationToken ct = default);
    Task<AgreementProcessingView> GetProcessingAsync(Guid accountId, Guid agreementId, CancellationToken ct = default);
    Task<Guid> SaveAdjustmentRuleAsync(Guid accountId, Guid agreementId, Guid revision, AgreementAdjustmentRule input, CancellationToken ct = default);
    Task<List<AgreementAdjustmentCandidate>> FindAdjustmentsAsync(Guid accountId, Guid agreementId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<Guid> ProposeAdjustmentAsync(Guid accountId, Guid agreementId, Guid lineId, DateOnly date, decimal? chosenPercent, CancellationToken ct = default);
    Task DecideAdjustmentAsync(Guid accountId, Guid agreementId, Guid proposalId, bool approve, string comment, CancellationToken ct = default);
    Task ChangePriceAsync(Guid accountId, Guid agreementId, Guid lineId, Guid revision, DateOnly date, decimal price, AgreementPriceTiming timing, string reason, CancellationToken ct = default);
    Task<AgreementForecastDto> PreviewBasisAsync(Guid accountId, Guid agreementId, AgreementDirection direction, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<AgreementBasisRun> GenerateBasisAsync(Guid accountId, Guid agreementId, AgreementDirection direction, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<List<AgreementBasisSummary>> ListBasisAsync(Guid accountId, AgreementDirection direction, CancellationToken ct = default);
    Task<AgreementBasisDetails> GetBasisAsync(Guid accountId, Guid basisId, CancellationToken ct = default);
    Task RegenerateBasisAsync(Guid accountId, Guid basisId, int revision, string reason, CancellationToken ct = default);
    Task ApproveBasisAsync(Guid accountId, Guid basisId, int revision, CancellationToken ct = default);
    Task CancelBasisAsync(Guid accountId, Guid basisId, int revision, string reason, CancellationToken ct = default);
    Task<Guid> CreateCorrectionAsync(Guid accountId, Guid originalId, string reason, CancellationToken ct = default);
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

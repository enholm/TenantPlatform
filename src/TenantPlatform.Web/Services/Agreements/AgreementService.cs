using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Web.Services.Agreements;
public partial class AgreementService(
    IAgreementDocumentStorage storage,
    IAgreementListService agreementList,
    IAgreementAttentionService agreementAttention,
    IAgreementDashboardService agreementDashboard,
    IAgreementInsightsService agreementInsights,
    IAgreementDetailsReader agreementDetails,
    IAgreementFollowupReader agreementFollowup,
    IAgreementDeadlineReader agreementDeadlines,
    AgreementInitializationService agreementInitialization,
    AgreementFollowupWriter followupWriter,
    AgreementIndexService indexService,
    AgreementDocumentService documents,
    AgreementManagementService management,
    AgreementFinancialService financial,
    AgreementAnalysisService analysis) : IAgreementService, IAgreementFollowupService, IAgreementAnalysisService
{
    public long MaxFileSizeBytes => storage.MaxFileSizeBytes;
    public Task<AgreementPageDto> ListAsync(Guid accountId, AgreementFilter filter, CancellationToken cancellationToken = default) =>
        agreementList.ListAsync(accountId, filter, cancellationToken);

    // GUI compatibility orchestration can write. Direct Application ReadAsync is strictly read-only.
    public async Task<AgreementDetailsDto> GetAsync(Guid accountId, Guid agreementId, CancellationToken cancellationToken = default)
    {
        var details = await agreementDetails.ReadAsync(accountId, agreementId, cancellationToken);
        if (!details.DeadlinesInitialized)
        {
            // Authorize the concrete resource first; never backfill unrelated agreements from a read.
            await agreementInitialization.EnsureAgreementAsync(accountId, agreementId, cancellationToken);
            details = await agreementDetails.ReadAsync(accountId, agreementId, cancellationToken);
        }
        return details;
    }

    public Task<AgreementOptionsDto> GetOptionsAsync(Guid accountId, Guid? agreementId = null, CancellationToken cancellationToken = default) => management.GetOptionsAsync(accountId, agreementId, cancellationToken);
    public Task<Guid> CreateAsync(Guid accountId, SaveAgreementRequest request, CancellationToken cancellationToken = default) => management.CreateAsync(accountId, request, cancellationToken);
    public Task UpdateAsync(Guid accountId, Guid agreementId, SaveAgreementRequest request, CancellationToken cancellationToken = default) => management.UpdateAsync(accountId, agreementId, request, cancellationToken);
    public Task SetAccessAsync(Guid accountId, Guid agreementId, Guid userId, AgreementAccessLevel? level, Guid revision, CancellationToken cancellationToken = default) => management.SetAccessAsync(accountId, agreementId, userId, level, revision, cancellationToken);

    public Task UploadAsync(Guid accountId, Guid agreementId, Guid revision, string fileName, Stream content, AgreementDocumentCategory category, string? description, CancellationToken cancellationToken = default) =>
        documents.UploadAsync(accountId, agreementId, revision, fileName, content, category, description, cancellationToken);
    public Task<AgreementDownload> DownloadAsync(Guid accountId, Guid documentId, CancellationToken cancellationToken = default) => documents.DownloadAsync(accountId, documentId, cancellationToken);

}

using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Web.Services.Agreements;
public partial class AgreementService
{
    public const int MaxAnalysisFiles = AgreementAnalysisService.MaxAnalysisFiles;
    public const long MaxAnalysisTotalBytes = AgreementAnalysisService.MaxAnalysisTotalBytes;
    public long MaxAnalysisFileSizeBytes => analysis.MaxAnalysisFileSizeBytes;
    public static string NormalizeOrganizationName(string value) => AgreementAnalysisService.NormalizeOrganizationName(value);
    public static string NormalizeOrganizationNumber(string? value) => AgreementAnalysisService.NormalizeOrganizationNumber(value);
    public Task<AnalysisDraftDto> StartAnalysisDraftAsync(Guid accountId, Guid? agreementId, CancellationToken ct = default) => analysis.StartAnalysisDraftAsync(accountId, agreementId, ct);
    public Task<AnalysisDraftDto> AddAnalysisFileAsync(Guid accountId, Guid draftId, string fileName, Stream content, AgreementDocumentCategory category, CancellationToken ct = default) => analysis.AddAnalysisFileAsync(accountId, draftId, fileName, content, category, ct);
    public Task<AnalysisDraftDto> RemoveAnalysisFileAsync(Guid accountId, Guid draftId, Guid fileId, CancellationToken ct = default) => analysis.RemoveAnalysisFileAsync(accountId, draftId, fileId, ct);
    public Task<AnalysisDraftDto> AnalyzeAsync(Guid accountId, Guid draftId, CancellationToken ct = default) => analysis.AnalyzeAsync(accountId, draftId, ct);
    public Task<List<AnalysisOrganizationDto>> FindAnalysisOrganizationsAsync(Guid accountId, Guid draftId, string name, string number, CancellationToken ct = default) => analysis.FindAnalysisOrganizationsAsync(accountId, draftId, name, number, ct);
    public Task<Guid> ApproveAnalysisAsync(Guid accountId, Guid draftId, ApproveAnalysisRequest request, CancellationToken ct = default) => analysis.ApproveAnalysisAsync(accountId, draftId, request, ct);
    public Task<AgreementDownload> DownloadAnalysisFileAsync(Guid accountId, Guid draftId, Guid fileId, CancellationToken ct = default) => analysis.DownloadAnalysisFileAsync(accountId, draftId, fileId, ct);
    public Task<List<AgreementAnalysis>> GetAnalysesAsync(Guid accountId, Guid agreementId, CancellationToken ct = default) => analysis.GetAnalysesAsync(accountId, agreementId, ct);
    public Task DiscardAnalysisAsync(Guid accountId, Guid draftId, CancellationToken ct = default) => analysis.DiscardAnalysisAsync(accountId, draftId, ct);
}

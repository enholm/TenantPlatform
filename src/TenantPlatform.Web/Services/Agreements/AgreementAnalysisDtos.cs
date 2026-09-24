using System.Text.Json;
using System.Text.Json.Serialization;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Organizations;

namespace TenantPlatform.Web.Services.Agreements;

public class AgreementAnalysisOptions
{
    public string ApiKey { get; set; } = "";
    // Explicitly configured: the deployment chooses a vision model supporting Structured Outputs.
    public string Model { get; set; } = "";
}
public record AnalysisDocumentResult(Guid DocumentId, bool Processed, string Explanation);
public record AnalysisSource(Guid DocumentId, string Quote, string Section, int? Page);
public record AnalysisFinding(AgreementFindingCategory Category, AgreementFindingStatus Status,
    string Value, string Parties, string Explanation, List<AnalysisSource> Sources);
public record AnalysisCounterparty(string Name, string OrganizationNumber, string Address,
    bool RequiresClarification, string Explanation);
public record ContractAnalysisResult(AnalysisCounterparty Counterparty, List<string> Parties,
    List<AnalysisDocumentResult> Documents, List<AnalysisFinding> Findings, List<string> Warnings);
public record AnalysisFileDto(Guid Id, string FileName, long Size, AgreementDocumentCategory Category);
public record AnalysisDraftDto(Guid Id, List<AnalysisFileDto> Files, ContractAnalysisResult? Result);
public record AnalysisOrganizationDto(Guid Id, string Name, string? OrganizationNumber);
public class ApproveAnalysisRequest
{
    public SaveAgreementRequest Agreement { get; set; } = new();
    public Guid? OrganizationId { get; set; }
    public string CounterpartyName { get; set; } = "";
    public string OrganizationNumber { get; set; } = "";
    public string Address { get; set; } = "";
    public OrganizationType OrganizationType { get; set; } = OrganizationType.ServiceProvider;
    public bool CounterpartyConfirmed { get; set; }
    public List<AnalysisFindingCorrection> Findings { get; set; } = [];
}
public class AnalysisFindingCorrection
{
    public string Value { get; set; } = "";
    public string Parties { get; set; } = "";
    public AgreementFindingStatus Status { get; set; }
}
public interface IAgreementAnalysisService
{
    long MaxAnalysisFileSizeBytes { get; }
    Task<AnalysisDraftDto> StartAnalysisDraftAsync(Guid accountId, Guid? agreementId, CancellationToken ct = default);
    Task<AnalysisDraftDto> AddAnalysisFileAsync(Guid accountId, Guid draftId, string fileName, Stream content, AgreementDocumentCategory category, CancellationToken ct = default);
    Task<AnalysisDraftDto> RemoveAnalysisFileAsync(Guid accountId, Guid draftId, Guid fileId, CancellationToken ct = default);
    Task<AnalysisDraftDto> AnalyzeAsync(Guid accountId, Guid draftId, CancellationToken ct = default);
    Task<List<AnalysisOrganizationDto>> FindAnalysisOrganizationsAsync(Guid accountId, Guid draftId, string name, string number, CancellationToken ct = default);
    Task<Guid> ApproveAnalysisAsync(Guid accountId, Guid draftId, ApproveAnalysisRequest request, CancellationToken ct = default);
    Task DiscardAnalysisAsync(Guid accountId, Guid draftId, CancellationToken ct = default);
    Task<AgreementDownload> DownloadAnalysisFileAsync(Guid accountId, Guid draftId, Guid fileId, CancellationToken ct = default);
    Task<List<AgreementAnalysis>> GetAnalysesAsync(Guid accountId, Guid agreementId, CancellationToken ct = default);
}
public static class ContractAnalysisJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
}

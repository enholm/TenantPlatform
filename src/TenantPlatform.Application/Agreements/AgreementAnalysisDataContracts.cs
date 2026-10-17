using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Organizations;
namespace TenantPlatform.Application.Agreements;

public interface IAgreementAnalysisData
{
    Task<IAgreementAnalysisTransaction> BeginAsync(Guid account, Guid? draft, Guid actor, CancellationToken ct);
    Task<Guid> ReadAgreementRevisionAsync(Guid account, Guid agreement, CancellationToken ct);
    Task<List<AgreementAnalysis>> ReadApprovedAsync(Guid account, Guid agreement, CancellationToken ct);
    Task RecordUsageAsync(Guid account, Guid draft, Guid attempt, ContractApiReply reply, CancellationToken ct);
    Task ResetAttemptAsync(Guid account, Guid draft, Guid attempt, CancellationToken ct);
    Task<bool> HasFileMetadataAsync(Guid account, string key, CancellationToken ct);
}
public interface IAgreementAnalysisTransaction : IAsyncDisposable
{
    AgreementAnalysis? Draft { get; set; }
    List<AgreementDocument> Documents { get; }
    AgreementAiUsage? Usage { get; set; }
    Task<string> ReadAccountNameAsync(CancellationToken ct);
    Task<List<Organization>> ReadOrganizationsAsync(bool lockAccount, CancellationToken ct);
    Task PersistOrganizationAsync(Organization organization, CancellationToken ct);
    Task<AgreementManagementState> PrepareApprovalAsync(Guid? agreement, CancellationToken ct);
    Task SynchronizeApprovalAsync(DateTimeOffset now, Guid user, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}

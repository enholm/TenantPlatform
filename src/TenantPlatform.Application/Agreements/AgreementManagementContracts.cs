using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public sealed record AgreementReferenceState(List<OrganizationElement> Elements, List<GeographicArea> Areas, AgreementOptionsDto Options);
public sealed class AgreementManagementState
{
    public Agreement? Agreement { get; set; }
    public required AgreementReferenceState References { get; init; }
    public bool HasActivatedLines { get; init; }
    public List<AgreementLineVersion> LineVersions { get; init; } = [];
    public List<AgreementAdjustmentProposal> Proposals { get; init; } = [];
    public List<AgreementIndexSelection> Selections { get; init; } = [];
    public List<AgreementAccess> Grants { get; init; } = [];
    public List<AgreementDeadline> Deadlines { get; init; } = [];
    public List<AgreementNoticeHistory> NoticeHistory { get; } = [];
    public List<AgreementDeadlineHistory> History { get; } = [];
}
public interface IAgreementManagementData
{
    Task<AgreementReferenceState> ReadReferencesAsync(Guid account, CancellationToken ct);
    Task<Agreement?> ReadAgreementAsync(Guid account, Guid agreement, CancellationToken ct);
    Task<IAgreementManagementTransaction> BeginAsync(Guid account, Guid? agreement, CancellationToken ct);
}
public interface IAgreementManagementTransaction : IAsyncDisposable
{
    AgreementManagementState State { get; }
    Task SynchronizeAsync(DateTimeOffset now, Guid user, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
}

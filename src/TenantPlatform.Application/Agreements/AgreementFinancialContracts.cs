using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public sealed class AgreementFinancialState
{
    public required Agreement Agreement { get; init; }
    public string AccountName { get; init; } = "";
    public string CounterpartyName { get; init; } = "";
    public List<AgreementBasis> Bases { get; init; } = [];
    public List<AgreementBasisSnapshot> Snapshots { get; init; } = [];
    public List<AgreementBasisEvent> Events { get; init; } = [];
    public List<AgreementAdjustmentProposal> Proposals { get; init; } = [];
    public List<AgreementLine> Lines { get; init; } = [];
    public List<AgreementLineVersion> Versions { get; init; } = [];
    public List<AgreementPriceVersion> Prices { get; init; } = [];
    public List<AgreementIndexSelection> Selections { get; init; } = [];
    public List<AgreementIndex> Indices { get; init; } = [];
    public List<AgreementIndexValue> IndexValues { get; init; } = [];
    public List<AgreementDocumentDto> Documents { get; init; } = [];
    public Dictionary<Guid, string> ActorNames { get; init; } = [];
}
public interface IAgreementFinancialData
{
    Task<Guid?> ReadBasisAgreementAsync(Guid account, Guid basis, CancellationToken ct);
    Task<List<AgreementBasisListRow>> ReadBasisListAsync(AgreementListScope scope, AgreementDirection direction, CancellationToken ct);
    Task<List<AgreementBulkBasisOption>> ReadBulkOptionsAsync(AgreementListScope scope, CancellationToken ct);
    Task<AgreementPendingApprovalData> ReadPendingApprovalsAsync(AgreementListScope scope, AgreementBasisApprovalFilter filter, CancellationToken ct);
    void ReportFailure(Exception exception, Guid resource);
    Task<IAgreementFinancialTransaction> BeginAsync(Guid account, Guid agreement, bool write, CancellationToken ct);
}
public interface IAgreementFinancialTransaction : IAsyncDisposable
{
    AgreementFinancialState State { get; }
    Task CommitAsync(CancellationToken ct);
}

public sealed record AgreementBasisListRow(AgreementBasis Basis, AgreementBasisSnapshot Snapshot);
public sealed record AgreementBasisCandidate(Guid Id, Guid AgreementId, AgreementDirection Direction, DateOnly InvoiceDate, string Title, string Party, bool IsArchived, Guid CounterpartyOrganizationId);
public sealed record AgreementPendingApprovalData(List<AgreementBulkBasisOption> Options, List<AgreementBasisCandidate> Candidates);

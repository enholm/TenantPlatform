using TenantPlatform.Application.Agreements;
namespace TenantPlatform.Application.PersonalPages;

public sealed record AgreementQueryResult(string SchemaVersion, Guid AccountId, AgreementReportSelection Selection,
    int RequestedPage, int Page, int PageSize, int TotalCount, int ReturnedCount, bool HasMore, int? NextPage,
    bool Complete, bool SnapshotConsistent, bool LimitReached, AgreementReportRow[] Items);
public sealed record AgreementAggregateResult(string SchemaVersion, Guid AccountId, AgreementReportSelection Selection,
    string GroupBy, int TotalCount, AgreementReportGroup[] Groups, bool Complete, bool SnapshotConsistent,
    bool CountsUniqueAgreements, bool GroupsOverlap, string MissingKey);
public sealed record AgreementDetailsResult(string SchemaVersion, Guid AccountId, bool Complete, bool SnapshotConsistent, AgreementReportRow Agreement);

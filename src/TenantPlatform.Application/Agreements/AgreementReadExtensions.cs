using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Agreements;

// Ordinary current business state only. No GUI actions, people, grants or raw notice snapshots.
public sealed record AgreementBusinessFields(string? Description, string? Terms, AgreementDirection? Direction,
    string? Currency, AgreementReference? Index, bool IndexSetupNeedsReview, AgreementForm Form,
    AgreementNoticeMode NoticeMode, int? NoticeCount, AgreementNoticeUnit? NoticeUnit,
    DateOnly CurrentPeriodStartDate, bool AutoRenew, int? RenewalMonths, DateOnly? TerminationEffectiveDate,
    int? TerminationNoticeCount, AgreementNoticeUnit? TerminationNoticeUnit, DateOnly? CessationDate,
    DateTimeOffset? TerminationRegisteredUtc, bool DeadlinesInitialized, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);
public sealed record AgreementBusinessDetails(AgreementReportRow Agreement, AgreementBusinessFields Business);
public sealed record FollowupEvent(Guid Id, Guid DeadlineId, DateTimeOffset CreatedUtc, AgreementHistoryKind Kind);
public sealed record AgreementEventPage(int Page, int Total, FollowupEvent[] Items);
public enum AgreementRelationKind { Counterparty, Building, Unit, OrganizationElement, GeographicArea }
public sealed record AgreementRelation(Guid Id, string Name);
public sealed record AgreementRelationPage(int Page, int Total, AgreementRelation[] Items);

public interface IAgreementReadExtensionData
{
    Task<AgreementBusinessDetails> DetailsAsync(AgreementReadScope scope, CancellationToken ct);
    Task<AgreementEventPage> HistoryAsync(AgreementReadScope scope, int page, CancellationToken ct);
    Task<AgreementRelationPage> RelationsAsync(AgreementListScope scope, AgreementRelationKind kind, string? search, int page, CancellationToken ct);
}

public sealed class AgreementReadExtensions(IAuthorizationService authorization, IAgreementReadExtensionData data)
{
    public const int PageSize = 25;
    private static void Page(int page) { if (page is < 1 or > 10000) throw new ArgumentException(); }
    public async Task<AgreementBusinessDetails> DetailsAsync(Guid account, Guid agreement, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var scope = await authorization.GetAgreementReadScopeAsync(account, agreement, ct);
        return await data.DetailsAsync(scope, ct);
    }
    public async Task<AgreementEventPage> HistoryAsync(Guid account, Guid agreement, int page = 1, CancellationToken ct = default)
    {
        Page(page); ct.ThrowIfCancellationRequested();
        var scope = await authorization.GetAgreementReadScopeAsync(account, agreement, ct);
        return await data.HistoryAsync(scope, page, ct);
    }
    // A relation is readable here only when used by a currently visible agreement. This is
    // narrower than the write editor's catalog and does not confer register/detail permission.
    public async Task<AgreementRelationPage> RelationsAsync(Guid account, AgreementRelationKind kind, string? search = null, int page = 1, CancellationToken ct = default)
    {
        Page(page); ct.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(kind) || search?.Length > 200 || search?.Any(char.IsControl) == true) throw new ArgumentException();
        var scope = await authorization.GetAgreementListScopeAsync(account, ct);
        return await data.RelationsAsync(scope, kind, search?.Trim(), page, ct);
    }
}
